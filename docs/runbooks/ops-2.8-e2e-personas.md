# OPS.2.8 — Provision staging E2E test personas (the headless-auth unblock)

**Purpose.** One-time operator setup that lets automated tests obtain **real** admin tokens on staging **headlessly** (no human browser, no owner personal account) — the durable fix for authenticated step-6 verification (ADR-0020 Tier S/A). Nothing to build; this is provisioning. Implements ADR-0019 (persona strategy) + ADR-0017 (admin pre-seed) under ADR-0016 (admin = Entra-local only).

**Owner/operator task, ~15 min. Staging only — never provision these in prod.**

## What it creates
Three Entra-local test users + their DB roles + their Key Vault passwords:
| Persona | Entra user (email) | DB role (pre-seeded) | Used for |
|---|---|---|---|
| `e2e-platform-admin` | `e2e-platform-admin@vrbook.test` | `is_platform_admin=true`, 0 memberships | platform-admin endpoints (VRB-216 tiers/tax) |
| `e2e-owner` | `e2e-owner@vrbook.test` | `tenant_admin` on the isolated `e2e-tenant` | tenant-admin endpoints (VRB-215 per-property) |
| `e2e-guest` | `e2e-guest@vrbook.test` | lazy-provisioned (not seeded) | guest flows |

## Steps

### 1. Create the 3 Entra users (staging External ID tenant)
In the staging Entra External ID tenant, in the **admin** user flow (`AdminSignUpSignIn`) create `e2e-platform-admin@vrbook.test` and `e2e-owner@vrbook.test` as **Entra-local email+password** accounts; create `e2e-guest@vrbook.test` in the guest flow. (Same tenant/app-reg wired by `infra/scripts/12-entra-cutover.ps1`.)
- **Set a strong password on each; record it for step 3.**
- **Exempt all three from MFA + Conditional Access** (test-only accounts — CA/MFA breaks both ROPC and the headless browser sign-in). This is the single most common reason the harness fails; do not skip.

### 2. Pre-seed their DB roles (ADR-0017 — required before first admin sign-in)
From `infra/scripts/` against staging:
```powershell
.\vrbook-admin.ps1 -Env staging -Action seed-platform-admin -Email e2e-platform-admin@vrbook.test
# get the e2e-tenant GUID (or create it) then:
.\vrbook-admin.ps1 -Env staging -Action seed-tenant-admin -Email e2e-owner@vrbook.test -TenantId <e2e-tenant-GUID>
.\vrbook-admin.ps1 -Env staging -Action list   # verify both rows
```
`VrBook.Migrator.SeedE2EBackfill` (`Bootstrap:E2e:Enabled`, staging-only) re-seeds these deterministically on every deploy, so they self-heal.

### 3. Store the passwords in Key Vault
```
az keyvault secret set --vault-name kv-vrbook-staging --name e2e-platform-admin-password --value "<pw>"
az keyvault secret set --vault-name kv-vrbook-staging --name e2e-owner-password         --value "<pw>"
az keyvault secret set --vault-name kv-vrbook-staging --name e2e-guest-password          --value "<pw>"
```
(Mirror into `infra/scripts/10-store-secrets.ps1` for durable RG bootstrap. Never commit the values.)

### 4. Done — how tests then get a token
- **API-only step-6:** `Category=Integration` already covers most (Testcontainers). For live-staging authed calls, the agent gets a bearer via **(A)** ROPC one-call *if available* (see below), else **(B)** the token captured by Playwright `web/tests/e2e/global-setup.ts` (real hosted-flow sign-in), then `Authorization: Bearer …` to staging.
- **UI:** the authed Playwright projects run headlessly with `E2E_{OWNER,PLATFORM_ADMIN,GUEST}_PASSWORD` from KV.

## The one open verification — ROPC feasibility (do this to settle "one-call token" vs "browser-captured token")
Our tenant is Entra External ID (CIAM), where ROPC support is uncertain. Confirm with a live check:
```
# msal-node acquireTokenByUsernamePassword OR a raw POST to the token endpoint:
#   grant_type=password, username=e2e-platform-admin@vrbook.test, password=<kv>,
#   client_id=<staging SPA/public client id>, scope=<api scope>/.default, authority=<staging CIAM authority>
```
- **Token returned** → ROPC works; use it (simplest, one call). Requires the app registration to be a **public client** (`allowPublicClient=true`) with ROPC enabled — verify in the app reg.
- **`unsupported_grant_type` / `AADSTS` error** → ROPC unavailable on External ID; **use path (B)** the Playwright-captured token (already built, always works). Record the finding here + in `docs/runbooks/playwright-e2e-flake.md`.

Either outcome is fine — (B) is the guaranteed fallback. ROPC is only a convenience.

## Guardrails (why this is safe, not a backdoor)
- Real Entra-issued tokens, real claim pipeline, real admin-gate middleware exercised — **no `[AllowAnonymous]`/DevAuth**, so `OpsOps2_AdminSurfaceAndTestBackdoorTests` stays green.
- Accounts are staging-only, isolated `e2e-tenant`, deterministic fixture. **Rotate the KV passwords periodically; never provision in prod.**

## EXECUTED 2026-07-19 — status + the ROPC verdict (definitive)
- ✅ **Personas created** in the CIAM tenant (`c6ada840-…` / `vrbook.ciamlogin.com`): `e2e-owner@vrbook.test`, `e2e-platform-admin@vrbook.test`, `e2e-guest@vrbook.test` (email+password local accounts). **`mail` attribute set** on each = its `@vrbook.test` email (so the `email` claim emits and matches the DB seed).
- ✅ **KV passwords set**: `e2e-{owner,platform-admin,guest}-password` in `kv-vrbook-staging`.
- ✅ **DB seed confirmed**: migrator job `caj-vrbook-migrator-staging` has `Bootstrap__E2e__Enabled=True`; a manual run succeeded (SeedE2EBackfill re-seeds `is_platform_admin`/`tenant_admin` rows).
- ❌ **ROPC is NOT viable for admin tokens here (verified live).** ROPC authenticates and even emits the correct `email` claim, but the token carries **no `tfp`/`acr` user-flow marker**, so `UserProvisioningMiddleware`'s OPS.M.22 admin-preseed gate never recognizes it as an admin-flow login and the `PlatformAdmin` role never materializes → **403**. ROPC bypasses the `AdminSignUpSignIn` user flow by design; it cannot produce an admin-flow token.
- ✅ **THE token path = interactive `AdminSignUpSignIn` sign-in via Playwright `global-setup.ts`** (the same flow real admins use — it carries the flow marker, so the role materializes). Personas now exist, so the authed Playwright projects are unblocked. For API-only step-6, pull the bearer from the Playwright-captured session, not ROPC.
