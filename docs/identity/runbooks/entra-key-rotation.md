# Runbook: Entra credential & access management

> **Status**: Authoritative for VrBook.
> **Owner**: Identity (A1).
> **Last updated**: 2026-06-26.
> **Use this when**: rotating secrets, auditing App Role memberships, recovering from KV secret corruption, or processing a leaver. Routine ongoing operations.

> Note on naming: there's no client-secret rotation for the SPA flow (`vrbook-web-<env>` is a public client / PKCE). "Rotation" in this runbook refers to (a) Key Vault secret values, (b) per-user App Role membership lifecycle, (c) extreme cases where you have to rebuild an app registration.

---

## 1. When KV `entra-*` secrets need rotation

| Secret | Source of truth | Rotate when |
|---|---|---|
| `entra-instance` | External tenant CIAM hostname (e.g. `https://vrbook.ciamlogin.com`) | Never. Tenant identifier doesn't change. |
| `entra-tenant-id` | External tenant GUID | Never (would imply moving to a different tenant entirely — `entra-external-id-setup.md` end-to-end). |
| `entra-api-client-id` | `vrbook-api-<env>` appId | Only if the app registration is recreated (see §4 below). |
| `entra-web-authority` | `https://{instance}/{tenantId}/v2.0` | Same as `entra-tenant-id` / `entra-instance`. |
| `entra-web-client-id` | `vrbook-web-<env>` appId | Only if recreated. |

**None of these are "secrets" in the classic credential sense** — they're identifiers stored in KV so that Bicep + the workflow can read them by name. They don't expire, they don't get compromised in isolation. The KV containers exist primarily to gate access to them (only the API container's managed identity can read).

If a value in KV is wrong (typo, manual edit), fix in place:

```powershell
az keyvault secret set --vault-name kv-vrbook-<env> --name <secret-name> --value '<correct-value>'

# Restart the API container so JwtBearer picks up the new value at startup
az containerapp revision restart -n ca-vrbook-api-<env> -g rg-vrbook-<env> `
    --revision $(az containerapp show -n ca-vrbook-api-<env> -g rg-vrbook-<env> `
                --query 'properties.latestRevisionName' -o tsv)
```

The web app's `NEXT_PUBLIC_ENTRA_*` values are **baked into the browser bundle at `next build`**. To pick up a changed `entra-web-authority` or `entra-web-client-id`, you must trigger `cd-staging-web` (or `cd-prod-web`) so the workflow's "Fetch Entra build args from Key Vault" step reads the new value and passes it as a Docker `--build-arg`.

---

## 2. App Role membership: grant a role

For the first admin per environment, see [`entra-external-id-setup.md`](./entra-external-id-setup.md) §7. For ongoing role grants:

### 2.1 Via portal (preferred when SP exists)

1. Entra admin center (External tenant) → **Enterprise applications** → **All applications** → **`vrbook-api-<env>`**.
2. Left nav → **Users and groups** → **+ Add user/group**.
3. Pick the user → select **Owner** or **Admin** → **Assign**.
4. Repeat for the second role if both are needed (the assignment dialog is one-role-at-a-time per click).

The user must **sign out + back in** for the new role to appear in their access token (claims are stamped at token issuance, not refresh).

### 2.2 Via CLI (when portal UI is greyed out or you're scripting)

```powershell
az login --tenant vrbook.onmicrosoft.com --allow-no-subscriptions | Out-Null

$apiAppId  = '<vrbook-api-$env appId>'
$userOid   = '<target user oid>'
$roleName  = 'Admin'   # or 'Owner'

$apiSpId = az ad sp list --filter "appId eq '$apiAppId'" --query "[0].id" -o tsv
$roleId  = az ad app show --id $apiAppId `
            --query "appRoles[?value=='$roleName'].id | [0]" -o tsv

$body = @{ principalId = $userOid; resourceId = $apiSpId; appRoleId = $roleId } | ConvertTo-Json
$bodyFile = New-TemporaryFile
$body | Set-Content -Path $bodyFile.FullName -Encoding utf8 -NoNewline
az rest --method POST `
    --uri "https://graph.microsoft.com/v1.0/users/$userOid/appRoleAssignments" `
    --headers 'Content-Type=application/json' `
    --body "@$($bodyFile.FullName)"
Remove-Item $bodyFile.FullName
```

---

## 3. App Role membership: revoke a role (leaver workflow)

### 3.1 Audit who currently holds what

```powershell
$apiAppId = '<vrbook-api-$env appId>'
$apiSpId  = az ad sp list --filter "appId eq '$apiAppId'" --query "[0].id" -o tsv

# List all current app role assignments on the API app
az rest --method GET `
    --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$apiSpId/appRoleAssignedTo" `
    --query "value[].{user:principalDisplayName, role:appRoleId, assignmentId:id, when:createdDateTime}" -o table
```

Cross-reference the `role` GUIDs against the App Role definitions:

```powershell
az ad app show --id $apiAppId --query "appRoles[].{value:value, id:id}" -o table
```

### 3.2 Revoke (DELETE the appRoleAssignment by its id)

```powershell
$assignmentId = '<assignmentId from §3.1 audit>'
$userOid      = '<user oid>'

az rest --method DELETE `
    --uri "https://graph.microsoft.com/v1.0/users/$userOid/appRoleAssignments/$assignmentId"
```

The user's **current access token still works until it expires** (default 60 min). To force-revoke immediately, also disable the user account itself via Graph PATCH `accountEnabled=false` — drastic; only for confirmed leavers, not role downgrades.

---

## 4. Rebuilding an app registration (extreme case)

You may need to rebuild `vrbook-api-<env>` or `vrbook-web-<env>` if:
- Identifier URI was set to the wrong value and downstream MSAL scope changes are infeasible.
- Sign-in audience or token issuance flags can't be flipped via patch.
- Compromised credentials (would only apply if we added a confidential-client flow later).

Procedure:

1. **Identify all dependencies.** Search `infra/.state/<env>.json` for the appId (`entraApiAppId` or `entraWebAppId`). Search `infra/main.bicep` (currently no hardcoded appIds — they're read from KV).
2. **Capture the App Role assignments** (§3.1) — you must re-assign after the rebuild.
3. **Create the replacement app registration.** Use `infra/scripts/12-entra-cutover.ps1` with `-SkipRegister:$false` if rebuilding both, or hand-craft via `az ad app create` if only rebuilding one. Match the identifier URI (`api://vrbook` for the API app) and redirect URIs (both `localhost:3000/auth/callback` and the container app URL) exactly.
4. **Recreate App Roles** (`Owner`, `Admin`) on the new API app registration. Values **case-sensitive** per `[Authorize(Roles="Owner,Admin")]`.
5. **Update KV** (`entra-api-client-id` or `entra-web-client-id`) with the new appId.
6. **Update `infra/.state/<env>.json`** with the new appId(s).
7. **Trigger `cd-<env>-web` workflow** (`workflow_dispatch`) so the browser bundle bakes in the new `entra-web-client-id`.
8. **Re-assign all users from the audit in step 2** to App Roles on the new API app.
9. **Restart the API Container App** to pick up the new `entra-api-client-id` from KV.
10. Smoke test: fresh incognito sign-in, verify access token has correct `aud` + `roles`.

Estimated time: ~45 minutes. Most of it is re-assignment if the leaver list is long.

---

## 5. KV secret incident: secret deleted or corrupted

If a deployment fails with `(SecretNotFound) A secret with (name/id) entra-* was not found`:

1. The `infra/scripts/10-store-secrets.ps1` seed script writes `'pending-identity-setup'` placeholders for these. Run it again if the placeholders are missing entirely.

   ```powershell
   .\infra\scripts\10-store-secrets.ps1 -Env <env>
   ```

2. If the values were correct but got overwritten with placeholders, re-run the Phase B of the cutover script:

   ```powershell
   .\infra\scripts\12-entra-cutover.ps1 `
       -Env <env> `
       -ExternalTenantDomain vrbook.onmicrosoft.com `
       -SkipRegister
   ```

   `-SkipRegister` skips the app-registration phase (apps already exist) and writes KV from `infra/.state/<env>.json`.

3. After KV restore: restart the API container (§1) and trigger a web rebuild (§1) so both pick up real values.

---

## 6. Routine audits (quarterly suggestion)

A simple checklist to run every ~90 days, takes ~10 minutes:

```powershell
$apiAppId = '<vrbook-api-prod appId>'
$apiSpId  = az ad sp list --filter "appId eq '$apiAppId'" --query "[0].id" -o tsv

# 1. Who has Admin?
$adminRoleId = az ad app show --id $apiAppId --query "appRoles[?value=='Admin'].id | [0]" -o tsv
az rest --method GET --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$apiSpId/appRoleAssignedTo" `
    --query "value[?appRoleId=='$adminRoleId'].{user:principalDisplayName, when:createdDateTime}" -o table

# 2. Who has Owner?
$ownerRoleId = az ad app show --id $apiAppId --query "appRoles[?value=='Owner'].id | [0]" -o tsv
az rest --method GET --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$apiSpId/appRoleAssignedTo" `
    --query "value[?appRoleId=='$ownerRoleId'].{user:principalDisplayName, when:createdDateTime}" -o table

# 3. App Role definitions still match code? (Owner + Admin must exist + be enabled)
az ad app show --id $apiAppId --query "appRoles[].{value:value, enabled:isEnabled}" -o table

# 4. KV secret values match infra/.state/<env>.json?
$state = Get-Content infra/.state/prod.json | ConvertFrom-Json
'entra-tenant-id, entra-api-client-id, entra-web-client-id' -split ', ' | ForEach-Object {
    $kv = az keyvault secret show --vault-name kv-vrbook-prod --name $_ --query value -o tsv
    "$_ : KV=$kv"
}
```

For anything that drifts, fix per the relevant section above.

---

## Related

- [`entra-external-id-setup.md`](./entra-external-id-setup.md) — provisioning a fresh tenant.
- [`entra-incident-triage.md`](./entra-incident-triage.md) — when sign-in is failing.
- [`roles-architecture.md`](../roles-architecture.md) — why App Roles, not extension attributes.
