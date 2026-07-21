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

### 4. Complete each admin persona's first-sign-in interstitial (one-time)
ADR-0017 enforces force-password-change on first admin sign-in via **CIAM policy, not code** — so the very first `AdminSignUpSignIn` sign-in for `e2e-owner` + `e2e-platform-admin` may surface a "update your password" (or email-OTP) interstitial that the headless `global-setup.ts` capture doesn't drive. **Operator does this once per admin persona right after seeding:** sign in manually at the staging admin URL with the KV password, complete any change-password/OTP prompt, land on the dashboard. (The guest persona uses the guest flow and has no such interstitial — which is why guest signs in headlessly and the admin personas can fail on their first run.) After this one-time completion the automated interactive capture proceeds cleanly.

### 5. Done — how tests then get a token
- **API-only step-6:** `Category=Integration` already covers most (Testcontainers). For live-staging authed calls, the agent gets a bearer **only** from the token captured by Playwright `web/tests/e2e/global-setup.ts` (real hosted-flow sign-in — the only path that carries the `tfp`/`acr` admin-flow marker + the flow's `email` claim), then `Authorization: Bearer …` to staging. **There is NO ROPC path** (see below + ADR-0019 §2 / ADR-0020 Rejected).
- **UI:** the authed Playwright projects run headlessly with `E2E_{OWNER,PLATFORM_ADMIN,GUEST}_PASSWORD` from KV.

## ROPC is NOT viable — settled (architect consult 2026-07-20)
ROPC (`grant_type=password`) bypasses the CIAM user flow entirely, so it can never produce a working admin token here — this is architectural, not a config gap:
- It carries no `tfp`/`acr` admin-flow marker (those are emitted *by* the user flow), and its `email` resolves to the synthetic `<guid>@…onmicrosoft.com` UPN instead of the flow-mapped `@vrbook.test`, so the OPS.M.22 admin-preseed gate can't attach the pre-seeded role → 403.
- Enabling it would require `allowPublicClient=true` + ROPC on the app reg, minting admin-authority tokens that skip the flow's OTP/force-password-change/CA — eroding ADR-0016 intent for zero functional gain.
- **The only supported admin-token path is the interactive `AdminSignUpSignIn` capture.** If the hosted page drifts, fix the selectors/interstitial handling in `global-setup.ts` — never ROPC.

## Guardrails (why this is safe, not a backdoor)
- Real Entra-issued tokens, real claim pipeline, real admin-gate middleware exercised — **no `[AllowAnonymous]`/DevAuth**, so `OpsOps2_AdminSurfaceAndTestBackdoorTests` stays green.
- Accounts are staging-only, isolated `e2e-tenant`, deterministic fixture. **Rotate the KV passwords periodically; never provision in prod.**

## EXECUTED 2026-07-19 — status + the ROPC verdict (definitive)
- ✅ **Personas created** in the CIAM tenant (`c6ada840-…` / `vrbook.ciamlogin.com`): `e2e-owner@vrbook.test`, `e2e-platform-admin@vrbook.test`, `e2e-guest@vrbook.test` (email+password local accounts). **`mail` attribute set** on each = its `@vrbook.test` email (so the `email` claim emits and matches the DB seed).
- ✅ **KV passwords set**: `e2e-{owner,platform-admin,guest}-password` in `kv-vrbook-staging`.
- ✅ **DB seed confirmed**: migrator job `caj-vrbook-migrator-staging` has `Bootstrap__E2e__Enabled=True`; a manual run succeeded (SeedE2EBackfill re-seeds `is_platform_admin`/`tenant_admin` rows).
- ❌ **ROPC is NOT viable for admin tokens here (verified live + architect-reviewed 2026-07-20).** Precise mechanism (corrected): ROPC bypasses the `AdminSignUpSignIn` user flow, so the token gets neither the flow's `email` claim mapping nor the `tfp`/`acr` marker. The **primary** 403 cause is the resulting **email mismatch** — the token's `email` falls back to the synthetic `<guid>@…onmicrosoft.com` UPN, which doesn't match the seeded `e2e-owner@vrbook.test` row, so the pre-seeded role never attaches. (A no-marker token whose `email` *did* match would actually get admin authority via the middleware's ADR-0017 fall-through — so the missing marker alone is not the whole story; the email mismatch is.) Additionally, live ROPC returns `AADSTS90002` if the `@vrbook.test` email is used as username (misroutes) and `AADSTS65001` (consent) with the real UPN. Conclusion unchanged and stronger: ROPC cannot produce a usable admin token; **dropped from the strategy** (ADR-0019 §2, ADR-0020 Rejected).
- ✅ **THE token path = interactive `AdminSignUpSignIn` sign-in via Playwright `global-setup.ts`** (the same flow real admins use — it carries the flow marker, so the role materializes). Personas now exist, so the authed Playwright projects are unblocked. For API-only step-6, pull the bearer from the Playwright-captured session, not ROPC.
