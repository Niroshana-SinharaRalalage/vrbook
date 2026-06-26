# Runbook: Entra cutover rollback

> **Status**: Authoritative for VrBook.
> **Owner**: Identity (A1) + Platform (A2).
> **Last updated**: 2026-06-26.
> **Use this when**: a production Entra cutover fails mid-flight or causes user-facing breakage post-deploy. STOP making forward changes; follow this playbook.

The cutover has multiple revert points. Pick the **least invasive** one that restores service.

---

## 0. Decision flow

Ask in order:

1. **Has any user-facing traffic seen the broken state?**
   - No → rollback isn't needed yet; fix in place. The cutover script's Phase A and B run pre-deploy; if you're failing there, no user impact yet. Stop, fix, restart from where you stopped.
   - Yes → proceed to question 2.

2. **Is the broken state a sign-in regression (users can't sign in) or an authorization regression (users sign in but see wrong things)?**
   - Sign-in regression → Section 1 (Revert deployment).
   - Authorization regression → Section 2 (Temporary DevAuth re-enable; isolate the offending config change).

3. **Is the offending change in code, config, or Entra portal state?**
   - Code → revert the container revision (Section 1).
   - Config (env vars, KV secrets) → patch the env var directly (Section 3).
   - Entra portal state (App Roles, redirect URIs) → restore the portal config (Section 4).

---

## 1. Revert deployment (Container App revision rollback)

Container Apps keeps the previous revision alongside the new one. Traffic can be redirected to the previous revision in seconds.

```powershell
# 1. List recent revisions, newest first
az containerapp revision list -n ca-vrbook-api-prod -g rg-vrbook-prod `
    --query "reverse(sort_by([], &properties.createdTime))[:5].{rev:name, active:properties.active, image:properties.template.containers[0].image, created:properties.createdTime}" -o table

# 2. Pick the last known-good revision (one BEFORE the cutover). Let's say its name is ca-vrbook-api-prod--0000220.
$lastGood = 'ca-vrbook-api-prod--0000220'
$brokenRev = 'ca-vrbook-api-prod--0000221'

# 3. Set traffic 100% to last-good
az containerapp ingress traffic set -n ca-vrbook-api-prod -g rg-vrbook-prod `
    --revision-weight "$lastGood=100" "$brokenRev=0"

# 4. Verify
az containerapp ingress traffic show -n ca-vrbook-api-prod -g rg-vrbook-prod -o table
```

**Time-to-restore**: ~30 seconds. The DNS / Front Door layer is unaffected; only the container revision behind it changes.

Same procedure for the web container (`ca-vrbook-web-prod`).

After rollback, document:
- The broken revision name.
- The commit SHA that built it.
- A 1-line summary of the symptom.

Open a ticket; do not re-attempt cutover until root cause is understood AND fixed.

---

## 2. Temporary DevAuth re-enable (last-resort fallback)

**Only use this if Section 1 is not viable** (e.g., the last-known-good revision predates other unrelated dependencies and reverting breaks more than it fixes). Re-enabling DevAuth in prod is a security regression — minimize the window.

```powershell
# Re-enable DevAuth
az containerapp update -n ca-vrbook-api-prod -g rg-vrbook-prod `
    --set-env-vars 'DevAuth__AllowAnonymous=true'

# Verify
az containerapp show -n ca-vrbook-api-prod -g rg-vrbook-prod `
    --query "properties.template.containers[0].env[?contains(name, 'DevAuth')]" -o table
```

**Immediate follow-up actions** (do NOT skip):
1. Announce in #vrbook-ops at the level of "production incident — DevAuth temporarily enabled."
2. Set a 1-hour timer. If not resolved by then, escalate.
3. As soon as the root cause is fixed and verified, flip back:
   ```powershell
   az containerapp update -n ca-vrbook-api-prod -g rg-vrbook-prod `
       --set-env-vars 'DevAuth__AllowAnonymous=false'
   ```
4. Update `infra/main.bicep` if the Bicep value needs to change permanently — but the current default for prod is already `'false'` so usually no Bicep edit is needed; the env-var override is enough.

**This must never be the resting state in prod.** Document any time this happens in the runbook's footer with date + duration.

---

## 3. Config / secret rollback

If the regression is from a bad KV secret value (e.g., wrong tenant id, wrong client id), Key Vault keeps version history.

```powershell
# List historical versions of the secret
az keyvault secret list-versions --vault-name kv-vrbook-prod --name entra-tenant-id -o table

# Restore the previous version
$prevVersion = '<id from the table — the second row, not the first>'
$prevValue = az keyvault secret show --vault-name kv-vrbook-prod --name entra-tenant-id `
    --version $prevVersion --query value -o tsv

az keyvault secret set --vault-name kv-vrbook-prod --name entra-tenant-id --value $prevValue

# Restart API container so it reads the restored value
$rev = az containerapp show -n ca-vrbook-api-prod -g rg-vrbook-prod `
    --query "properties.latestRevisionName" -o tsv
az containerapp revision restart -n ca-vrbook-api-prod -g rg-vrbook-prod --revision $rev
```

For `entra-web-authority` / `entra-web-client-id`, restoring the KV value alone is not enough — the browser bundle has them baked in at build time. After restoring KV, also trigger a `cd-prod-web` rebuild so the new bundle picks up the restored value.

---

## 4. Entra portal state rollback

### 4.1 App Role assignment was made in error

Delete the bad assignment via the Graph API (`DELETE /users/{id}/appRoleAssignments/{assignmentId}`). See [`entra-key-rotation.md`](./entra-key-rotation.md) §3.

### 4.2 Redirect URI accidentally moved or removed

Re-PATCH the app registration:

```powershell
az login --tenant vrbookprod.onmicrosoft.com --allow-no-subscriptions | Out-Null
$webAppId = '<vrbook-web-prod appId>'
$webObjectId = az ad app show --id $webAppId --query id -o tsv

$body = @{
    spa = @{ redirectUris = @('http://localhost:3000/auth/callback', 'https://<prod-front-door-url>/auth/callback') }
    web = @{ redirectUris = @() }
} | ConvertTo-Json -Depth 5

$bodyFile = New-TemporaryFile
$body | Set-Content -Path $bodyFile.FullName -Encoding utf8 -NoNewline
az rest --method PATCH `
    --uri "https://graph.microsoft.com/v1.0/applications/$webObjectId" `
    --headers 'Content-Type=application/json' `
    --body "@$($bodyFile.FullName)"
Remove-Item $bodyFile.FullName
```

### 4.3 App Role definitions accidentally deleted

Recreate via portal: App registrations → vrbook-api-prod → App roles → + Create app role. Values must be **exactly** `Owner` and `Admin`. Then re-assign all current role holders (capture audit BEFORE the cutover in [`entra-key-rotation.md`](./entra-key-rotation.md) §3.1).

### 4.4 User flow accidentally deleted

User flows can be recreated quickly via the portal, but **users provisioned under the old flow keep working** because their `oid` doesn't change. The new flow only affects new sign-ups. Recreate per [`entra-external-id-setup.md`](./entra-external-id-setup.md) §3.

---

## 5. Full tenant rollback (extreme — almost never the right answer)

If the prod External tenant itself is unsalvageable (extremely rare):

1. **Stop user-facing traffic** — temporary DevAuth re-enable (Section 2) buys you time.
2. **Investigate first.** Tenant deletion is irreversible; verify there's no per-app fix.
3. **Provision a replacement** via [`entra-external-id-setup.md`](./entra-external-id-setup.md) end-to-end against the new tenant.
4. **Migrate users** — there is no automated path. Users must sign up again on the new tenant. This is a user-facing breakage that needs comms + UX support.
5. **Repoint KV and rebuild** the web container so it sees the new tenant id.

Estimated time: 4-8 hours. Treat as a P0 incident.

---

## 6. What CANNOT be rolled back

- **Issued tokens already in users' browsers.** They expire on their own (60 min default). Users may see broken state for up to that window even after the underlying fix lands.
- **App Role assignments made before the rollback.** Re-revoking is a separate action.
- **Audit logs.** Anything that happened during the broken window is recorded in App Insights + Entra Sign-in logs.

---

## 7. Comms templates

### Cutover incident — initial post

```
:rotating_light: PROD INCIDENT - VrBook auth

What: <1-line symptom>
Started: <time>
Impact: <users affected estimate>
Action taken: <revert revision | DevAuth re-enabled | config rolled back>
Owner: <name>
Next update: in 15 minutes

Channel: #vrbook-ops
```

### Cutover incident — resolved post

```
:white_check_mark: PROD INCIDENT - VrBook auth - RESOLVED

Duration: <total time>
Root cause: <1-2 sentences>
Fix: <commit ref or config change>
Follow-ups: <list>
Post-mortem: by <date>
```

---

## Acknowledgements

On-call rotation acknowledgment of this playbook (required per [`entra-prod-cutover-prerequisites.md`](./entra-prod-cutover-prerequisites.md) §3.2):

| Name | Role | Date acknowledged |
|---|---|---|
| _(to be filled before cutover day)_ | | |

---

## Related

- [`entra-prod-cutover-checklist.md`](./entra-prod-cutover-checklist.md) — what to follow during cutover.
- [`entra-prod-cutover-prerequisites.md`](./entra-prod-cutover-prerequisites.md) — what must exist before cutover.
- [`entra-incident-triage.md`](./entra-incident-triage.md) — symptom → cause diagnosis for in-flight issues.
- [`entra-key-rotation.md`](./entra-key-rotation.md) — ongoing credential & access management.
