# 17. Admin identity.users rows MUST be operator-pre-seeded before first sign-in

- **Status**: Accepted
- **Date**: 2026-07-08 (owner-locked policy) / 2026-07-09 (this ADR committed alongside M.22.8)
- **Deciders**: Solutions Architecture (M.22 planning consult 2026-07-07), Owner (owner-locked answer 2026-07-07)
- **Tags**: identity, entra, authorization, product-decision

## Context

Post-[ADR-0016](0016-admin-vs-social-idp-surface-split.md), admins are Entra-local only (no social IdPs). But the M.13 stack still lazy-provisioned `identity.users` rows on first sign-in — a `UserProvisioningMiddleware` Branch 3 fires for any authenticated request whose email hasn't been seen. Right for guests, wrong for admins:

- The very first admin sign-in on a fresh environment created a row with `is_platform_admin=false` and zero memberships. Nothing distinguished "the owner onboarding" from "a random Entra local signup."
- Admin authority requires an out-of-band `SeedTenantMembership` call OR SQL runbook AFTER first sign-in — chicken-and-egg for the first admin.
- Nothing forced operator intent BEFORE the admin's first sign-in. A vector for silent misconfig.
- Prod cutover story was "have SOME operator SSH into staging and run SQL" — not repeatable, not IaC.

Owner policy locked 2026-07-07:

> "Platform Admins and Tenant Admins should have DB entry with username/password which will work with Extra Id. Guest users should be able to use either username/password or gmail, microsoft, apple and fb ids for sign-in."

Restated 2026-07-08 (after a third occurrence of the assistant re-deriving from ADR-0016 instead of applying the policy as given): admins are pre-seeded by an operator; guests self-serve.

## Decision

Admin identity.users rows MUST be pre-created by an operator BEFORE the admin's first sign-in via the admin flow. Enforced at four surfaces:

### Surface 1 — DB shape (M.22.2)

`identity.users.pre_seeded_at timestamptz NULL`. Non-null timestamp = "operator vouched at this instant." NEVER mutated after linking (audit trail immutable). Null = guest row (lazy-provisioned).

### Surface 2 — Operator surface (M.22.2 + M.22.5 + M.22.6)

Three paths, one shape:

- **API endpoint** — `POST /api/v1/admin/platform/users/seed`, `[Authorize(Roles="PlatformAdmin")]`. Idempotent on normalized email. Body: `{email, displayName, isPlatformAdmin, tenantMemberships[]}`.
- **PowerShell** — `vrbook-admin.ps1 -Action seed-platform-admin|seed-tenant-admin|list|revoke` — direct psql via KV-stored connection string. Handles the chicken-and-egg case for the first admin (no bearer token available yet).
- **Bicep** — `seedPlatformAdmins` array parameter → env vars on the migrator job → `SeedPlatformAdminsBackfill.RunAsync` on every deploy. Declarative source-of-truth for pre-M.22 admin backfill + prod cutover.

All three paths converge on `pre_seeded_at = NOW()` + the same idempotent read-then-write pattern.

### Surface 3 — Middleware gate (M.22.4)

`UserProvisioningMiddleware` reads:

- The token's Entra flow marker via `HttpCurrentUser.EntraFlowTfpClaim` (`tfp`) first, `EntraFlowAcrClaim` (`acr`) second.
- The configured `EntraExternalId:AdminFlowName`.

When `isAdminFlow=true` AND no `identity.users` row with `pre_seeded_at IS NOT NULL` matches the token's email:

- Whitelisted path (`/api/v1/me` or `/api/v1/me/tenants`) → skip provisioning, continue. Downstream sees `UserId=null`.
- Non-whitelisted path → throw `AdminAccountNotProvisionedException` → 401 with problem type `admin_account_not_provisioned`.

Guest-flow tokens AND tokens with no flow marker (single-flow tenants) fall through to Branch 3 lazy provisioning unchanged. Self-serve guest signup is unaffected.

### Surface 4 — SPA UX (M.22.7)

`useAdminGuard()` detects the 401/403 on `/api/v1/me` (the deterministic signal for the unprovisioned-admin case). `AdminAuthGuard` redirects to `/auth/admin-not-provisioned?email=<claim>`. The page shows a clear "contact your operator" copy with actionable snippets for the operator seeding paths.

## Consequences

### Positive

- **Invariant in DB shape**: admin authority (`is_platform_admin=true` OR `tenant_memberships` row) implies `pre_seeded_at IS NOT NULL` on the linked users row. Queryable, verifiable, auditable.
- **Explicit operator intent**: no more "the first admin was the first person to sign in." Backfill list is code-reviewed in Bicep.
- **Chicken-and-egg closed**: PowerShell path handles the FIRST admin bootstrap without an API round-trip.
- **Prod cutover is IaC**: adding a team lead to `seedPlatformAdmins` in `main.bicep` + deploying seeds them declaratively. No portal + SSH juggling.
- **Guest UX unchanged**: guest lazy provisioning stays as-is. M.22 fires only for admin-flow tokens.
- **UI clarity**: an unprovisioned admin sees "Your account hasn't been provisioned yet" instead of a blank admin page + silent 401 loops.

### Negative

- **Two Entra user flows required for the gate to fire** — same requirement as ADR-0016. Legacy single-flow tenants (no `tfp`/`acr` on the token) get guest-flow behaviour for all sign-ins including admin. This is deliberate fail-safe — admin can still sign in, just without the pre-seed gate — but it means the gate is a no-op until the flow split ships. Prod cutover checklist: verify `tfp`/`acr` presence in the admin-flow token BEFORE relying on the gate.
- **Config coupling**: `EntraExternalId:AdminFlowName` must match the exact string Entra emits. Portal rename = KV bump. Runbook (M.22.11 follow-up) will document.
- **Two operator paths (PowerShell + Bicep) to maintain**: intentional; PowerShell is for the live-environment escape hatch, Bicep is the declarative default. Duplication risk is small because both call the same SQL shape.
- **Adds a middleware branch to the hot path**: overhead is one config read + one indexed DB query when `isAdminFlow=true`. Guest-flow tokens skip the entire branch.

### Neutral

- **First-admin bootstrap still needs a human operator**: this is intentional (owner policy). Automating it against Entra would require app-only Graph credentials + `User.ReadWrite.All` — rejected in plan §5-Q1 as too much attack surface for the marginal ergonomics gain.
- **`SeedTenantMembership` (M.10.2 F11.3) stays**: it handles the post-sign-in add-membership case; `SeedAdminUserCommand` is the pre-first-sign-in superset. Both compose cleanly.
- **SPA detection is 401/403-heuristic, not problem-type-specific**: acceptable for MVP; hardening tracked as M.22.10 follow-up.

## Alternatives considered

**A. VrBook backend calls Microsoft Graph via app-only credentials.**  
Would combine "create Entra user" + "create DB row" into one endpoint call. Rejected — needs Graph API integration + secret rotation + `User.ReadWrite.All` permissions (attacker foothold if leaked). Two-step operator flow (portal → CLI) is one-day-done and mirrors what owner is already doing.

**B. Email+oid pre-seed** (operator provides both).  
Rejected in plan §5-Q3 — email-only seed is simpler UX. `pre_seeded_at` timestamp distinguishes vouched vs random signup. First admin-flow sign-in captures the oid via middleware Branch 2.

**C. Leave lazy provisioning; use a background sweeper to detect + refuse admin signups.**  
Rejected — the whole point is to have the invariant in the write path, not remediated after the fact. A sweeper leaks a window where the fake admin exists.

**D. Force-password-change on first sign-in.**  
Accepted BUT enforced via Entra CIAM policy, not VrBook code (plan §5-Q2). Runbook update; no code impact.

## References

- [`OPS_M_22_ADMIN_PRESEED_PLAN.md`](../OPS_M_22_ADMIN_PRESEED_PLAN.md) — full design doc, §7 owner-locked answers.
- [`OPS_M_22_CLOSE_OUT.md`](../OPS_M_22_CLOSE_OUT.md) — slice close-out with sub-commit map + deviations.
- [ADR-0016 — Admin vs Social IdP surface split](0016-admin-vs-social-idp-surface-split.md) — foundation this ADR builds on.
- [ADR-0014 — App Roles for global, DB for per-tenant](0014-app-roles-global-db-per-tenant.md) — role authority axis (M.22 does not touch it; is_platform_admin flag + tenant_memberships table stay authoritative).
- `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/SeedAdminUserCommand.cs` — command + handler.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs` — admin-gate.
- `src/VrBook.Migrator/SeedPlatformAdminsBackfill.cs` — declarative backfill.
- `infra/scripts/vrbook-admin.ps1` — operator escape hatch.
- `web/src/app/auth/admin-not-provisioned/page.tsx` — SPA rejection UX.
