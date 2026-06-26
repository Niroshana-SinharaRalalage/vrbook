# Runbook: Production Entra cutover — prerequisites

> **Status**: Authoritative for VrBook.
> **Owner**: Identity (A1) + Platform (A2).
> **Last updated**: 2026-06-26.
> **Use this when**: planning the production Entra cutover. Read this BEFORE [`entra-prod-cutover-checklist.md`](./entra-prod-cutover-checklist.md). If any prerequisite below is unchecked, prod cutover MUST NOT proceed.

This runbook is the **gate** — it lists everything that must exist before the prod cutover can start. As of 2026-06-26, several items are not yet built. They are tracked here so prod cutover doesn't accidentally inherit staging shortcuts.

---

## 0. Snapshot of the gap (2026-06-26)

| Prerequisite | Status | Where it lives / needs to be built |
|---|---|---|
| Staging Entra cutover end-to-end verified | ✅ Done | Closed in commit `b21447c` → `be897bc`; `/admin` loads via Entra alone, App Roles assigned, DevAuth off |
| Identity runbooks (setup, key rotation, incident triage) | ✅ Done | `docs/identity/runbooks/` (commit `b26e46a`) |
| `cd-prod-api.yml` workflow | ❌ Missing | Needs to be modeled on `cd-staging-api.yml` with prod-specific gates |
| `cd-prod-web.yml` workflow | ❌ Missing | Needs to be modeled on `cd-staging-web.yml` (also needs the `entra-web-authority` / `entra-web-client-id` fetch step prod equivalents) |
| `infra/.state/prod.json` | ❌ Missing | Created by running `infra/scripts/00-foundation.ps1 -Env prod` |
| Prod resource group + foundation (`rg-vrbook-prod`, KV, ACR, etc.) | ❌ Missing | Created by `infra/scripts/00-foundation.ps1` + `01-bicep-deploy.ps1` for prod |
| Prod Entra External tenant provisioned | ❌ Missing | [`entra-external-id-setup.md`](./entra-external-id-setup.md) §1 against `-Env prod` |
| Prod KV `entra-*` seeds + real values written | ❌ Missing | Cutover script Phase B against `-Env prod` |
| Prod App Roles + first admin assigned | ❌ Missing | [`entra-external-id-setup.md`](./entra-external-id-setup.md) §7 against the prod tenant |
| Manual approval gate on `cd-prod-*` workflows | ❌ Missing | GitHub Environment protection rule on `production` env |
| Prod observability hooked up (App Insights for prod tenant) | Status unknown | Bicep provisions it (`appi-vrbook-prod`); verify the alerts route to the correct on-call |
| MFA policy for `Admin` role decided | ❌ Open question | Default recommendation: required for `Admin`, optional for `Owner` |
| Rollback playbook reviewed by oncall | ❌ Missing | [`entra-cutover-rollback.md`](./entra-cutover-rollback.md) drafted; needs sign-off |

---

## 1. Infrastructure prerequisites

### 1.1 Prod foundation must exist

Before any Entra work, the prod Azure resources must be deployed:

```powershell
# Run from repo root, in the workforce tenant context
.\infra\scripts\00-foundation.ps1 -Env prod                # creates rg-vrbook-prod, KV, MI
.\infra\scripts\10-store-secrets.ps1 -Env prod              # seeds entra-* placeholders + others
.\infra\scripts\01-bicep-deploy.ps1 -Env prod               # deploys main.bicep with isProd=true
```

This creates everything the Entra cutover script needs: `kv-vrbook-prod`, the API + web Container Apps, Postgres, etc. **Without this, Phase B of the cutover script has nowhere to write KV secrets.**

### 1.2 Prod CI/CD workflows must exist

The cutover relies on `cd-prod-web.yml` to bake `NEXT_PUBLIC_ENTRA_*` into the browser bundle (same pattern as staging). Required workflows (currently missing — they need to be built):

- `.github/workflows/cd-prod-api.yml` — model on `cd-staging-api.yml`. Differences:
  - Branch trigger: `main` (not `develop`).
  - Resource group: `rg-vrbook-prod`.
  - Environment: `production` (with manual approval rule — see §3 below).
  - No DevAuth env vars (Bicep already handles this: commit `be897bc`'s `isDev ? 'true' : 'false'` resolves to `'false'` for prod).
- `.github/workflows/cd-prod-web.yml` — model on `cd-staging-web.yml`. Differences:
  - Trigger on `main` (not `develop`).
  - The "Fetch Entra build args from Key Vault" step reads from `kv-vrbook-prod` instead of `kv-vrbook-staging`.
  - The build args include the prod API container's URL for `NEXT_PUBLIC_API_BASE_URL`.

### 1.3 Front Door + WAF (prod only)

`infra/main.bicep` line 67 enables Front Door for prod (`var frontDoorEnabled = isProd`). This means prod's web app is behind a Front Door endpoint, not the raw Container App URL. Two implications for the Entra cutover:

1. **Redirect URI on `vrbook-web-prod` must be the Front Door URL**, not the Container App URL. Verify before running [`entra-external-id-setup.md`](./entra-external-id-setup.md) §6.
2. The `NEXT_PUBLIC_API_BASE_URL` in the web bundle should be the API's public hostname (via Front Door or direct CA — depends on routing decision in proposal §15).

---

## 2. Entra-specific prerequisites

### 2.1 Separate prod External tenant

**Decision**: prod gets its own External tenant (`VrBook External Prod`, e.g. `vrbookprod.onmicrosoft.com`). Staging's `VrBook External` (`vrbook.onmicrosoft.com`) is NOT reused.

Rationale (per `OPS_M_0_PLAN.md` §1.5 + ADR-0013):
- Isolates production user data from staging test data (compliance, blast radius).
- Allows different MFA / sign-up policies per env.
- Identifier URI `api://vrbook` is unique within a tenant — having separate tenants avoids collisions.

### 2.2 App Role naming consistency

The `vrbook-api-prod` App Role values **must be `Owner` and `Admin`** (case-sensitive, no env suffix). Code references `[Authorize(Roles="Owner,Admin")]` uniformly across all envs. Do not introduce `Owner-prod` or similar variations.

### 2.3 Tenant Domain prefix availability

Before §1 of [`entra-external-id-setup.md`](./entra-external-id-setup.md), confirm the desired prod domain prefix is available:

```
https://vrbookprod.ciamlogin.com   # check this resolves to "Domain not registered" — meaning available
```

If taken, pick an alternative (e.g. `vrbookid`, `vrbookauth`).

---

## 3. Procedural prerequisites

### 3.1 GitHub Environment protection rule

Create a `production` GitHub Environment with:
- **Required reviewers**: at least 2 named approvers (Platform + Identity owners).
- **Wait timer**: 5 minutes (gives the approver a moment to abort).
- **Deployment branches**: restrict to `main`.

Reference: [`cd-prod-api.yml`](../../../.github/workflows/) (to be created) must declare `environment: production` on its deploy job so the protection rule applies.

### 3.2 Sign-off review of the rollback playbook

The on-call rotation must read and acknowledge [`entra-cutover-rollback.md`](./entra-cutover-rollback.md) before cutover day. Add their acknowledgement (date + name) to a section at the bottom of that doc.

### 3.3 Communication plan

Before flipping `DevAuth__AllowAnonymous=false` on prod (the moment the platform actually requires real sign-in), the comms plan must include:
- Maintenance-window banner on the web app (post-Bicep, pre-Entra cutover).
- Internal Slack #vrbook-ops announcement at T-1h, T-0, T+1h (success/issues).
- External support contact ready if guest sign-up fails post-cutover.

---

## 4. MFA & policy decisions to confirm

These were left open in `OPS_M_0_PLAN.md` and must be answered before prod cutover. Default recommendations:

| Policy | Default recommendation | Confirm with |
|---|---|---|
| MFA for `Admin` role | **Required** | Security / Identity owner |
| MFA for `Owner` role | Optional (recommended) | Identity owner |
| MFA for guests (no role) | Off (frictionless sign-up) | Product owner |
| Password policy | Microsoft default + 12-char minimum | Identity owner |
| Session lifetime (access token) | 60 min (default) | OK as-is unless compliance dictates shorter |
| Refresh token lifetime | 90 days rolling (default) | OK as-is |
| Google IdP for guests | Optional | Product owner |
| Email-OTP vs Password | **Password** (more familiar; OTP is friction) | Product owner |

Capture decisions in `docs/identity/setup.md` (or a new `prod-policy-decisions.md` if it grows beyond a checklist).

---

## 5. Pre-cutover dry run

Before the actual prod cutover, do a dry run against a NEW staging Entra tenant (not the existing `VrBook External`). This validates:
- The runbook flows are still accurate.
- No new CIAM portal changes have broken the procedure.
- Anyone on-call could follow the runbook with no inside knowledge.

Allocate ~3 hours for the dry run. Treat any deviation from the runbook as a runbook bug to fix BEFORE prod.

---

## Related

- [`entra-prod-cutover-checklist.md`](./entra-prod-cutover-checklist.md) — go/no-go checklist for cutover day.
- [`entra-cutover-rollback.md`](./entra-cutover-rollback.md) — what to do if cutover goes sideways.
- [`entra-external-id-setup.md`](./entra-external-id-setup.md) — the actual cutover procedure.
- [`docs/OPS_M_0_PLAN.md`](../../OPS_M_0_PLAN.md) — original cutover plan.
- [ADR-0013](../../adr/0013-single-tenant-staging-and-prod.md) — separate prod tenant decision.
