# Runbook: Production Entra cutover — checklist + go/no-go gates

> **Status**: Authoritative for VrBook.
> **Owner**: Identity (A1) + Platform (A2).
> **Last updated**: 2026-06-26.
> **Use this when**: executing the production cutover. Walk top-to-bottom. STOP at any failed gate and remediate before continuing.

Prerequisite reading: [`entra-prod-cutover-prerequisites.md`](./entra-prod-cutover-prerequisites.md). If any prerequisite there is unchecked, do NOT start this checklist.

---

## T-7 days — Preparation

- [ ] Confirm all items in [`entra-prod-cutover-prerequisites.md`](./entra-prod-cutover-prerequisites.md) §0 are ✅.
- [ ] Dry-run the cutover against a fresh test External tenant (see prerequisites §5). Document any runbook fixes.
- [ ] Identify two named on-call engineers for cutover day. Both must have:
  - `Tenant Creator` role in workforce Azure AD.
  - Owner role on the prod Azure subscription (for the External tenant link).
  - Read access to the GitHub `production` Environment (for approval clicks).
- [ ] Schedule maintenance window (suggested: 90 minutes, low-traffic time).
- [ ] Announce in #vrbook-ops with calendar invite to both on-calls.

---

## T-24 hours — Final verification

- [ ] Staging `/admin` still loads via Entra alone (no DevAuth fallback). Quick sanity check.
- [ ] `develop` branch CI is green.
- [ ] `main` branch is at the commit you intend to deploy. Tag the commit: `git tag prod-cutover-YYYY-MM-DD`.
- [ ] `infra/.state/prod.json` exists and has the expected `keyVaultName`, `resourceGroup`, etc. from prerequisite §1.1.
- [ ] `kv-vrbook-prod` contains the `entra-*` placeholders (run `infra/scripts/10-store-secrets.ps1 -Env prod` if missing).
- [ ] Front Door endpoint URL is known and matches what will be configured in `vrbook-web-prod` redirect URI.
- [ ] Rollback playbook acknowledged + signed off by both on-calls.

---

## T-0 — Cutover execution

Each section is a discrete go/no-go gate. Mark ❌ on any failure → consult rollback playbook → do not proceed.

### Phase A — Tenant + app registrations (~60 min)

- [ ] **A1**. External tenant `VrBook External Prod` provisioned, type "External", linked to prod subscription. Tenant ID captured. *(per [`entra-external-id-setup.md`](./entra-external-id-setup.md) §1)*
- [ ] **A2**. `12-entra-cutover.ps1 -Env prod -ExternalTenantDomain vrbookprod.onmicrosoft.com` Phase A completes. Both app registrations exist:
  - `vrbook-api-prod` with identifier URI `api://vrbook`, exposed scope `access_as_user`.
  - `vrbook-web-prod` with redirect URIs (will be moved to SPA platform in A5).
- [ ] **A3**. User flow `SignUpAndSignIn` created in the prod tenant. Email signup enabled. Display Name + Email Address collected. **MFA enforced** (or per policy from prerequisites §4).
- [ ] **A4**. User flow associated with `vrbook-web-prod`.
- [ ] **A5**. `vrbook-web-prod` redirect URIs moved from `web` platform to `spa` platform via Graph PATCH *(per [`entra-incident-triage.md`](./entra-incident-triage.md) §2.2)*. Verify `web.redirectUris` is empty and `spa.redirectUris` has the Front Door URL + localhost.
- [ ] **A6**. `12-entra-cutover.ps1` Phase B writes 5 KV secrets to `kv-vrbook-prod`. Verify with:
   ```powershell
   az keyvault secret list --vault-name kv-vrbook-prod `
       --query "[?starts_with(name, 'entra-')].{name:name, updated:attributes.updated}" -o table
   ```

**Phase A GATE**: all 6 items ✅. If any ❌ → STOP.

### Phase B — App Roles + first admin (~10 min)

- [ ] **B1**. App Roles `Owner` + `Admin` defined on `vrbook-api-prod` (portal: App registrations → App roles). Both `enabled=true`. Values **exactly** `Owner` and `Admin` (case-sensitive).
- [ ] **B2**. Service principal for `vrbook-api-prod` exists (`az ad sp list --filter "appId eq '<apiAppId>'"`). Create if not.
- [ ] **B3**. The first prod admin signs up via the prod user flow at `https://vrbookprod.ciamlogin.com/...` (Entra admin center → User flows → SignUpAndSignIn → Run user flow). Captures their `oid` from the resulting token.
- [ ] **B4**. First admin assigned `Owner` AND `Admin` App Roles via Graph PATCH *(per [`entra-external-id-setup.md`](./entra-external-id-setup.md) §7.2)*.
- [ ] **B5**. Verify with `az rest --method GET --uri "https://graph.microsoft.com/v1.0/users/{oid}/appRoleAssignments"` — two assignments, both resolving to `vrbook-api-prod` SP.

**Phase B GATE**: 5 items ✅. If any ❌ → STOP.

### Phase C — Deploy + verify (~30 min)

- [ ] **C1**. `cd-prod-api.yml` workflow runs against `main`. Manual approval clicked by named on-call. Workflow completes green. API container has the new image with `EntraExternalId__*` secret references.
- [ ] **C2**. `cd-prod-web.yml` workflow runs against `main`. "Fetch Entra build args from Key Vault" step pulls real values (NOT placeholders). Build args reach the Dockerfile (verify in build log: real values in `ENV NEXT_PUBLIC_ENTRA_*` lines).
- [ ] **C3**. First admin signs in via the prod web app in a fresh incognito.
- [ ] **C4**. DevTools confirms:
   - `aud` claim = `vrbook-api-prod` appId
   - `roles` claim = `["Owner", "Admin"]` (order may differ)
   - `iss` claim = `https://vrbookprod.ciamlogin.com/<tenantId>/v2.0`
- [ ] **C5**. `/admin` route loads with real data.
- [ ] **C6**. Confirm `DevAuth__AllowAnonymous=false` on the prod API container (Bicep handles this; verify with `az containerapp show`).

**Phase C GATE**: 6 items ✅. If any ❌ → consult rollback playbook.

### Phase D — Open the gates

- [ ] **D1**. Remove any maintenance-window banner.
- [ ] **D2**. Send #vrbook-ops "cutover complete" message.
- [ ] **D3**. Monitor App Insights for the next 1 hour:
   - Sign-in error rate (`AADSTS*` codes).
   - 401/403 rate on `/api/v1/*`.
   - User provisioning failures (search logs for `User provisioning failed`).
- [ ] **D4**. Test sign-up as a fresh "guest" user (not the admin). Verify they can browse properties but `/admin` returns 403.

---

## T+1 hour — Post-cutover stabilization

- [ ] All metrics in §D3 within normal ranges.
- [ ] On-calls stand down from active monitoring; passive monitoring continues for 24h.

---

## T+24 hours — Cleanup

- [ ] Remove `DevAuth__WebBaseUrl` from any prod env vars (Bicep `be897bc` already cleared this — verify in deployed Container App config).
- [ ] Archive the cutover artifacts (Bicep deploy log, workflow runs, KV secret rotation history) to `docs/audit/prod-cutover-YYYY-MM-DD/`.
- [ ] Update `docs/MASTER_PLAN.md` and `docs/REPLAN.md` to mark OPS.M.0 prod-cutover as Done with date.
- [ ] Retrospective in #vrbook-ops within 7 days. Capture lessons in [`entra-incident-triage.md`](./entra-incident-triage.md) §2 table.

---

## Decision matrix during cutover

If a gate fails:

| Phase that failed | Severity | Action |
|---|---|---|
| Phase A (A1-A6) | Pre-deploy; no user impact | Stop. Fix the underlying issue. Restart phase. No rollback needed (no traffic affected). |
| Phase B (B1-B5) | Pre-deploy; no user impact | Same as above. |
| Phase C, items C1-C3 | Deployed image live but auth broken | Rollback per [`entra-cutover-rollback.md`](./entra-cutover-rollback.md) §2 (revert revision OR flip DevAuth back temporarily). |
| Phase C, items C4-C6 | Auth works but claims wrong | Diagnose per [`entra-incident-triage.md`](./entra-incident-triage.md). If fix is portal-only (App Role assignment, claim emission), apply in place. |
| Phase D | Cutover complete but symptom found | Treat as production incident; rollback if blast radius warrants it. |

---

## Related

- [`entra-prod-cutover-prerequisites.md`](./entra-prod-cutover-prerequisites.md) — what must exist before this checklist.
- [`entra-cutover-rollback.md`](./entra-cutover-rollback.md) — recovery playbook.
- [`entra-external-id-setup.md`](./entra-external-id-setup.md) — referenced from many gates.
- [`entra-incident-triage.md`](./entra-incident-triage.md) — diagnostic playbook for failed gates.
