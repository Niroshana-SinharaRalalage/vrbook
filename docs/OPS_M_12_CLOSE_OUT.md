# Slice OPS.M.12 — Close-Out

**Status:** shipped end-to-end.
**Slice plan:** [`OPS_M_12_SOCIAL_IDPS_PLAN.md`](OPS_M_12_SOCIAL_IDPS_PLAN.md).
**Predecessor:** [`OPS_M_14_CLOSE_OUT.md`](OPS_M_14_CLOSE_OUT.md) (DevAuth retirement) — M.12 assumes DevAuth is retired so Entra + this social split are the sole sign-in surfaces.
**Skipped between:** OPS.M.15 (App-role legacy claim reads / `[Authorize(Roles=)]` drop) — still deferred and independent of M.12.

---

## 1. What shipped

### The gap this closed

Before M.12, the SPA used **one** Entra authority for every sign-in — the `Providers.tsx` MSAL config's single `NEXT_PUBLIC_ENTRA_AUTHORITY`. If we widened that authority to include a social IdP for guest UX, **admins would also route through it**, and Entra would emit a social `idp` claim on the admin's token. The backend had no signal to distinguish "admin using their local password" from "admin using their Google login," and the `user_identities` table happily persisted whatever `provider` the last provisioning call sent.

Owner policy locked 2026-07-05 (§11 in the plan):

- Platform Admin + Tenant Admin — **Entra local ONLY** (password OR OTP).
- Guests — Entra local + Google + Microsoft consumer + Facebook + Apple.

M.12 encodes that policy at **three layers** (see [ADR-0016](adr/0016-admin-vs-social-idp-surface-split.md)):

- **Layer 0** — two Entra user flows (`AdminSignUpSignIn` local-only + `GuestSignUpSignIn` local+4-socials) + per-flow SPA authority so admin surfaces never see the social buttons.
- **Layer 1 (REFUSE-AT-PROVISIONING)** — `ProvisionOrLinkUserHandler` refuses to add a social `user_identities` row on any admin-authority user. Invariant enforced in DB shape.
- **Layer 2 (middleware belt)** — `AdminSocialIdpRejectionMiddleware` blocks any authenticated request whose token carries a social `idp` claim AND whose user has admin authority.

### Backend

**Accessor + policy plumbing (M.12.1):**
- `ICurrentUser.IdentityProvider` (new nullable string on the contract).
- `HttpCurrentUser.SocialIdpValues` (7 raw hosts) + `SocialProviderKeys` (4 canonical keys).
- `HttpCurrentUser.IdentityProvider` normalises `null` / tenant-issuer-host to `entra`, keeps social host values verbatim.
- `EntraExternalId__TenantIssuerHost` config key + Bicep env var.

**Layer 2 middleware + exception + ProblemDetails (M.12.2):**
- `AdminSocialIdpRejectionMiddleware` — predicate `idp ∈ SocialIdpValues AND (IsPlatformAdmin OR MembershipRoles.Count > 0)`, whitelist for `/api/v1/me` + `/api/v1/me/tenants`.
- `AdminSocialIdpRejectedException : ForbiddenException` with `const string Rule = "admin_authority_requires_entra_local"`.
- `ProblemTypes.AdminSocialIdpRejected` URI constant.
- `ProblemDetailsConfig` maps the exception BEFORE the generic `ForbiddenException` mapper (Hellang matches first-registered).
- Wired via `IdentityModule.UseIdentityModule` immediately after `UserProvisioningMiddleware` so it reads the stamped tenant + role state.

**Classifier + CHECK migration + provisioning wiring (M.12.3):**
- `IdentityProviderClassifier.Classify(idpClaim, entraTenantIssuerHost)` — pure static, maps raw `idp` values to canonical provider strings.
- Migration `OpsM12_UserIdentitiesProviderAddFacebook` widens the CHECK constraint set from `('entra','google','microsoft','apple','test')` to `('entra','google','microsoft','apple','facebook','test')`.
- `UserProvisioningMiddleware` uses the classifier (was hardcoded `"entra"`).

**Layer 1 REFUSE-AT-PROVISIONING (M.12.4):**
- `ProvisionOrLinkUserHandler.RefuseIfAdminSocialLinkAsync` — checks matched user for `IsPlatformAdmin=true` OR any active `tenant_memberships` row. Throws `BusinessRuleViolationException` with rule `admin_social_signin_refused` on hit.
- Called from BOTH Branch 2 (verified-email match) AND Branch 3 (race-recovery after concurrent insert).
- 6 integration tests locking the truth table: normal google guest LINKS, google + platform admin REFUSED, google + tenant admin REFUSED, facebook + platform admin REFUSED, fresh google guest lands, entra-local admin re-link NOT refused.

### Frontend (SPA)

**Per-flow authority + guarded sign-in (M.12.6):**
- `msalConfig.ts` — `authorityForFlow(flow)` + `loginRequestFor(flow, returnTo)`. Reads `NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN` / `_GUEST` with `NEXT_PUBLIC_ENTRA_AUTHORITY` as a one-cycle fallback (dropped in this slice — see §3 below). `state` is JSON-encoded `{flow, returnTo}` so the callback routes correctly even if sessionStorage was evicted.
- `useAuth.signIn({ flow?, returnTo? })` — persists flow to sessionStorage BEFORE `loginRedirect` so silent-refresh reconstructs the right authority.
- `/auth/signin?flow={admin|guest}&returnTo=<path>` (NEW page) — single kick-off point for both flows.
- `AdminAuthGuard` (NEW client component in `admin/layout.tsx`) — unauthenticated → `/auth/signin?flow=admin&returnTo=<pathname>`.
- Callback page routes `flow=admin` → M.13.5 tenant picker branch, `flow=guest` → skip picker.

**Admin-guard companion + rejection page (M.12.7):**
- `identityProvider.ts` — SPA classifier mirroring backend `SocialIdpValues` + `SocialProviderKeys` sets.
- `useAdminGuard()` hook — returns `{ status: 'loading' | 'unauthenticated' | 'ok' | 'social-admin-rejected', identityProvider? }`. Uses `useAuthedQuery` on `/me` + `/me/tenants` (both whitelisted). Predicate mirrors backend Layer 2.
- `/auth/admin-social-idp-rejected?provider=<idp>` (NEW page) — reads `?provider` for Google/Microsoft/Facebook/Apple naming; falls back to generic copy. CTA `Sign out and try again` wired to `useAuth().signOut`.
- `admin/error.tsx` — adds branch for `problem.type === '.../admin-social-idp-rejected'` linking to the error page.
- `select-tenant/page.tsx` — wrapped in `<AdminAuthGuard>` per plan §6.3.

### Docs

- **Runbook** `docs/runbooks/social_idp_setup.md` — 4 provider portal setups (Google, Microsoft consumer, Facebook via Meta, Apple with .p8 JWT rotation), Entra user flow config, Bicep + Key Vault audit table, 15-min smoke walk, break-glass recovery. (M.12.5)
- **ADR** `docs/adr/0016-admin-vs-social-idp-surface-split.md` — records the 3-layer split + 4 alternatives considered + why each was rejected. (M.12.5)
- **Plan** `docs/OPS_M_12_SOCIAL_IDPS_PLAN.md` — pre-existing plan doc; §11 has the owner-locked answers (2026-07-05).

### Arch tests

- `OpsM12_IdentityProviderAccessorShapeTests` (M.12.1) — 5 facts locking the accessor shape.
- `OpsM12_AdminSocialIdpRejectionShapeTests` (M.12.2) — 6 facts locking exception, middleware, ProblemDetails wire order, and IdentityModule pipeline order.
- `OpsM12_SocialIdpShapeTests` (M.12.8 — this slice) — 8 consolidated **cross-surface** facts:
  1. Backend `SocialIdpValues` = the 7 owner-locked hosts.
  2. Backend `SocialProviderKeys` = the 4 canonical keys.
  3. SPA `identityProvider.ts` mirrors the 7 hosts.
  4. SPA `identityProvider.ts` mirrors the 4 keys.
  5. `user_identities.provider` CHECK constraint migration includes 'facebook' + all 4 keys.
  6. `IdentityProviderClassifier.Classify(string, string) → string` signature exists.
  7. `ProvisionOrLinkUserHandler` calls `RefuseIfAdminSocialLinkAsync` on BOTH Branch 2 and Branch 3, and references the rule string `admin_social_signin_refused`.
  8. SPA route `/auth/admin-social-idp-rejected/page.tsx` exists and calls `useAuth`.

The M.12.8 cross-surface pack is the guardrail against a change on one side (backend widens the social set / SPA classifier removes a value / migration deletes a key from the CHECK constraint / anyone renames the rule string) breaking the invariant silently.

---

## 2. Sub-commit chronology

| Commit | Slice sub-step | Notes |
| --- | --- | --- |
| `fbd3039` | M.12.1 | `ICurrentUser.IdentityProvider` accessor + `TenantIssuerHost` config + 5 arch facts |
| `f90ceb2` + `5b9ff84` (fixup) | M.12.2 | Middleware + Exception + ProblemDetails wiring + IdentityModule pipeline + 9 unit tests. **Fixup:** CA1822 fires as error in the CI SDK where local only fires S2325; changed `Rule` from instance property to `const string` |
| `91b3a6c` | M.12.3 | `IdentityProviderClassifier` + migration widening CHECK to include `facebook` + `UserProvisioningMiddleware` wiring |
| `b7fcf0f` + `86e34f1` (fixup) | M.12.4 | REFUSE-AT-PROVISIONING helper + Branch 2 + Branch 3 call sites + 6 integration tests. **Fixup:** S3878 (redundant array-creation in `BeEquivalentTo`) fails `dotnet format --verify-no-changes` in CI |
| `e8bac84` | M.12.5 | Docs-only: runbook + ADR-0016 + ADR README index update |
| `865e1a0` | M.12.6 | SPA per-flow authority + `/auth/signin` + `AdminAuthGuard` + callback routing + Bicep env vars + `appsettings.json` `TenantIssuerHost`. See §3 for the Bicep-secret cutover trap that surfaced during this deploy |
| `ed40c50` | M.12.7 | SPA `identityProvider.ts` + `useAdminGuard` + rejection error page + `admin/error.tsx` branch + `select-tenant` guard |
| _(this commit)_ | M.12.8 | Cross-surface arch tests + MASTER_PLAN row + this close-out + legacy `NEXT_PUBLIC_ENTRA_AUTHORITY` fallback drop + `10-store-secrets.ps1` sync + `.env.local.example` sync |

**Total: 8 code sub-commits + 2 fixups + 1 planning commit (`OPS_M_12_SOCIAL_IDPS_PLAN.md`, pre-slice).**

---

## 3. Where the plan diverged

### Followed the plan mostly

- Owner policy §11 answers held throughout — no re-litigation.
- Two-layer defence held: Layer 1 REFUSE-AT-PROVISIONING at the invariant (§2.2), Layer 2 middleware belt (§3), matching flows on Entra portal (§5).
- Owner-locked list of 4 IdPs held: Google + Microsoft consumer + Facebook + Apple.

### KV secret bind-before-deploy trap surfaced during M.12.6

Bicep deploy of M.12.6 (`865e1a0`) failed because the three new secretRef bindings (`entra-tenant-issuer-host`, `entra-web-authority-admin`, `entra-web-authority-guest`) had no matching KV secret at deploy time. Container Apps resolve `secretRef` at revision-provision time via managed identity; a missing KV secret fails the whole `main.bicep` deployment atomically.

Fix: seeded the three secrets with `pending-identity-setup` placeholder values (matching the `10-store-secrets.ps1` convention) directly via `az keyvault secret set` and reran the failed CI job. Same commit, CI green.

Durable countermeasures shipped in the same slice:
- `infra/scripts/10-store-secrets.ps1` now includes the 3 new seed entries so a fresh RG bootstrap works.
- Memory reference `reference_kv_secret_bind_before_deploy` captured to catch this before-push next time.

### CA1822 vs S2325 CI-vs-local drift surfaced during M.12.2

Adding a hardcoded `Rule` property on `AdminSocialIdpRejectedException` fired `S2325` locally (silenced with `#pragma`) but `CA1822` in the Docker `dotnet publish` where the CI SDK ships stricter analyzer defaults. Fix: hoist to `const string`. Memory reference `reference_ci_docker_analyzer_stricter_than_local` updated.

### S3878 in `dotnet format --verify-no-changes` surfaced during M.12.4

Adding integration tests that use `BeEquivalentTo(new[] { ... })` fired Sonar S3878 as a **warning locally** but CI's `dotnet format --verify-no-changes` exits 2 on any style finding. Fix: use FluentAssertions params overload. Memory reference updated to include this.

### No scope deferrals

Every sub-commit landed exactly the surface the plan §M.12.X said it would. The scale grew from 7 sub-commits (original plan) to 8 (approved 2026-07-05 for 4-IdP + REFUSE-AT-PROVISIONING branch) but nothing was cut from the approved scope.

---

## 4. What was deferred / follow-ups

- **Operator sets real KV secret values** — `entra-tenant-issuer-host`, `entra-web-authority-admin`, `entra-web-authority-guest`, and 4-provider client-id/secret pairs. Follow `docs/runbooks/social_idp_setup.md` §3–§8. Until this runs, the app operates with `pending-identity-setup` placeholder authority URLs — social sign-in returns a portal error but the code paths + fallback to legacy authority (until M.12.8 drops it) keep the app functional.
- **Actual portal setups** — the Google/Microsoft/Facebook/Apple portal registrations are operator steps documented in the runbook. Each provider registration takes 10–30 minutes and requires external credentials + (for Apple) a $99 Developer Program membership.
- **Apple client-secret JWT rotation** — 6-month cadence; operator must regenerate + push to KV. Runbook §6 step 7 + §10 break-glass documents the process.
- **`_pre_m13_snap` schema cleanup** — inherited from M.13 close-out; unchanged by M.12.
- **OPS.M.15 (App Roles cleanup)** — remains open; no dependency on M.12 in either direction. When it lands, both surfaces (`ClaimTypes.Role` reads + `[Authorize(Roles=...)]` decorators) update uniformly.
- **Migration path for existing entra admins** — no data-heal required (per §11-Q9). M.13.4 backfill covered it. Existing admins keep working; the M.12 layers only take effect on NEW social sign-in attempts.

---

## 5. Rollback runbook

M.12 is code + migration + config + docs only. The migration is safely reversible; the exception / middleware layers can be reverted without touching users.

- **Tier-1 (code revert)** — revert the M.12.1..M.12.8 commit chain and CD-api rolls in < 10 min. The Migrator job would try to `Down` the `OpsM12_UserIdentitiesProviderAddFacebook` migration. Two behaviors:
  - If no user has yet linked a `facebook` identity: safe.
  - If any user has: the `Down` fails on the CHECK constraint until those rows are deleted. Manually `DELETE FROM identity.user_identities WHERE provider='facebook'` first.
- **Tier-2 (SPA only revert)** — revert `web/**` commits; keep backend. Users route through the legacy single-authority + guest-only-if-in-Entra flow. Backend layers still enforce policy. No data change.
- **Tier-3 (backend only revert)** — revert backend commits; keep SPA. SPA becomes belt-only (`AdminAuthGuard` still fires the client-side reject) but the API accepts social-IdP admin tokens if any slip through. Not recommended in prod — leaves an admin-token gap.

No data-heal rollback needed — no data was rewritten in place.

---

## 6. Staging walk verification

**Backend endpoints (owner runs after operator sets real portal + KV secrets):**

- **Guest Google sign-in on a fresh email** — `POST /api/v1/auth/provision-or-link` returns 200; `identity.user_identities` shows one row `provider='google'`. `GET /api/v1/me` returns `identityProvider: "google"`.
- **Guest Google sign-in on an existing entra-only guest email** — 200; `user_identities` now has both `provider='entra'` + `provider='google'` for that user.
- **Google sign-in on a `is_platform_admin=true` user with matching email** — 422 with `rule=admin_social_signin_refused`. No `provider='google'` row added.
- **Google sign-in on a user with any `tenant_memberships` row** — 422 same rule.
- **`GET /api/v1/admin/bookings` on a token whose `idp='google.com'` for a user who was somehow granted admin authority (test scenario only)** — 403 with `type=.../admin-social-idp-rejected` and `rule=admin_authority_requires_entra_local` extension.
- **`GET /api/v1/me` on the same social-admin token** — 200. Whitelist held.

**Frontend flows:**

- `/auth/signin` (no params) → routes to guest flow → picks Google button → completes → callback page → land on `/`.
- `/admin/*` unauthenticated → `AdminAuthGuard` redirects to `/auth/signin?flow=admin&returnTo=/admin/...`.
- Admin flow → completes with Entra local → callback page → M.13.5 picker or `/admin`.
- Admin who signed in with Google (test scenario) → `AdminAuthGuard` detects via `useAdminGuard` → redirect to `/auth/admin-social-idp-rejected?provider=google.com` → CTA `Sign out and try again` triggers MSAL logout.

Owner drives the walk once portal + KV secrets are set. Findings + follow-ups append to §4.

---

## 7. Prod cutover checklist

M.12 is prod-safe on merge to `main`. The migration runs on next Migrator job invocation.

1. `OpsM12_UserIdentitiesProviderAddFacebook` — `ALTER TABLE identity.user_identities DROP CONSTRAINT ... ADD CONSTRAINT ... CHECK (provider IN ('entra','google','microsoft','apple','facebook','test'))`. Cheap; no row updates; instant.

**Sequential; no cross-schema references. Rolling deploy safe** — OLD API image cannot insert `provider='facebook'` (nothing configured to yet), so the CHECK widening is a strict superset of the OLD accepted set.

**Post-deploy smoke:**

- `GET /api/v1/me` on an existing session — response shape unchanged; the accessor addition is additive.
- Trigger an admin sign-in with the CIAM tenant's local Entra password — response 200; token carries `idp=null` or the tenant issuer host; classifier normalises to `entra`; `AdminAuthGuard` passes.
- If a social IdP has been configured on the portal + KV secrets are set: trigger a guest Google sign-in on a new email — 200; user + one social identity row.

If Prod is not yet configured with real portal secrets (likely at first cut), the SPA + backend fall back to the legacy `NEXT_PUBLIC_ENTRA_AUTHORITY` fallback until M.12.8 drops it. This close-out drops it — so the M.12.8 deploy in prod requires the operator to have already populated `entra-web-authority-admin` + `_guest` KV secrets. Follow the runbook.

---

## 8. Session debt

Nothing outstanding from this slice. Plan followed, three bridge decisions documented in §3:

- KV secret bind-before-deploy — traced to shape difference between manual bootstrap script and CI deploy; captured as durable memory + updated `10-store-secrets.ps1`.
- CA1822 vs S2325 CI-vs-local drift — captured as durable memory; workaround (const) applied.
- S3878 in `dotnet format` — captured as durable memory; params-overload applied.

Follow-up work seeded in §4.
