# OPS.M.8 — Super Admin Console (Plan, rev 1)

**Status**: Proposed — awaiting user review.
**Author**: Plan agent (architect) consult, 2026-06-27.
**MASTER_PLAN reference**: `docs/MASTER_PLAN.md` §2 row OPS.M.8 ("Super Admin console", 4-day estimate).
**MULTI_TENANCY reference**: `docs/MULTI_TENANCY_OPS_PLAN.md` §9 ("Super Admin Console") + §12 row 2 ("super admin is a role, not a shared login — minimum three named humans").
**Predecessors**:
- Slice OPS.M.0 ✅ (Entra External ID + App Roles per ADR-0014).
- Slice OPS.M.1 ✅ (`Tenant` aggregate + `Tenant.Suspend(reason, actorId)` + `Tenant.Reactivate()` + `TenantSuspended`/`TenantActivated` events — verified `Tenant.cs:99-122`).
- Slice OPS.M.2 ✅ (`ICurrentUser.TenantId` + DB-wins claim enrichment per ADR-0014).
- Slice OPS.M.3 ✅ (all `tenant_id` columns landed; cross-tenant reads need the OPS.M.9 bypass factory but cross-tenant **app-side** reads are gated by App Roles per this slice).
- Slice OPS.M.4 ✅ (`TenantAuthorizationBehavior` with the `IsPlatformAdmin(user)` seam returning `false` — verified `TenantAuthorizationBehavior.cs:85-89`; M.8 swaps the body).
- Slice OPS.M.5 ✅ (`SetTenantPlatformFeeBpsCommand` shipped dormant — verified `StripeOnboardingCommands.cs:28-29`; `[Authorize(Roles="Owner,Admin")]` on `TenantsAdminController.SetPlatformFee` — verified `TenantsAdminController.cs:25, :70`; `Tenant.UpdateStripeAccountReadiness` state machine + `Suspended` status — verified `Tenant.cs:161-179`).
- Slice OPS.M.6 ✅ (no direct surface; M.6's `IBackgroundCommand` bypass in `TenantAuthorizationBehavior.cs:49-52` is independent of the PlatformAdmin bypass).
- Slice OPS.M.7 ✅ (`MeTenantDto` + `OnboardingProgressDto` shape + `GetMyTenantQuery`/`GetMyTenantHandler` + `OnboardingProgress` derivation — verified `src/VrBook.Contracts/Dtos/Identity.cs:19-48`, `src/Modules/VrBook.Modules.Identity/Application/Tenants/Queries/GetMyTenantQuery.cs:1-60`, `src/Modules/VrBook.Modules.Identity/Application/Tenants/Common/OnboardingProgress.cs:1-42`).

**Sequence**: After Slice OPS.M.7; before Slice OPS.M.9 (RLS — adds an `app.is_platform_admin` connection-level bypass for cross-tenant reads), Slice OPS.M.10 (cross-tenant isolation test pack — adds every M.8 endpoint to its sweep). Phase 2 organizes the PlatformAdmin grant lifecycle (rotation, MFA-policy, IP allowlist) into a hardening slot; M.8 ships the contract surface plus a minimal-viable seed mechanism.
**Estimate**: **4 days, one engineer** — TDD-first, see §5.

This plan is the contract. Slice OPS.M.8 ships **(i) the `IsPlatformAdmin` claim source (DB column `identity.users.is_platform_admin` + Entra App Role `PlatformAdmin` + DB-wins claim enrichment in `UserProvisioningMiddleware`)**, **(ii) `TenantAuthorizationBehavior.IsPlatformAdmin` swap from `return false` to the real check**, **(iii) cross-tenant read endpoints `GET /api/v1/admin/platform/tenants` (paged list) and `GET /api/v1/admin/platform/tenants/{tenantId}` (detail)**, **(iv) `SuspendTenantCommand` + `ReactivateTenantCommand` exposing the already-shipped `Tenant.Suspend`/`Tenant.Reactivate` aggregate methods**, **(v) lit-up `SetTenantPlatformFeeBpsCommand` (controller swap to PlatformAdmin gating)**, **(vi) super-admin web routes under `/admin/platform/*` (gated by PlatformAdmin role) with three pages: tenants list, tenant detail, suspend/reactivate confirmation**, **(vii) `vrbook-admin promote --email <addr>` Powershell cmdlet for ops-only PlatformAdmin grants**, **(viii) arch tests pinning every PlatformAdmin endpoint to the role gate**.

Impersonation (the MULTI_TENANCY_OPS_PLAN §9 "Act as TenantX" claim-swap), force-refund, global feature-toggle UI, MFA-policy enforcement at Entra level, and `/super-admin/*` IP allowlist are **explicitly OUT** of M.8 — they ship after Phase 1.5 demo-able. Open question O1 (§Appendix B) flags impersonation as the next-most-likely follow-up; the user can promote it into M.8 scope if a 4→6-day re-estimate is acceptable.

RLS-level `app.is_platform_admin` connection flag is Slice OPS.M.9; M.8's app-level bypass is the only enforcement until M.9 lands. Both are necessary: M.8 gates *which controllers a request can reach*, M.9 gates *which rows the connection can read*. The two layers are belt-and-braces per MULTI_TENANCY_OPS_PLAN §6 "defense-in-depth".

---

## 1. Scope summary

### 1.1 What this slice ships

| # | Deliverable | Touches |
|---|---|---|
| 1 | **Schema migration**: `identity.users.is_platform_admin boolean NOT NULL DEFAULT false`. Atomic-deploy wave 1. | `Migrations/2026MMDD_OpsM8a_Users_IsPlatformAdmin.cs`, `UserConfiguration.cs`, `User.cs` (add `IsPlatformAdmin` property + `GrantPlatformAdmin`/`RevokePlatformAdmin` methods). |
| 2 | **`ICurrentUser.IsPlatformAdmin` contract** (bool, non-null). | `src/VrBook.Contracts/Interfaces/ICurrentUser.cs` (extend interface). |
| 3 | **`HttpCurrentUser.IsPlatformAdmin` implementation**: reads the `users.is_platform_admin` column via the stamped `AppUserIdItemKey` (DB-wins precedence per ADR-0014). Fallback to the Entra `roles="PlatformAdmin"` claim for the dev-bridge edge case where the DB row is missing. | `HttpCurrentUser.cs`, `UserProvisioningMiddleware.cs` (re-read `is_platform_admin` after `ProvisionUserCommand`). |
| 4 | **`TenantAuthorizationBehavior.IsPlatformAdmin` swap**: replace `return false` with `return user.IsPlatformAdmin`. Single-line change; the pre-existing structured-log line (M.4 §3.5) already records the bypass event. | `TenantAuthorizationBehavior.cs:85-89`. |
| 5 | **`SuspendTenantCommand` + handler**: PlatformAdmin-only; calls `Tenant.Suspend(reason, actorId)`. Implements `IAuditable` (action `tenant.suspend`). Does NOT implement `ITenantScoped` (D5). | New `src/Modules/VrBook.Modules.Identity/Application/Platform/Commands/SuspendTenantCommand.cs` + handler. |
| 6 | **`ReactivateTenantCommand` + handler**: PlatformAdmin-only; calls `Tenant.Reactivate()`. Implements `IAuditable` (action `tenant.reactivate`). Same shape as Suspend. | Same file. |
| 7 | **Lit-up `SetTenantPlatformFeeBpsCommand` controller gate**: move from `TenantsAdminController` (`[Authorize(Roles="Owner,Admin")]`) to a new `TenantsPlatformController` (`[Authorize(Roles="PlatformAdmin")]`). The command itself is reused — only the entry point changes. | Delete the `[HttpPut("platform-fee")]` action on `TenantsAdminController.cs:70-81`; add it under the new platform controller at the new route. |
| 8 | **`GET /api/v1/admin/platform/tenants` (paged list)**: returns `PlatformTenantListItemDto` rows (extends `MeTenantDto` with `OwnerEmail`, `OwnerUserId`, `CreatedAt`, `LastActivityAt`, `SuspendedReason`). Query params: `?page=1&pageSize=25&status=Active&search=lima`. | New `TenantsPlatformController.cs` + `ListPlatformTenantsQuery` + handler. |
| 9 | **`GET /api/v1/admin/platform/tenants/{tenantId}` (detail)**: returns `PlatformTenantDto` (same `MeTenantDto` shape + ops fields). Tenant id from the URL — the only place we trust the URL for a tenant id, because the call is explicitly cross-tenant + paired with the PlatformAdmin gate (§9). | Same controller + `GetPlatformTenantQuery` + handler. |
| 10 | **Super-admin web routes** at `/admin/platform/*`: tenants list page, tenant detail page, suspend/reactivate confirmation modal, set-platform-fee form. Sidebar conditionally renders a "Platform" nav group when `useMe().isPlatformAdmin === true`. | `web/src/app/admin/platform/page.tsx`, `web/src/app/admin/platform/tenants/[tenantId]/page.tsx`, `web/src/components/layout/AdminSidebar.tsx` (extend), `web/src/lib/api/platform.ts`, `web/src/hooks/usePlatformTenants.ts`. |
| 11 | **`vrbook-admin promote --email <addr>` Powershell cmdlet**: ops-only seed mechanism. Wraps a SQL UPDATE through `VrBook.Migrator` / `DbCli`. Documented in a runbook; not exposed as an API endpoint. | New `tools/ops/vrbook-admin.ps1` (or extend an existing ops script). |
| 12 | **DevAuth `Admin` persona promotion**: the existing `DevAuthPersonas.Admin` (verified `DevAuthHandler.cs:67-73`) gets a tenant_memberships seed row that also flips `users.is_platform_admin = true` for staging walkthroughs. Dev-only; no impact on prod. | Seed migration `Migrations/2026MMDD_OpsM8b_DevAuth_Admin_PlatformAdmin.cs`. |
| 13 | **Arch test** `Every_PlatformAdmin_endpoint_requires_the_PlatformAdmin_role`: reflection over `TenantsPlatformController` (and any future controller routed under `/api/v1/admin/platform/`) asserting both controller-level `[Authorize(Roles="PlatformAdmin")]` AND, defense-in-depth, a `currentUser.IsPlatformAdmin` check inside each handler. | `tests/VrBook.Architecture.Tests/PlatformAdminEndpointRoleGateTests.cs`. |
| 14 | **`MeTenantDto.IsPlatformAdmin` field** + bump to `MeDto` (the `GET /api/v1/me` response — verified `IdentityController.cs:21-26`). The web client reads this to conditionally render the Platform nav group. | `src/VrBook.Contracts/Dtos/Identity.cs` (extend `UserDto`). |
| 15 | **Structured logging contract**: every PlatformAdmin write emits a Serilog line with `actor_platform_admin_user_id`, `target_tenant_id`, `action`, plus the standard `tenant_id` (which for these handlers is the **target** tenant). Re-uses the existing M.4 `AuditLogBehavior` for the `audit_log` row write. | Handlers in items 5, 6, 7. |
| 16 | Operator runbook `docs/runbooks/platform-admin-seed.md` documenting `vrbook-admin promote` invocation, the three-named-humans-minimum policy (per MULTI_TENANCY_OPS_PLAN §12 row 2), and how to revoke. | New `docs/runbooks/platform-admin-seed.md`. |
| 17 | **Component-level tests** (Vitest + RTL) for the platform list page + detail page + suspend confirmation modal. | `web/src/app/admin/platform/page.test.tsx`, `web/src/app/admin/platform/tenants/[tenantId]/page.test.tsx`. |
| 18 | **Integration tests**: `GET /api/v1/admin/platform/tenants` + `GET .../{tenantId}` + `POST .../{tenantId}/suspend` + `POST .../{tenantId}/reactivate` + `PUT .../{tenantId}/platform-fee`. Each test asserts: (a) 401 anonymous, (b) 403 authenticated non-PlatformAdmin (Owner persona), (c) 200/204 PlatformAdmin (Admin persona post-seed). | `tests/VrBook.Api.IntegrationTests/Identity/Platform/*.cs`. |

### 1.2 What's explicitly OUT of OPS.M.8

| Item | Owner slice | Why deferred |
|---|---|---|
| RLS connection-level `app.is_platform_admin = true` bypass | Slice OPS.M.9 | M.9 owns the RLS policy authoring + the `IRlsBypassDbContextFactory<TContext>` contract. M.8's app-level gate is the *only* barrier until M.9 lands — that's by design per MULTI_TENANCY_OPS_PLAN §6 ("application enforcement first, RLS as belt-and-braces"). The M.10 isolation test pack will assert both layers exist. |
| Impersonation ("Act as TenantX" 30-minute claim swap) | Deferred to follow-up (open question O1) | MULTI_TENANCY_OPS_PLAN §9 lists impersonation in the M.8 capability list, but the brief does NOT mention it. Carved out as O1 in §Appendix B; user can promote into M.8 by re-estimating to 6 days. |
| Force-refund a booking (dispute path) from the super-admin console | Phase 2 (post-launch hardening) | Force-refund is a destructive write that touches Stripe Connect refund logic shipped in OPS.M.5; the wiring is small but the operational risk surface is large (refund + dispute + audit). Phase 1.5's manual refund path (Stripe Express dashboard) is sufficient; the hard wiring waits. |
| Global feature toggles UI | Phase 2 | We don't have a feature-toggle table today. Adding one alongside the Super Admin console couples scope creep into M.8. Slice OPS.5+ launch hardening can add a `platform.feature_flags` table; M.8's tenant list/detail/suspend/reactivate/fee is enough. |
| MFA enforcement at Entra policy level for `PlatformAdmin` | Slice OPS.6 / OPS.7 (key rotation + Entra hardening) | A Conditional Access policy in Entra requires Entra admin + portal work; not a code change. Documented as a follow-up in `docs/runbooks/platform-admin-seed.md` and `docs/identity/runbooks/entra-external-id-setup.md`. |
| IP allowlist for `/api/v1/admin/platform/*` | Slice OPS.4 / OPS.6 | Container App ingress IP allow-list at the platform level is an infra change (Bicep), not an app change. Belongs with the other launch-hardening items. |
| Audit-log read UI ("show me every PlatformAdmin action against tenant X") | Follow-up after M.8 | The audit data exists (M.4 `AuditLogBehavior` already records every PlatformAdmin command with the new IAuditable interface — see §3.6 D6). A read endpoint + UI on top is ~1 day of additional work; carved out as O2 (§Appendix B). |
| Three-named-humans-minimum policy enforcement in code | Not enforced in code; ops-policy only | MULTI_TENANCY_OPS_PLAN §12 row 2 says "minimum three named super_admin users". M.8's `vrbook-admin promote` does NOT count rows or block fewer-than-3. The policy is a runbook commitment; enforcing it in code adds zero security value (an attacker who can promote one user can promote three). Documented in `docs/runbooks/platform-admin-seed.md` §"Why three". |
| Phase 2 multi-org concept ("one PlatformAdmin per Org") | Phase 2 | Phase 1.5 has no `organizations` table; PlatformAdmin is a single platform-wide flag. The Phase 2 org concept (a PlatformAdmin acts within an Org boundary) is a relationship table on top of the `is_platform_admin` flag — additive, future-compatible. M.8 ships the flag; Phase 2 ships the relationship. |
| The `/super-admin/*` separate route prefix from MULTI_TENANCY_OPS_PLAN §9 | Same `/admin/platform/*` prefix used instead (D7) | MULTI_TENANCY_OPS_PLAN §9 says "Routes under `/super-admin/*`, separate from `/admin/*`". §3.7 D7 of this plan picks `/admin/platform/*` instead. Reasoning: the same Next.js admin shell (with its auth wrapper, layout, AdminSidebar) hosts both; splitting routes forces a duplicate shell. A PlatformAdmin is also an Owner/Admin of some tenant (or none — but they always need the admin chrome to navigate). |

### 1.3 Decision lock summary

| # | Decision | Locked verdict |
|---|---|---|
| D1 | Where does `IsPlatformAdmin` live? | **DB column `identity.users.is_platform_admin`** (NOT Entra app role alone, NOT config-list of emails). Entra `roles="PlatformAdmin"` claim is a soft path/fallback only. DB is authoritative per ADR-0014 DB-wins precedence. |
| D2 | How does `ICurrentUser.IsPlatformAdmin` materialize? | **DB-wins via `UserProvisioningMiddleware`**: after `ProvisionUserCommand` returns the app user id, the middleware re-reads the `users` row's `is_platform_admin` column and stamps `HttpContext.Items["VrBook:IsPlatformAdmin"]`. `HttpCurrentUser.IsPlatformAdmin` reads that item; falls back to the `roles="PlatformAdmin"` claim only if the item is absent (the dev-bridge edge case). |
| D3 | Which surface does `TenantAuthorizationBehavior.IsPlatformAdmin` bypass? | **Every `ITenantScoped` command, no scope restriction.** PlatformAdmin can write to any tenant id. The bypass is logged at `Information` (existing `TenantAuthorizationBehavior.cs:61-64`). |
| D4 | Read endpoint shape | **Two endpoints under `/api/v1/admin/platform/tenants`**: paged list + single detail. Both `[Authorize(Roles="PlatformAdmin")]` controller-level + `currentUser.IsPlatformAdmin` defense-in-depth check inside the handler. Return shape: `PlatformTenantListItemDto` (list rows) / `PlatformTenantDto` (detail) — both extend `MeTenantDto` with operator-only fields. |
| D5 | Write surface for cross-tenant ops | **Three commands**: `SuspendTenantCommand`, `ReactivateTenantCommand`, `SetTenantPlatformFeeBpsCommand` (lit up). All `IAuditable`; **none** implement `ITenantScoped` (D5 detail). They stamp `target_tenant_id` from the route segment after the PlatformAdmin gate. |
| D6 | Audit logging | **Re-use existing M.4 `AuditLogBehavior`**: mark every PlatformAdmin command `IAuditable`; `AuditLogEntry.Record` already captures `actor_user_id`, `target_id`, `before/after_json`. The behavior writes `<action>.failed` on exception (`AuditLogBehavior.cs:46-50`). No special-casing needed. |
| D7 | Web route prefix | **`/admin/platform/*`** (NOT `/super-admin/*` per MULTI_TENANCY_OPS_PLAN §9). Reuses the existing `AdminLayout` chrome; PlatformAdmin nav appears as a sidebar group conditional on `useMe().isPlatformAdmin`. |
| D8 | Promotion / demotion mechanism | **`vrbook-admin promote --email <addr>` ops Powershell only.** No web UI. Three-named-humans-minimum is a runbook policy, not code. |
| D9 | Tenant suspend semantics | **Narrow scope**: when `Tenant.Status == Suspended`, (a) block new bookings (booking handlers check tenant status), (b) allow existing booking lifecycle to complete, (c) tenant owners see an in-product banner via `MeTenantDto.Status === "Suspended"` (already shipped — `MeTenantDto.Status`), (d) `OnboardTenantStripeCommand` rejects with `tenant.suspended`. Property-create / publish surface returns 503 with `tenant.suspended` body. M.8 SHIPS only (a) and (c); (b) and (d) ship as follow-up tickets (open question O3 — §Appendix B). |

### 1.4 What OPS.M.5 / M.7 left for M.8 to clean up

1. **`SetTenantPlatformFeeBpsCommand` controller mapping**: §3.16 D16 of OPS.M.5 explicitly says "M.8 will gate `IsPlatformAdmin`". M.8 moves the endpoint to `TenantsPlatformController` under the PlatformAdmin gate.
2. **`TenantsAdminController.cs:70-81`** carries the dormant `[HttpPut("platform-fee")]` action under `[Authorize(Roles="Owner,Admin")]`. M.8 deletes that action and ships the same `SetTenantPlatformFeeBpsCommand` mediator call from the new platform controller. The `SetTenantPlatformFeeBpsCommand` itself is unchanged.
3. **`TenantAuthorizationBehavior.IsPlatformAdmin`** at line 85-89 returns `false` with a comment "Slice OPS.M.8 will replace this body with a real claim check (`user.HasRole("PlatformAdmin")`) once `users.is_platform_admin` flows into `ICurrentUser`". M.8 honors that prediction *almost* verbatim: the actual swap reads `user.IsPlatformAdmin` (the new `ICurrentUser` property) rather than `user.HasRole("PlatformAdmin")` — because the DB-wins precedence (D1/D2) means the DB column is authoritative, and `HasRole` is the soft path only. The behavior cares about the DB-resolved truth.
4. **`MeTenantDto.Onboarding.IsComplete`** derivation in OPS.M.7 §4.1 / D2 reports `IsComplete = HasStripeAccount && PropertyCount >= 1 && Status == "Active"`. M.8 does NOT change this. Specifically: a Suspended tenant is treated as `IsComplete = false`; the M.7 wizard's dashboard gate would normally redirect the suspended-tenant owner to `/admin/onboarding`. M.8 inherits that behavior; the tenant detail view in the platform console shows the Suspended status with the SuspendedReason for the operator's troubleshooting. (Open question O3 — §Appendix B: should suspended tenants land on a "tenant is suspended" surface instead of the wizard?)
5. **DevAuth Admin persona's `IsAdmin = true` claim** is mapped to `ClaimTypes.Role = "Admin"` (verified `DevAuthHandler.cs:124-127`). That `Admin` role is the legacy "global admin" used by many existing controllers (verified `Grep '[Authorize(Roles="Admin"' src/VrBook.Api`). M.8 introduces a NEW role `PlatformAdmin` distinct from `Admin`. Reasoning: the existing `Admin` role gates 10+ controller actions on "is this a tenant admin/manager"; promoting all of them to "is this a platform admin" would silently re-shape every authz check. We KEEP `Admin` as-is and add `PlatformAdmin` alongside. The DevAuth Admin persona's seed (item 12 above) grants both `Admin` (existing) AND `PlatformAdmin` (new) for staging convenience.

---

## 2. Atomic-deploy constraints

Steps 1→9 in §5 sequence into **three waves** (schema migration must land before the auth-check reads it; the cross-tenant read/write endpoints must reach prod with the bypass active or every PlatformAdmin endpoint 403s itself):

### Wave 1 — Schema (Step 1 alone)

Ship the `identity.users.is_platform_admin boolean NOT NULL DEFAULT false` migration in its own tag. Additive-only column; rollback is a column drop. The app continues to run unchanged (the column is unread by Wave-0 code).

**Acceptance**: `information_schema.columns` shows the column with `data_type='boolean'`, `is_nullable='NO'`, `column_default='false'`. The OPS.M.5a / M.3a precedent (`OpsM5a_Identity_Tenants_StripeReadiness`) is the template — verified `Migrations/20260627221243_OpsM5a_Identity_Tenants_StripeReadiness.cs:1-44`.

### Wave 2 — Auth source + bypass surface (Steps 2 + 3 + 4 + 14)

Ship in one tag:

1. `ICurrentUser.IsPlatformAdmin` interface bump.
2. `HttpCurrentUser.IsPlatformAdmin` implementation + `UserProvisioningMiddleware` re-read.
3. `TenantAuthorizationBehavior.IsPlatformAdmin` swap from `false` to `user.IsPlatformAdmin`.
4. `MeTenantDto.IsPlatformAdmin` + `UserDto.IsPlatformAdmin` field bumps.
5. DevAuth Admin persona seed migration (item 12 in §1.1) granting `is_platform_admin = true`.

**Why bundle these**: the seed migration is what makes the bypass *useful* in staging immediately. Without it the bypass is dead code in dev. The interface bump + impl + behavior-swap must land together because the interface bump touches the contract assembly which several modules consume.

**Acceptance**: a DevAuth Admin-persona request to a cross-tenant write (e.g. a `SetTenantPlatformFeeBpsCommand` targeting a tenant the Admin persona is not a member of) succeeds with HTTP 204 and emits the existing M.4 structured log line "PlatformAdmin bypass for {RequestType} on tenant {TenantId}".

### Wave 3 — Platform controller + commands + web (Steps 5 + 6 + 7 + 8 + 9 + 10 + 11)

Ship in one tag:

1. New `TenantsPlatformController` with the 5 endpoints (list, detail, suspend, reactivate, fee).
2. `SuspendTenantCommand` + `ReactivateTenantCommand` + the moved `SetTenantPlatformFeeBpsCommand` handler.
3. `ListPlatformTenantsQuery` + `GetPlatformTenantQuery` handlers.
4. `PlatformTenantListItemDto` + `PlatformTenantDto` contracts.
5. Web routes `/admin/platform/*` + sidebar Platform nav group.
6. Web API client `platform.ts` + hooks.
7. `vrbook-admin promote` cmdlet.
8. Arch test `PlatformAdminEndpointRoleGateTests`.

**Why bundle Wave 3**: Wave 3 = the "user-visible Slice OPS.M.8 ship". The wave is consumed by a single operator persona (the PlatformAdmin) so atomic delivery is the right shape; partial waves leave the console half-functional. The web sidebar conditionally renders the Platform group only when `me.isPlatformAdmin === true`, so a wave-2-only deploy is invisible to non-Platform users.

### Forward-replay constraint

M.8 introduces three new outbox events through aggregate state changes:

- `Tenant.Suspend` raises `TenantSuspended(TenantId, Reason, ActorId)` — already shipped in OPS.M.1 (`TenantEvents.cs:7`).
- `Tenant.Reactivate` raises `TenantActivated(TenantId)` — already shipped (`TenantEvents.cs:5`).
- `Tenant.SetPlatformFeeBps` does NOT raise an event today (verified `Tenant.cs:181-189`). M.8 does NOT add one — Open question O4 (§Appendix B): should it? The operator audit trail is captured by `AuditLogEntry` (which is sufficient for the M.8 user story); a domain event would let downstream consumers react to fee changes. Defer to a follow-up unless a real consumer materializes.

Replay safety: the outbox events are stable shapes; M.8 does not bump payloads.

### Per-environment deploy script

Each wave is one tag → one `azd deploy` (or equivalent) per environment. Wave 1's DB migration runs via the existing migrator path (`VrBook.Migrator`); Wave 2 and 3 are app-tier code changes only.

After Wave 3 lands in staging, the operator MUST run `vrbook-admin promote --email <human> --env staging` for each of the three named humans listed in `docs/runbooks/platform-admin-seed.md`. Production runs the same one-time seed for the production tenant's three named humans during the Phase 1.5 launch.

**Per OPS.M.5 §3.12 reminder**: M.5 already pinned the `OnboardingReturnUrl` Key Vault values; M.8 introduces no new config keys.

---

## 3. Design decisions

### 3.1 D1 — Where the `IsPlatformAdmin` claim lives

Three options were on the table:

- **(a) DB column on `identity.users.is_platform_admin`** — boolean, default false. Granted via `vrbook-admin promote`; revoked via the inverse.
- **(b) Entra App Role `PlatformAdmin`** assigned via `POST /users/{id}/appRoleAssignments` (the same mechanism ADR-0014 picked for the global `Owner` + `Admin` roles).
- **(c) Config list of allowed emails** in `appsettings.{env}.json` (`"PlatformAdmin": { "AllowedEmails": ["..."] }`).

**Verdict: (a) DB column.** Reasoning:

1. **Works in both DevAuth and Entra paths uniformly.** DevAuth bypasses Entra entirely (verified `DevAuthHandler.cs:95-132`); a pure-Entra grant via App Role doesn't reach DevAuth users without a config-bridge. A DB column flows through `UserProvisioningMiddleware` (which runs for both DevAuth and Entra paths — verified `UserProvisioningMiddleware.cs:29-91`); the DevAuth Admin persona gets `is_platform_admin = true` via the same seed migration that promotes any other user, no Entra-specific code path.
2. **Auditable**: the column has `updated_at` + `updated_by` from `AggregateRoot` (verified `UserConfiguration.cs:43-44`); every grant/revoke leaves a SQL-readable trail. The `AuditLogBehavior` captures the `GrantPlatformAdminCommand` write (when we ship one — see §3.8 D8 footnote).
3. **Survives Entra rotation.** Entra App Role assignments do NOT survive a `vrbook-api-<env>` app re-registration (the assignments are tied to the appId). If we ever re-register (and we did during OPS.M.0 cutover — verified `docs/adr/0012-entra-external-id-over-b2c.md` historical note), the App Role assignments would be lost. The DB column is durable.
4. **Doesn't conflate with the existing `Admin` role.** Per §1.4 row 5, the existing `Admin` role is used by ~10 controllers as a "tenant admin power user" semantics. Promoting `Admin` to mean "platform admin" would silently re-gate every existing endpoint. A distinct `is_platform_admin` flag (and a distinct `PlatformAdmin` Entra App Role for the soft path — D2) keeps the two scopes orthogonal.
5. **ADR-0014 DB-wins precedence applies cleanly.** ADR-0014 says "global roles → Entra App Roles" but it's also explicit that per-tenant roles → DB. The Entra App Role mechanism is the *soft path*; the DB is authoritative. `IsPlatformAdmin` fits the same shape: an Entra App Role can carry it in the token (D2), but the DB column is the source of truth.

**Rejected: (b) Entra App Role alone.** Reasons: doesn't reach DevAuth (would need a separate config-keyed override that re-introduces the C-style allowlist); loses durability on app re-registration; the App Role assignment lifecycle is a separate ops surface from the rest of the user management. We keep the App Role *concept* as a soft path (D2) so that an Entra-side grant works in production without a DB write — but the DB column is the canonical answer.

**Rejected: (c) Config list.** Reasons: config is deployment-time, not runtime; promoting a fourth named human requires a config change + redeploy, which is heavy-weight for a permission grant; secret rotation patterns don't naturally extend to "list of emails"; the production config repo is the wrong source-of-truth for who can act as a platform admin.

#### What the schema looks like

```sql
-- 2026MMDD_OpsM8a_Users_IsPlatformAdmin.cs
ALTER TABLE identity.users
  ADD COLUMN is_platform_admin boolean NOT NULL DEFAULT false;

CREATE INDEX ix_users_is_platform_admin
  ON identity.users (is_platform_admin)
  WHERE is_platform_admin = true;
```

The partial index (`WHERE is_platform_admin = true`) is for the "show me every platform admin" admin-only query — there will only ever be ~3-10 rows true, so a partial index is cheap and the query is fast.

#### Aggregate method

```csharp
// User.cs (additions)
public bool IsPlatformAdmin { get; private set; }

public void GrantPlatformAdmin(Guid actorId)
{
    if (IsPlatformAdmin) return;
    IsPlatformAdmin = true;
    Raise(new UserPlatformAdminGranted(Id, actorId));
}

public void RevokePlatformAdmin(Guid actorId)
{
    if (!IsPlatformAdmin) return;
    IsPlatformAdmin = false;
    Raise(new UserPlatformAdminRevoked(Id, actorId));
}
```

The two domain events land in `src/VrBook.Contracts/Events/UserEvents.cs` (or wherever the existing `UserRegistered`/`UserDeactivated` events live — see `Grep 'UserRegistered' src/VrBook.Contracts`).

**Decision: option (a) — DB column on `identity.users.is_platform_admin`, partial index, with two aggregate methods `GrantPlatformAdmin`/`RevokePlatformAdmin` and two domain events for observability. The Entra App Role `PlatformAdmin` is the soft path (D2) only.**

### 3.2 D2 — How `ICurrentUser.IsPlatformAdmin` is materialized

Per ADR-0014 ("DB-wins precedence" for per-tenant roles), and confirmed in OPS.M.2 (`UserProvisioningMiddleware` reads `tenant_memberships` on every authenticated request — verified `UserProvisioningMiddleware.cs:64-84`), the DB is the source of truth. The Entra/DevAuth claim is the soft path.

**The materialization chain on every authenticated request**:

1. **Entra or DevAuth issues the token / cookie** with the user's identity (`oid` claim) and potentially a `roles="PlatformAdmin"` claim if the user has the Entra App Role (production) or if the DevAuth persona has the role hard-coded (the new dev-side claim addition).
2. **`UserProvisioningMiddleware` runs** (per OPS.M.2 — `UserProvisioningMiddleware.cs:27-94`):
   - Calls `ProvisionUserCommand` to ensure an `identity.users` row exists.
   - Receives back the app user id; stamps `HttpContext.Items["VrBook:UserId"]`.
   - **NEW in M.8**: re-reads the just-provisioned `users` row's `is_platform_admin` column and stamps `HttpContext.Items["VrBook:IsPlatformAdmin"]`.
   - The DB read is piggy-backed on the same `IdentityDbContext` scope already used for the `tenant_memberships` enrichment (OPS.M.2). Zero new DB round-trips.
3. **`HttpCurrentUser.IsPlatformAdmin` getter** reads `HttpContext.Items["VrBook:IsPlatformAdmin"]`. Falls back to the `roles="PlatformAdmin"` claim if the item is absent.

#### Why the fallback exists

The fallback exists for one narrow case: **a request that bypasses `UserProvisioningMiddleware`**. There is no such case in production today (every authenticated request runs the middleware — verified `Program.cs` middleware chain). The fallback is documented defensive code; the test pack explicitly covers a path that does NOT set the HttpContext item and asserts the fallback fires.

**Crucially**: in production, the DB-stamped item ALWAYS wins. Reading the Entra `roles="PlatformAdmin"` claim alone would be a soft path: the claim says "Entra thinks this user is a platform admin", but the DB says authoritatively. We honor the DB.

#### The HttpCurrentUser implementation

```csharp
// HttpCurrentUser.cs (additions)
public const string IsPlatformAdminItemKey = "VrBook:IsPlatformAdmin";
public const string PlatformAdminRoleClaim = "PlatformAdmin";

public bool IsPlatformAdmin
{
    get
    {
        var ctx = accessor.HttpContext;
        if (ctx is null) return false;

        // DB-wins per ADR-0014: middleware stamps this from users.is_platform_admin
        if (ctx.Items.TryGetValue(IsPlatformAdminItemKey, out var v) && v is bool b)
        {
            return b;
        }

        // Soft-path fallback: Entra App Role claim (production) or DevAuth-stamped role.
        // Reached only when UserProvisioningMiddleware did NOT run (no such case in production today).
        return HasRole(PlatformAdminRoleClaim);
    }
}
```

#### The middleware addition

```csharp
// UserProvisioningMiddleware.cs (additions, after the tenant_memberships block)
var isPlatformAdmin = await db.Set<User>()
    .Where(u => u.Id == userId)
    .Select(u => u.IsPlatformAdmin)
    .FirstOrDefaultAsync(ctx.RequestAborted);

ctx.Items[HttpCurrentUser.IsPlatformAdminItemKey] = isPlatformAdmin;

// Also stamp a Role claim so downstream [Authorize(Roles="PlatformAdmin")] works.
if (isPlatformAdmin && ctx.User.Identity is ClaimsIdentity rolesIdentity)
{
    rolesIdentity.AddClaim(new Claim(ClaimTypes.Role, "PlatformAdmin"));
}
```

The Role-claim stamping is what makes `[Authorize(Roles="PlatformAdmin")]` work at the controller level. Without it, only the `currentUser.IsPlatformAdmin` defense-in-depth check inside the handler would fire (the controller would 401/403 first). Stamping the claim from the DB ensures `[Authorize]` and `ICurrentUser.IsPlatformAdmin` agree.

#### What this looks like end-to-end

| Path | Claim source | DB column | `ICurrentUser.IsPlatformAdmin` result |
|---|---|---|---|
| Entra prod, user has App Role + DB column true | `roles=["PlatformAdmin"]` in token | true | true (DB-stamped item wins; claim concurs) |
| Entra prod, user has App Role but DB column false | `roles=["PlatformAdmin"]` in token | false | **false (DB wins)** — Entra grant is moot until the DB column flips |
| Entra prod, user has no App Role but DB column true | no `roles` claim | true | true (DB-stamped item wins; middleware also stamps the Role claim on the request so `[Authorize(Roles="PlatformAdmin")]` works) |
| Entra prod, neither | no claim | false | false |
| DevAuth Admin persona, post-seed | `roles=["Admin","PlatformAdmin"]` (NEW) | true | true |
| DevAuth Admin persona, pre-seed | `roles=["Admin"]` (legacy) | false | false |
| DevAuth Guest persona | no claim | false | false |
| DevAuth Owner persona | `roles=["Owner"]` | false | false |
| Background worker (no `ICurrentUser`) | N/A | N/A | N/A — `IBackgroundCommand` bypasses `TenantAuthorizationBehavior` upstream (verified `TenantAuthorizationBehavior.cs:49-52`) |

**Decision: DB-wins per ADR-0014 — `UserProvisioningMiddleware` re-reads the `users.is_platform_admin` column on every authenticated request and stamps `HttpContext.Items` + a `ClaimTypes.Role` claim. `HttpCurrentUser.IsPlatformAdmin` reads the item with a defensive fallback to the role claim (covers the no-middleware edge case). The Entra `roles="PlatformAdmin"` claim is the soft path; the DB is the source of truth.**

### 3.3 D3 — The bypass surface in `TenantAuthorizationBehavior`

The seam already exists (verified `TenantAuthorizationBehavior.cs:85-89`):

```csharp
private static bool IsPlatformAdmin(ICurrentUser user)
{
    ArgumentNullException.ThrowIfNull(user);
    return false; // Slice OPS.M.8 will replace this body.
}
```

**M.8 replaces the body**:

```csharp
private static bool IsPlatformAdmin(ICurrentUser user)
{
    ArgumentNullException.ThrowIfNull(user);
    return user.IsPlatformAdmin;
}
```

**Surface impact**: every `ITenantScoped` MediatR command is gated by this behavior (verified `TenantAuthorizationBehavior.cs:41-44`). With `IsPlatformAdmin == true`, the behavior short-circuits and the handler runs against the command's stated `TenantId` regardless of `currentUser.TenantId`. Note that:

1. **The handler still needs the target tenant id to do the work.** `SuspendTenantCommand(Guid TenantId, string Reason)` carries the target. The controller stamps it from the route segment.
2. **The bypass does NOT skip validation or audit.** Pipeline order is `Validation → TenantAuthorization → AuditLog → handler` (verified `IdentityModule.cs:48-53`). PlatformAdmin commands run through validation and audit normally; only the tenant-equality check is short-circuited.
3. **The bypass logs at `Information`** — `TenantAuthorizationBehavior.cs:61-64` already emits "PlatformAdmin bypass for {RequestType} on tenant {TenantId}". This is the structured-log breadcrumb the operator-investigation runbook reads.

#### Does this mean PlatformAdmin can write to any aggregate?

Yes, BUT only via commands that implement `ITenantScoped`. Commands that don't implement the marker (the M.8 platform-admin commands themselves — D5) flow through the behavior with the `request is not ITenantScoped` short-circuit (`TenantAuthorizationBehavior.cs:41-44`), so the bypass isn't even consulted. That's intentional: the M.8 commands aren't tenant-scoped writes (they don't claim "this user is writing inside tenant X"), they're platform-scoped writes against a target tenant. The `[Authorize(Roles="PlatformAdmin")]` controller gate plus the `currentUser.IsPlatformAdmin` defense-in-depth check inside each handler is what authorizes them.

#### What about an Owner writing to their own tenant via a SetPlatformFeeBps command?

Pre-M.8 the dormant `SetTenantPlatformFeeBpsCommand` was reachable through `TenantsAdminController.SetPlatformFee` under `[Authorize(Roles="Owner,Admin")]` (verified `TenantsAdminController.cs:70`). An Owner of tenant X could call it for tenant X (the controller's `CallerTenantId()` was passed, the command's `TenantId` was tenant X, the behavior agreed). That path is removed by M.8: the action moves to the new `TenantsPlatformController` under `[Authorize(Roles="PlatformAdmin")]`. After M.8, ONLY a PlatformAdmin can call SetPlatformFee — even an Owner cannot adjust their own fee. This is the intended security stance per OPS.M.5 §3.16: PlatformAdmin is the role that owns the rate negotiation.

**Decision: `TenantAuthorizationBehavior.IsPlatformAdmin(user) => user.IsPlatformAdmin`. The bypass covers every `ITenantScoped` write, no scope restriction. The audit + validation pipeline still runs.**

### 3.4 D4 — Read endpoints

**Two endpoints under `/api/v1/admin/platform/tenants`**:

- `GET /api/v1/admin/platform/tenants` — paged list of every tenant.
- `GET /api/v1/admin/platform/tenants/{tenantId:guid}` — single tenant detail.

Both `[Authorize(Roles="PlatformAdmin")]` AND `currentUser.IsPlatformAdmin` defense-in-depth check inside the handler. Reasoning for double-gating in §7.

#### List endpoint

```
GET /api/v1/admin/platform/tenants?page=1&pageSize=25&status=Active&search=lima
Authorization: Bearer <Entra access token with PlatformAdmin role>
→ 200 OK { items: PlatformTenantListItemDto[], total: number, page: number, pageSize: number }
→ 401 Unauthorized (no bearer)
→ 403 Forbidden (authenticated but not PlatformAdmin)
```

**Query params**:
- `page` (1-indexed, default 1, max 1000)
- `pageSize` (default 25, max 100 — capped to prevent operator-side mistakes that pull all tenants in one round-trip)
- `status` (optional; one of `PendingOnboarding | Active | Suspended | Closed`)
- `search` (optional; case-insensitive prefix match on `slug` OR `display_name` OR primary-owner email — the Identity-side query uses `ILIKE 'search%'` on all three with an `OR`)

**Sort**: hard-coded `created_at DESC` for the M.8 ship. Sort-by-column is a follow-up.

#### Detail endpoint

```
GET /api/v1/admin/platform/tenants/{tenantId:guid}
Authorization: Bearer <Entra access token with PlatformAdmin role>
→ 200 OK { tenant: PlatformTenantDto }
→ 401 Unauthorized
→ 403 Forbidden (not PlatformAdmin)
→ 404 Not Found (tenant id doesn't exist; soft-deleted tenants ARE returned but flagged `DeletedAt`)
```

#### Why the URL carries the tenant id

This is the **single place** in the M.8 API surface where we trust a tenant id from the URL. Reason: the call is *explicitly cross-tenant* — the PlatformAdmin is reading "any tenant", not their own. The URL is the natural carrier; the alternative (`?tenantId=<guid>` query string) is equivalent in security but worse in convention. The PlatformAdmin gate is what authorizes the URL-derived id (§7).

#### Response shapes

Two DTOs land in `src/VrBook.Contracts/Dtos/Identity.cs` alongside `MeTenantDto`:

```csharp
public sealed record PlatformTenantListItemDto(
    Guid Id,
    string Slug,
    string DisplayName,
    string Status,                  // PendingOnboarding | Active | Suspended | Closed
    string DefaultCurrency,
    int PlatformFeeBps,
    string? StripeAccountStatus,
    bool ChargesEnabled,
    bool PayoutsEnabled,
    bool HasStripeAccount,
    int PropertyCount,
    string? OwnerEmail,             // primary tenant_admin's email
    Guid? OwnerUserId,              // primary tenant_admin's user id
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActivityAt, // max(updated_at) across tenant + memberships + properties for this tenant
    string? SuspendedReason);

public sealed record PlatformTenantDto(
    Guid Id,
    string Slug,
    string DisplayName,
    string Status,
    string DefaultCurrency,
    int PlatformFeeBps,
    string? StripeAccountStatus,
    bool ChargesEnabled,
    bool PayoutsEnabled,
    bool HasStripeAccount,
    int PropertyCount,
    OnboardingProgressDto Onboarding,  // reused from M.7
    string? OwnerEmail,
    Guid? OwnerUserId,
    string OwnerDisplayName,
    string SupportEmail,            // tenants.support_email
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActivityAt,
    string? SuspendedReason,
    string? StripeAccountId,        // exposed to PlatformAdmin for ops correlation
    int RecentBookingCount,         // last 30 days
    int LifetimeBookingCount,
    decimal LifetimeGrossRevenue);  // sum of completed bookings' totals
```

#### Notes on the DTO shape

1. **`OnboardingProgressDto` is reused verbatim** from M.7 (`src/VrBook.Contracts/Dtos/Identity.cs:46-48`). The same `DeriveNextStep` + `DeriveIsComplete` static helpers (`OnboardingProgress.cs:23-41`) compute the values. This is the §11 forward-link from M.7 §3.1 / §11.
2. **`OwnerEmail` + `OwnerUserId` + `OwnerDisplayName`** are the primary `tenant_admin` (i.e. the `tenant_memberships` row where `IsPrimary = true`). If no primary exists (e.g. the tenant was created without a membership for some reason), these are null + the empty string respectively.
3. **`LastActivityAt`** is a derived value: `max(tenant.updated_at, max(membership.updated_at), max(property.updated_at)) for this tenant_id`. It's a deliberately coarse signal for "is this tenant alive?". A more precise activity signal (last booking, last login) is a follow-up.
4. **`RecentBookingCount` / `LifetimeBookingCount` / `LifetimeGrossRevenue`** require cross-module reads from Booking + Payment. We introduce **one new cross-module contract** `IPlatformTenantStatsLookup` (same pattern as OPS.M.5 §3.4 `ITenantStripeContextLookup` and OPS.M.7 §4.2 `IPropertyCountByTenant`). The contract lives in `src/VrBook.Contracts/Interfaces/`; the implementation reads Booking + Payment via raw SQL through the existing `NpgsqlDataSource` (read-only path; safe). Alternative considered: separate `IBookingStatsByTenant` + `IPaymentStatsByTenant` — rejected as over-decomposition; the M.8 surface needs both stats in one round-trip on the detail page.
5. **`StripeAccountId`** is exposed to the PlatformAdmin but NOT to the tenant Owner via `MeTenantDto` (verified `MeTenantDto` does not include it). Reason: operator correlation needs the raw Stripe id; the tenant Owner does not (the M.7 wizard hides it).

#### Why server-derive `OnboardingProgressDto` for cross-tenant view

Per OPS.M.7 D2 ("The OPS.M.8 super-admin view of the same status uses the same DTO shape"), the M.8 detail page reuses `OnboardingProgressDto`. The derivation lives in `OnboardingProgress.cs` (verified). M.8 calls into the same helper with the cross-tenant `Tenant` aggregate's state. No duplication.

**Decision: `GET /api/v1/admin/platform/tenants` (paged list) + `GET /api/v1/admin/platform/tenants/{tenantId}` (detail). Both `[Authorize(Roles="PlatformAdmin")]` + defense-in-depth `currentUser.IsPlatformAdmin` handler check. Response shapes `PlatformTenantListItemDto` + `PlatformTenantDto` extend `MeTenantDto`/`OnboardingProgressDto` with operator-only fields. One new cross-module contract `IPlatformTenantStatsLookup` for booking + payment stats.**

### 3.5 D5 — Write surface: three commands, none ITenantScoped

Three commands ship under the PlatformAdmin gate:

1. **`SuspendTenantCommand(Guid TenantId, string Reason)`** — calls `Tenant.Suspend(reason, actorId)` (verified the aggregate method exists at `Tenant.cs:99-110`).
2. **`ReactivateTenantCommand(Guid TenantId)`** — calls `Tenant.Reactivate()` (verified `Tenant.cs:112-122`).
3. **`SetTenantPlatformFeeBpsCommand(Guid TenantId, int Bps)`** — already exists (verified `StripeOnboardingCommands.cs:28-29`); M.8 only moves the controller mapping.

#### Why none implement `ITenantScoped`

This is the load-bearing decision in this section. Three options were considered:

- **(A) All three implement `ITenantScoped`.** Pro: the existing `TenantAuthorizationBehavior` runs (and bypasses for PlatformAdmin); commands flow through a familiar pipeline. Con: the bypass is the *only* thing protecting the cross-tenant write — if a non-PlatformAdmin somehow reaches the handler (e.g. via a route mis-mapping), the behavior would BLOCK them because `currentUser.TenantId != scoped.TenantId` (the Owner of tenant X cannot Suspend tenant Y via M.8's command shape). Sounds safe. **But**: this conflates two concerns. The marker says "this command writes inside the caller's tenant". M.8's commands write into a target tenant *because* the caller is platform-scoped. Using the marker would misrepresent the contract.
- **(B) None implement `ITenantScoped`; rely entirely on the controller `[Authorize(Roles="PlatformAdmin")]` plus a handler-side defense-in-depth `currentUser.IsPlatformAdmin` check.** Pro: honest semantics — the commands aren't tenant-scoped, they're platform-scoped writes against a target. Con: a stray controller action that mediates a command but FORGETS the role gate could reach the handler; the handler's defense-in-depth check is the last line.
- **(C) New marker `IPlatformScoped` analogous to `ITenantScoped`; a new `PlatformAuthorizationBehavior` enforces.** Pro: symmetric structure; the pipeline is the single enforcement point. Con: scope creep for one slice; the §8 arch test (`PlatformAdminEndpointRoleGateTests` — item 13 in §1.1) achieves the same goal with reflection.

**Verdict: (B).** The PlatformAdmin authorization happens at two layers: (i) the controller `[Authorize(Roles="PlatformAdmin")]` filter, (ii) the handler's first-line check `if (!currentUser.IsPlatformAdmin) throw new ForbiddenException(…)`. Both are pinned by the §8 arch test. The pipeline behavior is *not* the enforcement point for these commands.

This matches OPS.M.7 §4.3 (the `GetMyTenantQuery` also does NOT implement `ITenantScoped` — verified `GetMyTenantQuery.cs:11-17`); the same reasoning applies symmetrically to writes.

#### Command shapes

```csharp
// src/Modules/VrBook.Modules.Identity/Application/Platform/Commands/SuspendTenantCommand.cs

/// <summary>
/// OPS.M.8 §3.5 (D5) — PlatformAdmin-only command to suspend a tenant.
/// Does NOT implement <see cref="ITenantScoped"/>: it is a platform-scoped
/// write against a target tenant id, not a tenant-scoped write inside the
/// caller's own tenant. The caller is authorized by the controller's
/// <c>[Authorize(Roles="PlatformAdmin")]</c> filter PLUS a defense-in-depth
/// <see cref="ICurrentUser.IsPlatformAdmin"/> check inside the handler.
///
/// <para>Implements <see cref="IAuditable"/>; the existing M.4 audit pipeline
/// (verified <c>AuditLogBehavior.cs:31-54</c>) captures <c>tenant.suspend</c>
/// (success) or <c>tenant.suspend.failed</c> (exception path) with
/// before/after JSON.</para>
/// </summary>
public sealed record SuspendTenantCommand(Guid TenantId, string Reason)
    : IRequest<Unit>, IAuditable
{
    public string AuditAction => "tenant.suspend";
    public string? AuditTargetType => "Tenant";
    public string? AuditTargetId => TenantId.ToString("D");
}

public sealed record ReactivateTenantCommand(Guid TenantId)
    : IRequest<Unit>, IAuditable
{
    public string AuditAction => "tenant.reactivate";
    public string? AuditTargetType => "Tenant";
    public string? AuditTargetId => TenantId.ToString("D");
}
```

`SetTenantPlatformFeeBpsCommand` already exists and is `ITenantScoped`. **M.8 keeps it `ITenantScoped`** for one reason: the OPS.M.5 arch test (`TenantScopedCommandTests`) was extended to assert the M.5 commands implement the marker (verified `TenantScopedCommandTests.cs:43-45`). Removing the marker would either need a test edit (architecturally invasive) or a comment-out (drift-prone). And: when a PlatformAdmin sets the fee, the bypass branch in `TenantAuthorizationBehavior` (now lit-up per D3) fires and lets the write through. Win-win: the existing pipeline handles it correctly under the new bypass. The two NEW commands (`SuspendTenantCommand`, `ReactivateTenantCommand`) do NOT implement `ITenantScoped` — they're new contracts and we get to set them right from day 1.

#### Handler defense-in-depth pattern

Every PlatformAdmin handler starts with this guard (mirroring `GetMyTenantHandler.cs:27-30`):

```csharp
public async Task<Unit> Handle(SuspendTenantCommand cmd, CancellationToken ct)
{
    if (!currentUser.IsPlatformAdmin)
    {
        throw new ForbiddenException(
            "Platform-admin role required. Bug: controller filter should have rejected this.");
    }
    // … target lookup + aggregate method call + SaveChangesAsync
}
```

The "Bug:" prefix is intentional: if this check ever fires, the controller filter failed to gate the route. The arch test (§8) is what prevents that drift, but the runtime check is the failsafe.

#### Handler implementations

```csharp
internal sealed class SuspendTenantHandler(
    IdentityDbContext db,
    ICurrentUser currentUser,
    ILogger<SuspendTenantHandler> logger)
    : IRequestHandler<SuspendTenantCommand, Unit>
{
    public async Task<Unit> Handle(SuspendTenantCommand cmd, CancellationToken ct)
    {
        if (!currentUser.IsPlatformAdmin)
        {
            throw new ForbiddenException("Platform-admin role required.");
        }
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == cmd.TenantId, ct)
            ?? throw new NotFoundException("Tenant", cmd.TenantId);

        var actorId = currentUser.UserId
            ?? throw new InvalidOperationException(
                "PlatformAdmin command requires a resolved actor user id.");

        tenant.Suspend(cmd.Reason, actorId);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "PlatformAdmin suspended tenant {TargetTenantId} (reason={Reason}, actor={ActorUserId})",
            cmd.TenantId, cmd.Reason, actorId);

        return Unit.Value;
    }
}
```

`ReactivateTenantHandler` is the same shape minus the `Reason` parameter. `SetTenantPlatformFeeBpsHandler` (existing, `StripeOnboardingCommands.cs:94-105`) gets a defense-in-depth `currentUser.IsPlatformAdmin` check added at the top.

#### Why no `actorId`-from-cmd

The `actorId` MUST come from `currentUser.UserId` server-side, NEVER from a request body. This is the same pattern OPS.M.4 enforces for tenant-scoped writes. The aggregate method `Tenant.Suspend(reason, actorId)` carries `actorId` into the raised `TenantSuspended(TenantId, Reason, ActorId)` event (verified `TenantEvents.cs:7`), which lands in the outbox and is the audit-trail-of-record for "who suspended this tenant".

**Decision: three PlatformAdmin commands (`SuspendTenantCommand`, `ReactivateTenantCommand`, lit-up `SetTenantPlatformFeeBpsCommand`). The two new commands do NOT implement `ITenantScoped` (B5); they're platform-scoped writes against a target. The pre-existing `SetTenantPlatformFeeBpsCommand` keeps `ITenantScoped` because the M.5 arch test pins it and the M.8 bypass lights the path up correctly. Every handler does a defense-in-depth `currentUser.IsPlatformAdmin` check first. The `actorId` always comes from `currentUser.UserId`, never from a request body.**

### 3.6 D6 — Audit logging via the existing `AuditLogBehavior`

The existing M.4 `AuditLogBehavior` (verified `AuditLogBehavior.cs:31-54`) writes an `AuditLogEntry` for every `IAuditable` MediatR request:

1. `before_json` = the serialized command before the handler runs.
2. `after_json` = the serialized response after success, OR null on exception.
3. `action` = the command's `AuditAction` property, with `.failed` suffix on exception.
4. `actor_user_id` = `currentUser.UserId`.
5. `actor_role` = derived from `currentUser.IsAdmin` / `IsOwner` (verified `AuditLogBehavior.cs:83-101`) — **PROBLEM**: this method does NOT know about `IsPlatformAdmin`. It would resolve a PlatformAdmin acting as such to "admin" if they're also an Admin (the DevAuth Admin persona case), or to "owner" / "guest" otherwise. **M.8 extends the method** to return `"platform_admin"` first:

```csharp
// AuditLogBehavior.cs (M.8 extension)
private static string ResolveRole(ICurrentUser u)
{
    if (!u.IsAuthenticated) return "anonymous";
    if (u.IsPlatformAdmin) return "platform_admin";  // NEW — highest precedence
    if (u.IsAdmin) return "admin";
    if (u.IsOwner) return "owner";
    return "guest";
}
```

This is a single one-line addition; the rest of the audit pipeline is unchanged. The arch test in §8 includes a fact asserting `"platform_admin"` is the role for a PlatformAdmin caller.

#### What the audit row carries

For `SuspendTenantCommand`:

| Column | Value |
|---|---|
| `actor_user_id` | The PlatformAdmin's app user id |
| `actor_role` | `"platform_admin"` |
| `action` | `"tenant.suspend"` (or `"tenant.suspend.failed"` on exception) |
| `target_type` | `"Tenant"` |
| `target_id` | The target tenant id (string) |
| `before_json` | `{"TenantId":"…","Reason":"…"}` (the serialized command) |
| `after_json` | `null` (Unit response is empty) |
| `tenant_id` | The PlatformAdmin's own `currentUser.TenantId`, which may be null if they're not an Owner of any tenant (per `AuditLogEntry.Record` parameter shape — `AuditLogEntry.cs:34-59`) |
| `ip_address` | Caller IP |
| `user_agent` | UA |
| `trace_id` | Activity-current trace id |

**Note on `tenant_id` semantics**: the column carries the *actor's* tenant id, not the *target*. The OPS.M.3 §1.7 footnote (carried into `AuditLogEntry.cs:13-19`) says tenant_id is nullable for PlatformAdmin actions. M.8 honors that. The target tenant is in `target_id`. A "show me every audit row that targeted tenant X" query reads `WHERE target_type='Tenant' AND target_id='<X>'`. (The audit-log read UI mentioned as deferred in §1.2 row 7 is the consumer of that query shape.)

#### Failure-path audit

The behavior writes `<action>.failed` audit rows on exception (verified `AuditLogBehavior.cs:46-50`). A `ForbiddenException` from a non-PlatformAdmin reaching a PlatformAdmin handler (the defense-in-depth firing) would land as `tenant.suspend.failed` with the command JSON in `before_json` — exactly what a security review wants to read.

#### No special-casing required

The audit pipeline already handles every shape M.8 needs. The only change is the `ResolveRole` extension and the new `IAuditable` annotations on the three commands. Confirmed: no new audit infrastructure, no new tables, no new migration.

**Decision: re-use the existing M.4 `AuditLogBehavior` verbatim; mark every M.8 command `IAuditable` with stable action strings (`tenant.suspend`, `tenant.reactivate`, `tenant.set_platform_fee`); extend `ResolveRole` to return `"platform_admin"` first. No new audit infrastructure.**

### 3.7 D7 — Super-admin web route prefix: `/admin/platform/*`

MULTI_TENANCY_OPS_PLAN §9 says "Routes under `/super-admin/*`, separate from `/admin/*`". M.8 picks `/admin/platform/*` instead.

#### Reasoning

1. **Reuse the existing AdminLayout chrome.** The `web/src/app/admin/layout.tsx` provides sidebar + header + auth-aware shell (verified — and OPS.M.7's deviation in `OPS_M_7_PLAN.md` §11 retained it as the AdminSidebar-based layout). A PlatformAdmin needs that chrome to navigate. Splitting routes to `/super-admin/*` forces a duplicate `SuperAdminLayout.tsx` with identical responsibilities. Not worth.
2. **A PlatformAdmin is usually also an Owner of some tenant** (the DevAuth Admin persona is the staging example; the seeded production PlatformAdmins will likely each be Owners of a small "test tenant" they use for QA). Their primary navigation is still the tenant admin chrome; the Platform group is the additional surface. Co-located routing keeps the mental model "Admin shell with a Platform nav group when entitled" rather than "two separate shells".
3. **`/admin/platform/*` URL shape signals the elevation cleanly.** A casual operator who lands on `/admin/platform/tenants` from a Slack-shared link is immediately oriented: this is the platform-admin surface inside the admin shell. The MULTI_TENANCY_OPS_PLAN §9 framing ("separate from `/admin/*`") was a security framing — *that* concern is satisfied by the `PlatformAdmin` role gate, not by a URL prefix.
4. **Auth boundary is enforced at the API**, not the URL. The web route `/admin/platform/tenants` calls `GET /api/v1/admin/platform/tenants`; the API filter is the security boundary. Web-side, the page would render an "Access denied" surface for a non-PlatformAdmin (mirroring M.7 §3.5's pattern for guest callers).
5. **Sidebar conditional nav**: the AdminSidebar (`web/src/components/layout/AdminSidebar.tsx` — verified at `AdminSidebar.tsx:1-91`) is the place to conditionally render the Platform nav group. The condition is `useMe().isPlatformAdmin` (the new `UserDto.IsPlatformAdmin` field per item 14 in §1.1).

#### The Platform nav group

A new sidebar subgroup, rendered ABOVE the existing tenant items, only when `useMe().isPlatformAdmin === true`:

```tsx
// AdminSidebar.tsx (M.8 addition)
{showPlatformGroup && (
  <div className="mb-2 border-b border-border pb-2" data-testid="platform-nav-group">
    <div className="px-3 pb-1 text-[11px] uppercase tracking-wide text-muted-foreground">
      Platform
    </div>
    <Link
      href="/admin/platform/tenants"
      className={cn(/* same active styling as items */)}
      data-testid="platform-tenants-link"
    >
      <Building2 className="h-4 w-4" aria-hidden />
      Tenants
    </Link>
  </div>
)}
```

#### Web routes

| Web route | Page | Purpose |
|---|---|---|
| `/admin/platform/tenants` | `web/src/app/admin/platform/tenants/page.tsx` | Paged list of every tenant. Filters: status, search. |
| `/admin/platform/tenants/[tenantId]` | `web/src/app/admin/platform/tenants/[tenantId]/page.tsx` | Single tenant detail; suspend/reactivate buttons; set-fee form. |

NO web-side `/admin/platform/audit-log` page in M.8 (item §1.2 row 7 carve-out).
NO web-side `/admin/platform/promote-admin` page (D8 lock — Powershell only).

**Decision: `/admin/platform/*` (NOT `/super-admin/*`). Reuses the existing AdminLayout + AdminSidebar; the sidebar conditionally renders a Platform group when `useMe().isPlatformAdmin === true`. Two web pages: tenants list, tenant detail.**

### 3.8 D8 — Promotion / demotion mechanism: ops Powershell only

No web UI. The only way to grant or revoke PlatformAdmin is the Powershell cmdlet:

```
.\tools\ops\vrbook-admin.ps1 promote --env staging --email niroshanaks@gmail.com
.\tools\ops\vrbook-admin.ps1 demote  --env staging --email niroshanaks@gmail.com
.\tools\ops\vrbook-admin.ps1 list    --env staging
```

#### Why no web UI

1. **Bootstrap problem.** A web UI to grant PlatformAdmin requires AN EXISTING PlatformAdmin to exist; the first one cannot be created via the web. We'd need either a "magic" first-ever-grant path (insecure) or a Powershell cmdlet anyway. Just ship the cmdlet.
2. **Operations-policy concern.** MULTI_TENANCY_OPS_PLAN §12 row 2 says "minimum three named super_admin users" — a runbook policy, not enforced in code. A web UI normalizes the grant flow; a Powershell cmdlet keeps it deliberate.
3. **Audit trail clarity.** A direct DB UPDATE through the migrator path is identifiable in postgres logs by connection identity (the migrator runs as the migrator role). A web grant would land in `audit.audit_log` correctly but feels "easy" — the operator could grant themselves PlatformAdmin from the web shell, which is the wrong-shape behavior.
4. **MFA enforcement at Entra level (deferred §1.2 row 5).** Until MFA is enforced for PlatformAdmin sessions, *any* PlatformAdmin can promote *any* other user via a hypothetical web UI; promote-via-web is a privilege-escalation primitive that we should make harder than a single click. Powershell + Az CLI + Key Vault access enforces a higher operator bar.

#### What the cmdlet does

```powershell
# tools/ops/vrbook-admin.ps1 (skeleton)
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true, Position=0)][ValidateSet('promote','demote','list')][string]$Action,
    [Parameter(Mandatory=$true)][ValidateSet('dev','staging','prod')][string]$Env,
    [string]$Email
)

. (Join-Path $PSScriptRoot '..\..\infra\scripts\_common.ps1')

$state = Read-State -Env $Env
$conn  = Get-PostgresConnection -Env $Env   # reads from Key Vault

switch ($Action) {
    'promote' {
        if (-not $Email) { throw "promote requires --Email" }
        $sql = @"
UPDATE identity.users
   SET is_platform_admin = true,
       updated_at        = NOW(),
       updated_by        = NULL  -- ops bootstrap; null actor by convention
 WHERE email = @Email;
SELECT id, email, is_platform_admin FROM identity.users WHERE email = @Email;
"@
        Invoke-Postgres -Conn $conn -Sql $sql -Params @{ Email = $Email }
        Write-Ok "Promoted $Email on $Env"
    }
    'demote' {
        # symmetric
    }
    'list' {
        Invoke-Postgres -Conn $conn -Sql "SELECT id, email, display_name FROM identity.users WHERE is_platform_admin = true ORDER BY email;"
    }
}
```

The cmdlet relies on:
- `infra/scripts/_common.ps1` (existing — verified `Read-State` helper exists; see `grant-self-admin.ps1:34`).
- A new `Invoke-Postgres` helper in `_common.ps1` (or inline `Add-Type 'Npgsql.dll'` etc. — the runbook specifies). Alternatively, shell out to `psql` via `az postgres flexible-server execute`.
- Connection string from Key Vault, NOT from a local file.

#### Three-named-humans policy

`docs/runbooks/platform-admin-seed.md` documents:

1. Why the minimum of three (operational continuity: any single admin can be unavailable; the bus factor of a single Super Admin in a production SaaS is unacceptable).
2. The three humans for production (names + emails go into the private ops vault, not the public docs).
3. The promotion procedure (run `vrbook-admin promote` from a sanctioned ops machine with Az CLI auth).
4. The demotion procedure (run `vrbook-admin demote`; document a 24h "are you sure?" delay before deleting their user row outright, in case of mistaken demotion).
5. The audit trail (every promote/demote is a SQL row update; the migrator-role connection identity is the actor proxy).

#### Why no domain event for grant/revoke

The aggregate methods raise `UserPlatformAdminGranted` / `UserPlatformAdminRevoked` events (D1) for the outbox. But the **cmdlet does NOT use the aggregate**; it issues a raw SQL UPDATE. Reason: the cmdlet runs outside the API, has no MediatR pipeline, no `IUnitOfWork`, no domain-event raise infrastructure. The events exist for a future web/API path (if we ever add one — deferred). For the cmdlet path, the SQL UPDATE is the action; the postgres audit log is the trail.

(Open question O5 — §Appendix B: should we ship a `GrantPlatformAdminCommand` + endpoint anyway, gated to PlatformAdmin, so that grant-by-other-PlatformAdmin runs through the MediatR pipeline and emits a domain event? Carved out; cmdlet-only suffices for M.8 ship.)

**Decision: ops Powershell only — `vrbook-admin.ps1 {promote|demote|list}`. The cmdlet issues a raw SQL UPDATE against `identity.users.is_platform_admin`. Three-named-humans is a runbook policy. Domain events `UserPlatformAdminGranted` / `UserPlatformAdminRevoked` are shipped on the aggregate but not raised by the cmdlet path (future API path would raise them).**

### 3.9 D9 — Tenant suspend semantics

The `Tenant.Suspend(reason, actorId)` aggregate method already exists (verified `Tenant.cs:99-110`) and transitions `Active → Suspended` with a stamped `SuspendedReason`. It raises `TenantSuspended(TenantId, Reason, ActorId)`. So *what does Suspended mean for the running system?*

The brief calls for "narrow scope (block new bookings, allow existing booking lifecycle to complete; tenant owner sees a banner; web 503 on owner property-create attempts)".

**M.8 ships a narrow scope; the broader enforcement is open as O3 (§Appendix B).** Specifically:

#### What M.8 SHIPS (the narrow scope)

1. **Tenant owners see an in-product banner.** The existing `MeTenantDto.Status` field (verified `Identity.cs:31` — `Status: string`) already carries `"Suspended"`. The M.7 wizard's gate logic (per OPS.M.7 §11 deviation: the gate was deferred; `AdminSidebar` reads `useMyTenant()` for the Continue-setup link). M.8 extends the AdminSidebar to render a "Tenant suspended" warning banner above the nav when `tenant.status === 'Suspended'`. ~5-line edit.
2. **The OPS.M.5 §3.5 D15 `BusinessRuleViolationException("payment.connect_account_missing", …)` already throws** if a tenant has no Stripe account. There is no analogous "tenant.suspended" check on the booking handler today. M.8 does NOT add one in scope; see O3 below.

#### What is OPEN (O3 — §Appendix B)

The brief mentioned "block new bookings, allow existing booking lifecycle to complete; web 503 on owner property-create attempts". Honest assessment:

- **Block new bookings**: requires extending the `PlaceBookingHandler` (Booking module) to check `Tenant.Status` and throw `BusinessRuleViolationException("tenant.suspended", …)` if Suspended. Cross-module read (needs the `ITenantStatusLookup` lookup or piggy-back on `ITenantStripeContextLookup`). ~1-2 hours including tests.
- **Allow existing bookings to complete**: the Booking aggregate's `Confirm`/`Cancel`/`CheckIn`/`CheckOut` methods don't check tenant status today. M.8 should NOT add the check on those (they're lifecycle transitions of already-placed bookings; we WANT them to complete).
- **503 on owner property-create**: requires extending the `CreatePropertyHandler` (Catalog module) similarly. ~1 hour.
- **Reject Stripe onboarding (`tenant.suspended` error code)**: extending `OnboardTenantStripeHandler` + `GenerateStripeAccountLinkHandler` (Identity module). ~30 minutes.

Total: ~3-4 hours of additional work to ship the full suspend semantics. **M.8 ships the banner + the aggregate state machine (already done in OPS.M.1) + the Suspend/Reactivate endpoints; the broader enforcement is open as O3.** User picks:

- **(o3a)** Add the 3-4 hours of enforcement work to M.8. Re-estimate to 4.5 days.
- **(o3b)** Ship M.8 at 4 days with the narrow scope. The broader enforcement ships as a 1-day follow-up "Slice OPS.M.8.1 — Tenant Suspended enforcement".
- **(o3c)** Hold off entirely; suspend is a "warning, not an enforcement" mechanism for Phase 1.5. Operators contact the tenant offline. M.8 still ships the aggregate + endpoints + banner; the booking/property/stripe handlers don't change.

**Default: (o3b)** — ship M.8 with the narrow scope; track the enforcement as a fast-follow. Rationale: the use cases for "suspended" in the early days of Phase 1.5 are likely operator-driven (a tenant payment is late; the operator suspends them via the console; they call the operator; the operator reactivates). The hard enforcement is more pressing once self-serve sign-up exists (Phase 2). Until then, the banner + aggregate state + audit trail is the right ship.

#### What Suspend does to the OPS.M.7 wizard

The M.7 wizard's `MeTenantDto.Onboarding.IsComplete` is `HasStripeAccount && PropertyCount >= 1 && Status == "Active"` (verified `OnboardingProgress.cs:37-41`). A Suspended tenant resolves `IsComplete = false` → the dashboard gate (deferred in M.7 §11 but the AdminSidebar's "Continue setup" link surfaces it) would direct the owner back into the wizard. That's not the right surface for a suspended tenant.

**M.8 narrow fix**: the AdminSidebar's "Continue setup" link is hidden when `tenant.status === 'Suspended'`. Instead, a "Tenant suspended" banner appears at the top of the sidebar. Same hook (`useMyTenant`), different conditional render. ~3-line edit:

```tsx
// AdminSidebar.tsx (M.8 edit)
const showContinueSetup = tenant
  && !tenant.onboarding.isComplete
  && tenant.status !== 'Suspended';   // NEW
const showSuspendedBanner = tenant && tenant.status === 'Suspended';   // NEW
```

The broader UX (e.g. a dedicated suspended-tenant landing page) is in O3.

#### What Suspend does to the Stripe onboarding state machine

OPS.M.5 §3.8 D8 `Tenant.UpdateStripeAccountReadiness` (verified `Tenant.cs:161-179`) has its own auto-Active / auto-Suspend logic:

- `PendingOnboarding` + both flags true → `Active`.
- `Active` + capability lost → `Suspended` with reason `stripe_capability_lost`.
- `Suspended` cannot auto-re-Activate from flags alone (operator must explicitly call `Reactivate()` after re-running readiness check).

The interaction with M.8's explicit `Suspend(reason, actorId)` / `Reactivate()`:

| Pre-state | Trigger | Post-state | Notes |
|---|---|---|---|
| Active | M.8 `Suspend("operator_action", actorId)` | Suspended, reason="operator_action" | Aggregate method already validates `Active → Suspended` (verified `Tenant.cs:102-110`). |
| Suspended (any reason) | `UpdateStripeAccountReadiness(true, true)` | Suspended (no change) | Per §3.8: Suspended cannot auto-re-Activate from flags alone. Correct behavior. |
| Suspended (reason="stripe_capability_lost") | M.8 `Reactivate()` | Active | The Stripe issue might still exist; the operator is signaling "we've verified offline that it's resolved or we accept the risk". |
| Suspended (reason="operator_action") | `UpdateStripeAccountReadiness(false, false)` | Suspended (no change) | Already Suspended; no event. |
| Suspended (reason="operator_action") | `UpdateStripeAccountReadiness(true, true)` | Suspended (no change) | Stripe is healthy but the operator suspension is what matters. |

This all works *as the aggregate methods already define*. No M.8 code change to the state machine; the Suspend/Reactivate commands just expose it.

**One edge case worth calling out**: a tenant that was auto-Suspended due to `stripe_capability_lost`, then operator-Reactivated, then Stripe flags drop again — the next `UpdateStripeAccountReadiness(false, false)` will re-Suspend with reason `stripe_capability_lost` (per the aggregate logic). The audit trail records both events. Correct.

#### Reason free-text vs enum

`Tenant.Suspend(reason: string)` accepts free-text (verified `Tenant.cs:99-110`). M.8 does NOT promote to an enum. Reasoning: the reasons are operator-authored ("operator_action", "billing_overdue", "fraud_investigation", etc.); a fixed enum locks the vocabulary too early. The DB column is `text NOT NULL` (or NULL when Active). The web form is a small text field with a few suggested-values dropdown options that fill the text field.

#### Validation rules on the SuspendTenantCommand

- `Reason` is required, non-empty, max 1000 characters (DB-column limit TBD; the column exists today as untyped — verify in the EF migration if needed).
- Trim whitespace; reject after-trim empty.

Standard FluentValidation; lands in `SuspendTenantCommandValidator.cs`.

**Decision: M.8 ships the narrow scope — Suspend/Reactivate aggregate methods (already done), commands (new), endpoints (new), tenant-owner banner (new). Broader enforcement (block new bookings, 503 on property-create, Stripe-onboarding reject) is open as O3 (§Appendix B); default verdict O3b (1-day follow-up slice OPS.M.8.1). The auto-state-machine for Stripe-capability-loss is unchanged; explicit Suspend layers on top. Reason is free-text with web-side suggested values.**

### 3.10 D10 — `UserDto` bump for `IsPlatformAdmin`

The web client must know whether to render the Platform nav group. The signal flows through `GET /api/v1/me` (the existing `IdentityController.cs:21-26` route returning `UserDto`).

**Decision: extend `UserDto` with `bool IsPlatformAdmin`**. Verified shape (Identity.cs:4-13):

```csharp
public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? Phone,
    bool IsOwner,
    bool IsAdmin,
    bool IsPlatformAdmin,           // NEW in M.8
    bool EmailVerified,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);
```

Position note: inserting `IsPlatformAdmin` after `IsAdmin` is a positional-constructor breaking change for callers using positional construction. The codebase uses named arguments at every call site (verified for the M.7 case, and the pattern is consistent for `UserDto`). Net impact: contract assembly recompile; consumers' named-arg call sites recompile cleanly. The web TypeScript shape (`web/src/lib/api/me.ts` or equivalent) gains the optional field; old web builds ignoring it work fine (defaults to false in the conditional render).

Handler change: `GetMeHandler` (wherever it lives — find via `Grep 'GetMeQuery'`) sources `IsPlatformAdmin` from the `User` aggregate's new property.

### 3.11 D11 — `IPlatformTenantStatsLookup` cross-module read

Per §3.4 D4 note 4, the `PlatformTenantDto` carries `RecentBookingCount`, `LifetimeBookingCount`, `LifetimeGrossRevenue`. These cross module boundaries.

**Decision: one new contract `IPlatformTenantStatsLookup` in `src/VrBook.Contracts/Interfaces/`** — same pattern as OPS.M.5 §3.4 `ITenantStripeContextLookup` and OPS.M.7 §4.2 `IPropertyCountByTenant`.

```csharp
namespace VrBook.Contracts.Interfaces;

public interface IPlatformTenantStatsLookup
{
    Task<PlatformTenantStats> GetStatsAsync(Guid tenantId, CancellationToken ct);
}

public sealed record PlatformTenantStats(
    int RecentBookingCount,        // last 30 days
    int LifetimeBookingCount,
    decimal LifetimeGrossRevenue,
    DateTimeOffset? LastActivityAt);
```

Implementation: a single class in the Booking module (or a new "Platform" cross-cutting module — judgement call) that issues a single SQL query joining `booking.bookings` + `payment.payment_intents` (read-only, no aggregate hydration). Owns:

```sql
SELECT
  count(*) FILTER (WHERE b.created_at >= NOW() - INTERVAL '30 days') AS recent_count,
  count(*)                                                            AS lifetime_count,
  COALESCE(sum(p.captured_amount), 0)                                  AS gross_revenue,
  max(GREATEST(b.updated_at, p.updated_at))                            AS last_activity
FROM booking.bookings b
LEFT JOIN payment.payment_intents p ON p.booking_id = b.id
WHERE b.tenant_id = @tenant_id;
```

The query is run via `NpgsqlDataSource` (read-only); not through a DbContext (cross-module aggregate hydration would be expensive). The trade-off is the same as OPS.M.7 §4.2 (which used EF Core); for stats we deliberately pick raw SQL because (a) we need a JOIN that EF would generate suboptimally, (b) this is a read-only operator query, (c) the column shape is value-stable.

Lives in `src/Modules/VrBook.Modules.Booking/Infrastructure/PlatformTenantStatsLookup.cs`; DI-registered in `BookingModule.cs`. Identity-side handler `GetPlatformTenantHandler` constructor-injects `IPlatformTenantStatsLookup`.

**Decision: `IPlatformTenantStatsLookup` in Contracts, raw-SQL impl in Booking module, no EF query. Single round-trip. ~30 lines.**

### 3.12 D12 — Pagination shape

The list endpoint returns:

```csharp
public sealed record PlatformTenantListResponse(
    IReadOnlyList<PlatformTenantListItemDto> Items,
    int Total,
    int Page,
    int PageSize);
```

Total is the unfiltered + filtered count post-search/status filter. Page is 1-indexed. PageSize is what the caller asked for, capped at 100.

Pagination is offset-based (`SKIP/TAKE`), not cursor. Reasoning: the tenant population is small (~hundreds in Phase 1.5, low-thousands at maturity); offset paging is fine. Cursor paging is a Phase 2+ concern when result-set stability under concurrent writes becomes a problem.

The query is sorted by `created_at DESC`; ties broken by `id`. Stable.

### 3.13 D13 — PlatformAdmin endpoint set; the bypass DOES NOT widen to GET endpoints

A subtle but important point: the M.8 PlatformAdmin gate (Wave 2 D3 / behavior swap) ONLY applies to MediatR `ITenantScoped` writes. The cross-tenant *read* endpoints (`GET /api/v1/admin/platform/tenants*`) do NOT need the behavior bypass; they need the controller `[Authorize(Roles="PlatformAdmin")]` filter PLUS the handler's defense-in-depth check.

So why is the bypass needed at all? Because the cross-tenant *write* endpoints (Suspend, Reactivate, SetFee) DO go through `TenantAuthorizationBehavior` (Suspend/Reactivate don't implement `ITenantScoped` — D5, so the behavior short-circuits in the `request is not ITenantScoped` check; SetFee DOES implement it and needs the bypass to fire).

Summary of which endpoints rely on what:

| Endpoint | `[Authorize(Roles="PlatformAdmin")]` | Handler `currentUser.IsPlatformAdmin` check | `TenantAuthorizationBehavior` bypass |
|---|---|---|---|
| `GET /admin/platform/tenants` | ✓ | ✓ | N/A (query, no `ITenantScoped`) |
| `GET /admin/platform/tenants/{id}` | ✓ | ✓ | N/A |
| `POST /admin/platform/tenants/{id}/suspend` | ✓ | ✓ | N/A (command does NOT implement `ITenantScoped` per D5) |
| `POST /admin/platform/tenants/{id}/reactivate` | ✓ | ✓ | N/A |
| `PUT /admin/platform/tenants/{id}/platform-fee` | ✓ | ✓ | **Required** (command implements `ITenantScoped`; the bypass is the only thing that lets the command target a tenant ≠ caller's) |

The bypass logic is therefore "load-bearing" only for the SetFee endpoint within the M.8 surface. But it remains essential for any future `ITenantScoped` command a PlatformAdmin might invoke against any tenant — `SuspendTenantCommand` and `ReactivateTenantCommand` are intentionally non-scoped, but other Phase 2 commands might be. Lighting up the bypass now is the right shape for the general policy "PlatformAdmin can write to any tenant's scope".

**Decision: behavior swap (D3) is the policy; the actual endpoint inventory in §4 enumerates which use which gate.**

---

## 4. Endpoint inventory

Every API endpoint M.8 ships, with HTTP method, request DTO, response DTO, role gate, and `ITenantScoped` interaction.

| Endpoint | Method | Request | Response | Role gate | New in M.8? | `ITenantScoped`? | Audit action |
|---|---|---|---|---|---|---|---|
| `/api/v1/admin/platform/tenants` | GET | (query string: page, pageSize, status, search) | `PlatformTenantListResponse` | `[Authorize(Roles="PlatformAdmin")]` + handler check | **YES** | No (query) | (read; no audit) |
| `/api/v1/admin/platform/tenants/{tenantId}` | GET | (no body) | `{ tenant: PlatformTenantDto }` | Same | **YES** | No (query) | (read; no audit) |
| `/api/v1/admin/platform/tenants/{tenantId}/suspend` | POST | `SuspendTenantRequest { Reason: string }` | 204 No Content | Same | **YES** | No (per D5) | `tenant.suspend` |
| `/api/v1/admin/platform/tenants/{tenantId}/reactivate` | POST | (no body) | 204 No Content | Same | **YES** | No (per D5) | `tenant.reactivate` |
| `/api/v1/admin/platform/tenants/{tenantId}/platform-fee` | PUT | `SetPlatformFeeRequest { Bps: int }` | 204 No Content | Same | **YES** (moved from `TenantsAdminController`) | Yes (existing M.5 command keeps marker) | `tenant.set_platform_fee` |
| `/api/v1/me` | GET | (no body) | `UserDto` (now with `IsPlatformAdmin`) | `[Authorize]` (existing) | No (bumped contract) | No | (read) |
| `/api/v1/admin/tenants/{tenantId}/platform-fee` | PUT | — | — | **REMOVED** (was `[Authorize(Roles="Owner,Admin")]` dormant; M.8 deletes it) | n/a | n/a | n/a |

### 4.1 Request DTOs

```csharp
// SuspendTenantRequest
public sealed record SuspendTenantRequest(
    [property: System.Text.Json.Serialization.JsonRequired] string Reason);

// SetPlatformFeeRequest — already exists (verified TenantsAdminController.cs:86-87)
public sealed record SetPlatformFeeRequest(
    [property: System.Text.Json.Serialization.JsonRequired] int Bps);
```

The Suspend request body carries only `Reason` (TenantId from URL, ActorId from currentUser server-side per §3.5 footer). Reactivate has no body (TenantId from URL).

### 4.2 Controller shape

```csharp
namespace VrBook.Api.Controllers;

/// <summary>
/// OPS.M.8 §3.4 + §3.5 — cross-tenant operator surface. Every action is
/// PlatformAdmin-only at the filter AND defense-in-depth at the handler.
/// The {tenantId} route segment is trusted because the call is explicitly
/// cross-tenant + paired with the role gate (§7).
/// </summary>
[Route("api/v1/admin/platform/tenants")]
[Tags("Platform — Super Admin")]
[Authorize(Roles = "PlatformAdmin")]
public sealed class TenantsPlatformController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "List every tenant (PlatformAdmin only).")]
    [ProducesResponseType(typeof(PlatformTenantListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PlatformTenantListResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default) =>
        Ok(await mediator.Send(new ListPlatformTenantsQuery(page, pageSize, status, search), ct));

    [HttpGet("{tenantId:guid}")]
    [SwaggerOperation(Summary = "Detail view of a single tenant (PlatformAdmin only).")]
    [ProducesResponseType(typeof(PlatformTenantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlatformTenantDto>> Get(
        Guid tenantId, CancellationToken ct) =>
        Ok(await mediator.Send(new GetPlatformTenantQuery(tenantId), ct));

    [HttpPost("{tenantId:guid}/suspend")]
    [SwaggerOperation(Summary = "Suspend a tenant. Active → Suspended.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Suspend(
        Guid tenantId, [FromBody] SuspendTenantRequest body, CancellationToken ct)
    {
        await mediator.Send(new SuspendTenantCommand(tenantId, body.Reason), ct);
        return NoContent();
    }

    [HttpPost("{tenantId:guid}/reactivate")]
    [SwaggerOperation(Summary = "Reactivate a suspended tenant. Suspended → Active.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reactivate(Guid tenantId, CancellationToken ct)
    {
        await mediator.Send(new ReactivateTenantCommand(tenantId), ct);
        return NoContent();
    }

    [HttpPut("{tenantId:guid}/platform-fee")]
    [SwaggerOperation(Summary = "Override the tenant's platform fee (basis points).")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPlatformFee(
        Guid tenantId, [FromBody] SetPlatformFeeRequest body, CancellationToken ct)
    {
        await mediator.Send(new SetTenantPlatformFeeBpsCommand(tenantId, body.Bps), ct);
        return NoContent();
    }
}
```

Note: every action passes `tenantId` (from URL) to the MediatR command. The bypass behavior (for SetPlatformFee) lets the command's `TenantId` ≠ `currentUser.TenantId`. The handlers' defense-in-depth checks fire.

### 4.3 Query handler shapes

```csharp
// ListPlatformTenantsQuery
public sealed record ListPlatformTenantsQuery(
    int Page, int PageSize, string? StatusFilter, string? SearchTerm)
    : IRequest<PlatformTenantListResponse>;

internal sealed class ListPlatformTenantsHandler(
    IdentityDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ListPlatformTenantsQuery, PlatformTenantListResponse>
{
    public async Task<PlatformTenantListResponse> Handle(
        ListPlatformTenantsQuery query, CancellationToken ct)
    {
        if (!currentUser.IsPlatformAdmin)
            throw new ForbiddenException("Platform-admin role required.");

        var page = Math.Clamp(query.Page, 1, 1000);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var q = db.Tenants.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.StatusFilter))
            q = q.Where(t => t.Status == query.StatusFilter);

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var s = query.SearchTerm.Trim().ToLowerInvariant();
            q = q.Where(t =>
                EF.Functions.ILike(t.Slug, $"{s}%") ||
                EF.Functions.ILike(t.DisplayName, $"{s}%"));
        }

        var total = await q.CountAsync(ct);
        var pageRows = await q
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        // … materialize PlatformTenantListItemDto with primary-owner email lookup,
        // PropertyCount via IPropertyCountByTenant (per-row N+1 acceptable at ~25 rows max).

        return new PlatformTenantListResponse(items, total, page, pageSize);
    }
}
```

`GetPlatformTenantQuery` is the same shape minus paging, with the additional `IPlatformTenantStatsLookup` call.

### 4.4 Removed surface

`TenantsAdminController.cs:70-81` (`[HttpPut("platform-fee")]`) is **deleted**. The migration moves the same MediatR command call to the new `TenantsPlatformController.SetPlatformFee` under the PlatformAdmin gate. The Owner-tenant-self-set path is intentionally removed; only PlatformAdmins can adjust platform fees per OPS.M.5 §3.16 D16.

---

## 5. Step-by-step TDD plan (Red → Green)

Every step is red-first. Red commit + green commit are tracked in the §11 ledger.

### Step 1 — Schema migration (XS, ~1h) — Wave 1

**Tests (red first)** — `tests/VrBook.Api.IntegrationTests/Identity/UsersIsPlatformAdminSchemaTests.cs`:

- `Column_is_platform_admin_exists` — `information_schema.columns` shows `data_type='boolean'`, `is_nullable='NO'`.
- `Default_value_is_false` — assert `column_default = 'false'`.
- `Partial_index_exists` — `pg_indexes` shows `ix_users_is_platform_admin` with `WHERE (is_platform_admin = true)`.

**Min implementation**:

1. `User.IsPlatformAdmin` aggregate property + `GrantPlatformAdmin(actorId)` / `RevokePlatformAdmin(actorId)` methods.
2. `UserPlatformAdminGranted` / `UserPlatformAdminRevoked` events in `UserEvents.cs`.
3. `UserConfiguration.cs` — `b.Property(u => u.IsPlatformAdmin).HasColumnName("is_platform_admin").HasDefaultValue(false);`.
4. Migration `OpsM8a_Users_IsPlatformAdmin` — ADD COLUMN + partial INDEX.

**Refactor**: none.

**§3 cross-reference**: §3.1 (D1).

### Step 2 — `ICurrentUser.IsPlatformAdmin` + `HttpCurrentUser` impl + middleware (S, ~2h) — Wave 2

**Tests (red first)** — three test classes:

- `tests/VrBook.Api.IntegrationTests/Identity/IsPlatformAdminClaimWiringTests.cs`:
  - `Authenticated_user_with_db_column_true_resolves_IsPlatformAdmin_true_via_currentUser`.
  - `Authenticated_user_with_db_column_false_resolves_IsPlatformAdmin_false_even_when_entra_role_claim_present_DB_wins`.
  - `Authenticated_user_with_db_column_true_AND_no_entra_role_claim_resolves_true_AND_stamps_role_claim_for_Authorize_filter`.
  - `Anonymous_caller_resolves_false`.
- `tests/VrBook.Modules.Identity.UnitTests/Auth/HttpCurrentUserIsPlatformAdminTests.cs`:
  - `IsPlatformAdmin_returns_HttpContext_Items_value_when_present_bool`.
  - `IsPlatformAdmin_falls_back_to_HasRole_PlatformAdmin_when_item_absent` (the no-middleware edge case).
  - `IsPlatformAdmin_returns_false_when_no_context`.
- `tests/VrBook.Architecture.Tests/ICurrentUserShapeTests.cs` (extend):
  - `ICurrentUser_exposes_bool_IsPlatformAdmin_property`.

**Min implementation**:

1. `ICurrentUser.cs` — add `bool IsPlatformAdmin { get; }`.
2. `HttpCurrentUser.cs` — `IsPlatformAdminItemKey` const + `PlatformAdminRoleClaim` const + property impl per §3.2.
3. `UserProvisioningMiddleware.cs` — after the `tenant_memberships` block, read `is_platform_admin`, stamp `HttpContext.Items`, conditionally add `ClaimTypes.Role = "PlatformAdmin"` claim.
4. `AnonymousCurrentUser` (the migrator/stub) — implement `IsPlatformAdmin => false`. Find via `Grep 'AnonymousCurrentUser'`.

**Refactor**: none.

**§3 cross-reference**: §3.1 (D1), §3.2 (D2).

### Step 3 — `TenantAuthorizationBehavior.IsPlatformAdmin` swap + `AuditLogBehavior.ResolveRole` extension (XS, ~30min) — Wave 2

**Tests (red first)**:

- `tests/VrBook.Modules.Identity.UnitTests/Application/TenantAuthorizationBehaviorPlatformAdminBypassTests.cs`:
  - `PlatformAdmin_caller_bypasses_cross_tenant_ITenantScoped_check`.
  - `PlatformAdmin_caller_logs_information_line_for_bypass`.
  - `Non_PlatformAdmin_caller_with_mismatched_tenant_still_throws_CrossTenantAccessException`.
  - `PlatformAdmin_bypass_does_NOT_apply_to_IBackgroundCommand` (the M.6 short-circuit runs first).
- `tests/VrBook.Modules.Identity.UnitTests/Application/AuditLogBehaviorPlatformAdminRoleTests.cs`:
  - `ResolveRole_returns_platform_admin_when_currentUser_IsPlatformAdmin_true`.
  - `ResolveRole_returns_platform_admin_even_when_also_IsAdmin_true` (PlatformAdmin takes precedence).
  - `ResolveRole_returns_admin_when_IsAdmin_true_and_not_PlatformAdmin`.

**Min implementation**:

1. `TenantAuthorizationBehavior.cs:85-89` — replace `return false;` with `return user.IsPlatformAdmin;`.
2. `AuditLogBehavior.cs:83-101` — insert `if (u.IsPlatformAdmin) return "platform_admin";` as the first non-anonymous branch.

**Refactor**: update the XML doc on `IsPlatformAdmin` to reflect the new shape.

**§3 cross-reference**: §3.3 (D3), §3.6 (D6).

### Step 4 — Suspend/Reactivate commands + handlers (M, ~3h) — Wave 3

**Tests (red first)** — three test classes:

- `tests/VrBook.Modules.Identity.UnitTests/Application/SuspendTenantCommandTests.cs`:
  - `Command_implements_IAuditable_with_action_tenant_suspend`.
  - `Command_does_NOT_implement_ITenantScoped`.
  - `Validator_rejects_empty_Reason`.
  - `Validator_rejects_whitespace_only_Reason`.
  - `Validator_accepts_valid_Reason`.
- `tests/VrBook.Modules.Identity.UnitTests/Application/SuspendTenantHandlerTests.cs`:
  - `Throws_ForbiddenException_when_currentUser_IsPlatformAdmin_false`.
  - `Throws_NotFoundException_when_tenant_row_missing`.
  - `Calls_Tenant_Suspend_with_command_reason_and_actor_userid`.
  - `Logs_Information_line_with_target_tenant_actor_user_reason`.
- `tests/VrBook.Modules.Identity.UnitTests/Application/ReactivateTenantHandlerTests.cs`:
  - `Throws_ForbiddenException_when_not_PlatformAdmin`.
  - `Throws_NotFoundException_when_tenant_missing`.
  - `Calls_Tenant_Reactivate`.

**Min implementation**:

1. New file `src/Modules/VrBook.Modules.Identity/Application/Platform/Commands/SuspendTenantCommand.cs` containing both records + both handlers + both validators.
2. Register the FluentValidation validators in the module (assembly scan already handles this — verified `IdentityModule.cs:46`).

**Refactor**: extract a private `PlatformAdminGuard.Require(currentUser)` helper if the same `if (!currentUser.IsPlatformAdmin) throw` block recurs across handlers (it does — three places). One static method, ~3 lines.

**§3 cross-reference**: §3.5 (D5).

### Step 5 — `SetTenantPlatformFeeBpsCommand` controller move + defense-in-depth (XS, ~30min) — Wave 3

**Tests (red first)** — `tests/VrBook.Api.IntegrationTests/Identity/Platform/SetPlatformFeeEndpointTests.cs`:

- `Endpoint_returns_401_for_anonymous`.
- `Endpoint_returns_403_for_authenticated_non_PlatformAdmin` (Owner persona).
- `Endpoint_returns_204_for_PlatformAdmin_targeting_any_tenant`.
- `Old_owner_route_returns_404_after_M8_removes_it` — assert `PUT /api/v1/admin/tenants/{tenantId}/platform-fee` returns 404 (the action is deleted).
- `Audit_row_action_is_tenant_set_platform_fee` — assert audit_log captures the action name.

**Min implementation**:

1. Add `IAuditable` to `SetTenantPlatformFeeBpsCommand` with `AuditAction = "tenant.set_platform_fee"`, `AuditTargetType = "Tenant"`, `AuditTargetId = TenantId.ToString("D")`.
2. Add defense-in-depth `if (!currentUser.IsPlatformAdmin) throw new ForbiddenException(…)` to the existing `SetTenantPlatformFeeBpsHandler` (verified `StripeOnboardingCommands.cs:94-105`). Inject `ICurrentUser`.
3. Delete `[HttpPut("platform-fee")]` action on `TenantsAdminController.cs:70-81` + its `SetPlatformFeeRequest` record (it moves to the new controller's file).

**Refactor**: none.

**§3 cross-reference**: §3.5 (D5), §1.4 row 1.

### Step 6 — `IPlatformTenantStatsLookup` cross-module read (S, ~1.5h) — Wave 3

**Tests (red first)** — `tests/VrBook.Api.IntegrationTests/Booking/PlatformTenantStatsLookupTests.cs`:

- `Returns_zeros_for_tenant_with_no_bookings`.
- `RecentBookingCount_filters_to_last_30_days`.
- `LifetimeGrossRevenue_sums_captured_amounts_only`.
- `Scoped_to_tenant_id_correctly`.
- `LastActivityAt_is_max_of_booking_and_payment_updated_at`.

**Min implementation**:

1. New file `src/VrBook.Contracts/Interfaces/IPlatformTenantStatsLookup.cs` (the contract).
2. New file `src/Modules/VrBook.Modules.Booking/Infrastructure/PlatformTenantStatsLookup.cs` (the SQL impl).
3. Register in `BookingModule.cs` — `services.AddScoped<IPlatformTenantStatsLookup, PlatformTenantStatsLookup>();`.

**Refactor**: none.

**§3 cross-reference**: §3.11 (D11).

### Step 7 — Read endpoints + DTOs + handlers (M, ~3h) — Wave 3

**Tests (red first)** — three test classes:

- `tests/VrBook.Architecture.Tests/PlatformTenantDtoShapeTests.cs` — reflection assertions on the two DTOs' shapes (per §3.4 D4 record shape — sealed records, fields in documented order, `OnboardingProgressDto` carried).
- `tests/VrBook.Modules.Identity.UnitTests/Application/Platform/Queries/ListPlatformTenantsHandlerTests.cs`:
  - `Throws_ForbiddenException_when_not_PlatformAdmin`.
  - `Pages_correctly_at_default_pageSize_25`.
  - `Filters_by_status_when_provided`.
  - `Searches_by_slug_prefix_case_insensitive`.
  - `Searches_by_displayName_prefix_case_insensitive`.
  - `Sorts_by_created_at_desc_with_id_tiebreaker`.
  - `Caps_pageSize_at_100`.
  - `Returns_total_independent_of_page`.
- `tests/VrBook.Modules.Identity.UnitTests/Application/Platform/Queries/GetPlatformTenantHandlerTests.cs`:
  - `Throws_ForbiddenException_when_not_PlatformAdmin`.
  - `Throws_NotFoundException_when_tenant_id_unknown`.
  - `Returns_PlatformTenantDto_with_all_fields_populated`.
  - `Onboarding_field_uses_OnboardingProgress_helper_from_M7`.
  - `RecentBookingCount_comes_from_IPlatformTenantStatsLookup`.
- `tests/VrBook.Api.IntegrationTests/Identity/Platform/ListPlatformTenantsEndpointTests.cs`:
  - `Endpoint_returns_401_for_anonymous`.
  - `Endpoint_returns_403_for_Owner_persona` (not PlatformAdmin).
  - `Endpoint_returns_200_with_paged_response_for_PlatformAdmin`.
  - `Status_filter_query_param_narrows_response`.
- `tests/VrBook.Api.IntegrationTests/Identity/Platform/GetPlatformTenantEndpointTests.cs`:
  - 401 / 403 / 200 / 404 facts.

**Min implementation**:

1. Extend `src/VrBook.Contracts/Dtos/Identity.cs` with `PlatformTenantListItemDto`, `PlatformTenantDto`, `PlatformTenantListResponse` per §3.4.
2. New files `src/Modules/VrBook.Modules.Identity/Application/Platform/Queries/ListPlatformTenantsQuery.cs` + handler.
3. New files `…/Queries/GetPlatformTenantQuery.cs` + handler.
4. New file `src/VrBook.Api/Controllers/TenantsPlatformController.cs` per §4.2.

**Refactor**: extract a `PlatformTenantMapper` helper if the list handler's per-row materialization duplicates the detail handler's. Probably yes.

**§3 cross-reference**: §3.4 (D4), §3.10 (D10), §3.11 (D11), §3.12 (D12).

### Step 8 — `UserDto.IsPlatformAdmin` field bump (XS, ~30min) — Wave 2

**Tests (red first)**:

- `tests/VrBook.Architecture.Tests/UserDtoShapeTests.cs` (new or extend):
  - `UserDto_has_IsPlatformAdmin_property_of_type_bool_in_documented_position`.
- `tests/VrBook.Modules.Identity.UnitTests/Application/Users/Queries/GetMeHandlerIsPlatformAdminTests.cs`:
  - `Returns_IsPlatformAdmin_true_when_users_row_is_platform_admin_true`.
  - `Returns_IsPlatformAdmin_false_when_users_row_is_platform_admin_false`.
- `tests/VrBook.Api.IntegrationTests/Identity/MeEndpointIsPlatformAdminTests.cs`:
  - `GET_me_returns_isPlatformAdmin_true_for_DevAuth_Admin_persona_post_seed`.

**Min implementation**:

1. Extend `UserDto` per §3.10.
2. Edit `GetMeHandler` (find via `Grep 'GetMeQuery'`) — source the new field from the User aggregate.

**Refactor**: none.

**§3 cross-reference**: §3.10 (D10).

### Step 9 — DevAuth Admin persona seed migration (XS, ~30min) — Wave 2

**Tests (red first)** — `tests/VrBook.Api.IntegrationTests/Auth/DevAuthAdminPersonaPlatformAdminTests.cs`:

- `DevAuth_Admin_persona_users_row_has_is_platform_admin_true_after_seed`.
- `DevAuth_Owner_persona_users_row_has_is_platform_admin_false`.
- `DevAuth_Guest_persona_has_no_users_row_or_is_platform_admin_false`.

**Min implementation**:

1. Migration `OpsM8b_DevAuth_Admin_PlatformAdmin` — `UPDATE identity.users SET is_platform_admin = true WHERE b2c_object_id = '<DevAuth Admin OID>';` (the OID is hard-coded in `DevAuthPersonas` — verified `DevAuthHandler.cs:67-73`).
2. (Optional) Extend the DevAuth Admin's claims in `DevAuthHandler.cs:124-127` to also add a `ClaimTypes.Role = "PlatformAdmin"` claim. The middleware will stamp the same claim from the DB, so this is belt-and-braces but doesn't hurt.

**Refactor**: none.

**§3 cross-reference**: §1.4 row 5, §1.1 item 12.

### Step 10 — Web API client + hooks + pages + sidebar nav group (M, ~4h) — Wave 3

**Tests (red first)** — five test files:

- `web/src/lib/api/platform.test.ts`:
  - `listPlatformTenants_calls_GET_admin_platform_tenants_with_query_params`.
  - `getPlatformTenant_calls_GET_admin_platform_tenants_id`.
  - `suspendTenant_calls_POST_with_reason_body`.
  - `reactivateTenant_calls_POST`.
  - `setPlatformFee_calls_PUT_with_bps_body`.
- `web/src/hooks/usePlatformTenants.test.tsx`:
  - `Default_returns_isLoading_initially`.
  - `Refetches_on_status_filter_change`.
  - `Refetches_on_search_term_change_debounced_300ms`.
- `web/src/app/admin/platform/tenants/page.test.tsx`:
  - `Renders_table_with_tenant_rows`.
  - `Renders_status_filter_dropdown`.
  - `Renders_search_input`.
  - `Renders_pagination_controls`.
  - `Row_click_navigates_to_detail`.
  - `Renders_access_denied_when_api_returns_403`.
- `web/src/app/admin/platform/tenants/[tenantId]/page.test.tsx`:
  - `Renders_tenant_detail_card_with_all_fields`.
  - `Suspend_button_opens_modal_with_reason_input`.
  - `Confirm_in_modal_calls_suspendTenant_with_reason`.
  - `Reactivate_button_only_visible_when_status_is_Suspended`.
  - `Set_fee_form_calls_setPlatformFee_on_submit`.
  - `Renders_404_surface_when_tenant_not_found`.
- `web/src/components/layout/AdminSidebar.test.tsx`:
  - `Renders_Platform_group_when_useMe_isPlatformAdmin_true`.
  - `Does_NOT_render_Platform_group_when_useMe_isPlatformAdmin_false`.
  - `Hides_Continue_setup_when_tenant_status_is_Suspended` (D9 narrow scope).
  - `Renders_Tenant_suspended_banner_when_status_is_Suspended`.

**Min implementation**:

1. `web/src/lib/api/platform.ts` — five exported functions wrapping `apiFetch`.
2. `web/src/hooks/usePlatformTenants.ts` (react-query) + `usePlatformTenant(tenantId)`.
3. `web/src/hooks/useMe.ts` (if not already present) returning `UserDto` shape.
4. `web/src/app/admin/platform/tenants/page.tsx` (list page).
5. `web/src/app/admin/platform/tenants/[tenantId]/page.tsx` (detail page).
6. `web/src/components/platform/SuspendTenantModal.tsx` (modal with reason input).
7. `web/src/components/platform/SetPlatformFeeForm.tsx` (inline form).
8. Edit `web/src/components/layout/AdminSidebar.tsx`:
   - Read `useMe()` → conditionally render the Platform nav group.
   - Adjust `showContinueSetup` to exclude Suspended (D9).
   - Add the suspended-tenant banner.

**Refactor**: extract a `<PlatformTable>` primitive if the list page's table HTML duplicates patterns from existing admin tables (`web/src/app/admin/bookings/page.tsx` etc.).

**§3 cross-reference**: §3.7 (D7), §3.9 (D9), §3.10 (D10).

### Step 11 — `vrbook-admin promote` Powershell cmdlet + runbook (S, ~1.5h)

**Tests (red first)**:

- The cmdlet itself is not unit-tested (Pester is not in-tree); the runbook documents the manual verification:
  - Run `vrbook-admin list --env dev` against a fresh dev DB → empty list.
  - Run `vrbook-admin promote --env dev --email niroshanaks@gmail.com` → returns the row.
  - Run `vrbook-admin list --env dev` → shows one row.
  - Run `vrbook-admin demote --env dev --email niroshanaks@gmail.com` → returns the row updated.
  - Run `vrbook-admin list --env dev` → empty again.

**Min implementation**:

1. `tools/ops/vrbook-admin.ps1` — the cmdlet skeleton per §3.8.
2. `infra/scripts/_common.ps1` — add `Invoke-Postgres` helper (if not already present; check by reading the file).
3. `docs/runbooks/platform-admin-seed.md` — full runbook with three-named-humans policy + invocation examples + revocation procedure.

**Refactor**: none.

**§3 cross-reference**: §3.8 (D8).

### Step 12 — Arch test `PlatformAdminEndpointRoleGateTests` (S, ~1h)

**Tests (red first)** — `tests/VrBook.Architecture.Tests/PlatformAdminEndpointRoleGateTests.cs`:

- `Every_action_on_TenantsPlatformController_has_Authorize_Roles_PlatformAdmin` — reflects on the controller's action methods, asserts each has either a class-level or method-level `[Authorize(Roles="PlatformAdmin")]` attribute.
- `Controller_route_starts_with_api_v1_admin_platform` — pin the route prefix.
- `Every_handler_under_Identity_Application_Platform_namespace_starts_with_currentUser_IsPlatformAdmin_check` — reflect on handler classes; assert the first IL operation of `Handle` references `IsPlatformAdmin`. This is the defense-in-depth pin. (Implementation: use Mono.Cecil or a regex-on-source approach if Cecil is overkill.)
- `No_Authorize_Roles_Owner_Admin_attributes_on_TenantsPlatformController` — confirm the controller does NOT regress to the legacy role mix.

**Min implementation**: the test file itself (no production change beyond Step 5/7).

**Refactor**: none.

**§3 cross-reference**: §8 (this is the §8 arch test).

### Step 13 — Integration test sweep for the platform endpoints (S, ~1.5h)

**Tests (red first)** — `tests/VrBook.Api.IntegrationTests/Identity/Platform/PlatformEndpointAuthMatrixTests.cs`:

A single `[Theory]` per endpoint × persona combination:

| Endpoint | Anonymous | Owner | Admin (no PlatformAdmin) | Admin (post-seed PlatformAdmin) |
|---|---|---|---|---|
| `GET /admin/platform/tenants` | 401 | 403 | 403 | 200 |
| `GET /admin/platform/tenants/{id}` | 401 | 403 | 403 | 200 |
| `POST .../{id}/suspend` | 401 | 403 | 403 | 204 |
| `POST .../{id}/reactivate` | 401 | 403 | 403 | 204 |
| `PUT .../{id}/platform-fee` | 401 | 403 | 403 | 204 |

5 endpoints × 4 personas = 20 facts. Each is one `[Theory]` row with `InlineData`. The fixture seeds the DevAuth Admin persona's `is_platform_admin = true` from Step 9; the "Admin (no PlatformAdmin)" case requires a second seeded admin user with the role flipped off (or uses the Owner persona, since Owner is non-PlatformAdmin by design).

**Min implementation**: the test class + a small fixture extension to switch personas mid-test.

**Refactor**: none.

**§3 cross-reference**: §7, §8.

---

## 6. Hot-path validation

Which paths in OPS.M.4 / M.5 / M.6 / M.7 change behavior because of M.8?

### 6.1 `TenantAuthorizationBehavior` — every `ITenantScoped` write

**Before M.8**: PlatformAdmin bypass returns `false`; behavior never short-circuits via the bypass. Every `ITenantScoped` command is gated by `currentUser.TenantId == scoped.TenantId`. Cross-tenant writes throw `CrossTenantAccessException`.

**After M.8**: PlatformAdmin bypass returns `user.IsPlatformAdmin`. For a PlatformAdmin caller, the cross-tenant equality check is short-circuited. The pre-existing structured-log line (`TenantAuthorizationBehavior.cs:61-64`) is emitted on bypass.

**Hot-path impact**: every existing `ITenantScoped` command (per `TenantScopedCommandTests` enumeration — verified `TenantScopedCommandTests.cs:35-45`) is now writeable by a PlatformAdmin against ANY tenant id. That's the intended semantics. The M.5 `SetTenantPlatformFeeBpsCommand` is the documented user-visible example; every other `ITenantScoped` command is also now PlatformAdmin-reachable cross-tenant in principle.

**Audit pin**: every `ITenantScoped` write by a PlatformAdmin lands in `audit_log` with `actor_role = "platform_admin"` (per §3.6 D6 extension). A "show me every PlatformAdmin action" query is `SELECT * FROM identity.audit_log WHERE actor_role = 'platform_admin'`. That's the after-the-fact audit lens.

**Negative-path pin**: a non-PlatformAdmin Owner still cannot write cross-tenant. The behavior's logic in `TenantAuthorizationBehavior.cs:67-73` is unchanged. The arch test `TenantScopedCommandTests` continues to pass.

### 6.2 OPS.M.5 `SetTenantPlatformFeeBpsCommand` — fully lit up

**Before M.8**: dormant. The endpoint at `TenantsAdminController.cs:70-81` was reachable by Owners/Admins of the caller's own tenant; in that case `currentUser.TenantId == scoped.TenantId` and the behavior allowed it. Owners could set their own platform fee — a documented staging stub per OPS.M.5 §11 deviation row "TenantsAdminController.SetPlatformFee ships dormant".

**After M.8**:
- The Owner-side endpoint is **deleted**.
- The PlatformAdmin-side endpoint at `TenantsPlatformController.SetPlatformFee` is the only path.
- The command (still `ITenantScoped`) gets `IAuditable` annotation; the bypass behavior fires for PlatformAdmin callers; the `actor_role` is `"platform_admin"`.

**Hot-path impact**: a tenant Owner trying to set their own fee gets 404 (the old action is gone) or 403 (if they try the new PlatformAdmin route without the role). Documented in §11 close-out as a behavior change.

### 6.3 OPS.M.5 `Tenant.UpdateStripeAccountReadiness` state machine

Per §3.9 D9: the state machine is unchanged. The interaction with M.8's explicit `Suspend()` / `Reactivate()`:

- Auto-Suspend on capability loss (`stripe_capability_lost`) → unchanged.
- Operator Suspend (`SuspendTenantCommand`) with arbitrary reason → new path; lands the same `Status = Suspended`, different `SuspendedReason`.
- Operator Reactivate → unchanged; works on either reason.

**Hot-path impact**: zero behavior change in the auto-state-machine. M.8 adds parallel operator-driven paths.

### 6.4 OPS.M.7 `MeTenantDto.Onboarding`

Per §1.4 row 4 + §3.9 D9: the M.7 wizard's `IsComplete` derivation treats Suspended as `IsComplete = false`. M.8 adjusts the AdminSidebar to render a banner instead of the "Continue setup" link when status is Suspended. The wizard's behavior at `/admin/onboarding` itself does NOT change in M.8; the wizard would still render its current surface if a Suspended-tenant owner navigated there directly (URL-typed).

**Hot-path impact**: minor UX adjustment to the AdminSidebar. The wizard route itself is unmodified.

(O3-track: the broader enforcement would make `/admin/onboarding` show a "Tenant suspended" landing page; M.8 doesn't ship that.)

### 6.5 OPS.M.6 background-worker `IBackgroundCommand`

Verified `TenantAuthorizationBehavior.cs:49-52`: `IBackgroundCommand` short-circuits the behavior before the PlatformAdmin check is consulted. **No interaction**. Background commands continue to bypass tenant auth via their own pathway; PlatformAdmin is irrelevant to them.

### 6.6 OPS.M.4 `AuditLogBehavior` exception path

The pre-existing M.4 behavior writes `<action>.failed` audit rows on exception (verified `AuditLogBehavior.cs:46-50`). With M.8:

- A non-PlatformAdmin Owner trying to call `POST .../{id}/suspend` would hit the controller's `[Authorize(Roles="PlatformAdmin")]` filter and get 403 before any MediatR pipeline runs. **No audit row** (the filter rejects before pipeline entry). Acceptable — the Authentication middleware logs the rejection at the platform level.
- A PlatformAdmin calling `POST .../{id}/suspend` for a tenant that doesn't exist → `NotFoundException` from the handler → `tenant.suspend.failed` audit row with the command JSON. Useful audit trail.
- A PlatformAdmin calling `POST .../{id}/suspend` for an already-Suspended tenant → `InvalidOperationException` from `Tenant.Suspend` (verified `Tenant.cs:103-106`) → `tenant.suspend.failed` audit row. Useful.

**Hot-path impact**: zero structural change; M.4 audit behavior already covers M.8's needs.

### 6.7 Stripe Connect webhook → `Tenant.UpdateStripeAccountReadiness` ↔ M.8 Suspend

If Stripe's `account.updated` webhook fires for a tenant that the operator just Suspended via M.8, the webhook handler invokes `tenant.UpdateStripeAccountReadiness(true, true)` (suppose the Stripe side is fine). Per the aggregate logic (`Tenant.cs:166-179`), Suspended cannot auto-Active from flags alone, so the Status stays Suspended. **No conflict**.

If the operator-Suspended tenant has Stripe flags drop and the webhook fires `UpdateStripeAccountReadiness(false, false)`, the aggregate (`Tenant.cs:173-178`) checks `Status == StatusActive` before re-Suspending — since status is already Suspended, this branch doesn't fire and no event is raised. The aggregate stays Suspended with the *operator's* reason intact.

**Hot-path impact**: zero — the aggregate's existing branches correctly handle the M.8 + webhook interaction.

### 6.8 The DevAuth Admin persona

**Before M.8**: DevAuth Admin has roles `["Owner", "Admin", "tenant_admin"]` per `IdentityController.cs:92`. No `PlatformAdmin` role. `TenantAuthorizationBehavior.IsPlatformAdmin` returns `false`, so DevAuth Admin cannot cross-tenant-write.

**After M.8 Step 9 seed**: DevAuth Admin's `users.is_platform_admin = true`. Middleware stamps `IsPlatformAdmin = true` + `ClaimTypes.Role = "PlatformAdmin"`. Both the behavior bypass AND the controller `[Authorize(Roles="PlatformAdmin")]` filter pass.

**Hot-path impact**: a staging walkthrough using DevAuth Admin can exercise the M.8 surface immediately after Wave 2 + Step 9 lands. No additional Entra setup needed in staging/dev.

For production: a real Entra user must (a) have an `identity.users` row (auto-provisioned on first login per `UserProvisioningMiddleware`), then (b) get promoted via `vrbook-admin promote --env prod --email <addr>`. The two-step is intentional; the production seed cannot happen until the user has logged in once.

### 6.9 Cross-module reads on the detail page

The `GetPlatformTenantHandler` calls:

- `IdentityDbContext.Tenants` for the aggregate state.
- `IdentityDbContext.TenantMemberships` for the owner email/name/userid (find the `IsPrimary = true` row).
- `IPropertyCountByTenant` (from OPS.M.7) for the property count.
- `IPlatformTenantStatsLookup` (new in M.8) for booking + payment stats.

Total: 1 Identity scope (2 EF reads) + 1 Catalog round-trip (the M.7 lookup) + 1 raw SQL on Booking + Payment. ~4 DB round-trips for the detail page. Acceptable for an operator-facing read (the page is not high-volume).

The list endpoint at default page size of 25 issues: 1 Tenants query + 1 count + 25 Catalog round-trips for PropertyCount (the N+1). The N+1 is acceptable at this scale; if it becomes a problem in operator UX, we can swap to a JOIN-projection or a bulk-count contract `IPropertyCountByTenants(IEnumerable<Guid>)`. Open question O6 (§Appendix B): N+1 acceptable?

**Hot-path impact**: small; operator-facing only. No customer-facing endpoint is impacted.

---

## 7. Cross-tenant safety review

This section is the most important in this slice because we are *explicitly granting* cross-tenant access.

### 7.1 The bypass surface map

Every endpoint M.8 ships, and every layer that must agree the caller is a PlatformAdmin:

| Endpoint | Filter layer | Handler layer | Pipeline layer |
|---|---|---|---|
| `GET /admin/platform/tenants` | `[Authorize(Roles="PlatformAdmin")]` | `if (!currentUser.IsPlatformAdmin) throw` | N/A (query, no `ITenantScoped`) |
| `GET /admin/platform/tenants/{id}` | Same | Same | N/A |
| `POST .../{id}/suspend` | Same | Same | N/A (command not `ITenantScoped` per D5) |
| `POST .../{id}/reactivate` | Same | Same | N/A |
| `PUT .../{id}/platform-fee` | Same | Same | `TenantAuthorizationBehavior.IsPlatformAdmin` bypass |

**Three layers of defense for every endpoint**: filter, handler, audit. Read endpoints rely on filter + handler + (audit-on-failure). Write endpoints add the audit-on-success row via `IAuditable`.

### 7.2 What if the filter is missing?

A future contributor adds a new platform-admin action to `TenantsPlatformController` but forgets `[Authorize(Roles="PlatformAdmin")]` at the method level. The class-level attribute SHOULD catch it (per §4.2 the attribute IS at class level). But if someone splits the class into a partial and forgets the class attribute, the new method has no filter.

**Mitigations**:

1. **Arch test §8 / Step 12** — `Every_action_on_TenantsPlatformController_has_Authorize_Roles_PlatformAdmin`. Reflects on the controller's actions, asserts each has either method-level OR class-level `[Authorize(Roles="PlatformAdmin")]`. Fails CI if missing.
2. **Handler defense-in-depth** — every handler under `Identity/Application/Platform/` starts with `if (!currentUser.IsPlatformAdmin) throw new ForbiddenException(…)`. The arch test asserts this too (per Step 12).
3. **Audit trail** — even if both the filter and the handler check were somehow missing, an `[IAuditable]` command would still land in `audit_log` with the actor's `actor_user_id`. A periodic "show me every audit row for `tenant.suspend` where `actor_role != 'platform_admin'`" query would catch drift. That's the third line.

### 7.3 What if `IsPlatformAdmin` resolves wrong?

The DB column is the source of truth (D1). The middleware stamps `HttpContext.Items` from the column (D2). `HttpCurrentUser.IsPlatformAdmin` reads the item; falls back to the role claim if absent.

**Risk scenario**: a misconfigured middleware skips the stamp; the fallback reads `roles="PlatformAdmin"` from the Entra token. If a non-PlatformAdmin user has the role claim (e.g. via a misclick in Entra portal), `IsPlatformAdmin` resolves true → cross-tenant bypass.

**Mitigations**:

1. **The middleware ALWAYS runs in production** (per OPS.M.2 register chain). The fallback path is reached only when middleware is bypassed, which we control.
2. **The seed mechanism is ops-Powershell only (D8)**. Entra App Role assignment alone does NOT grant PlatformAdmin in production (because the DB column is false). The Entra grant is the soft path; the cmdlet is what makes it real.
3. **Integration test `Authenticated_user_with_db_column_false_resolves_IsPlatformAdmin_false_even_when_entra_role_claim_present_DB_wins`** pins this exact scenario. Drift fails the test.

### 7.4 What about RLS (Slice OPS.M.9)?

M.9 will add `app.is_platform_admin = true` connection-level bypass for RLS policies. M.8's app-level gate is the only barrier until M.9 lands. Once M.9 ships:

- The detail endpoint's `IdentityDbContext.Tenants` query runs under a connection-tagged `app.is_platform_admin = true` flag (because the caller is a PlatformAdmin); RLS policy allows cross-tenant rows. The query continues to work.
- A non-PlatformAdmin who somehow reaches the handler (filter + handler-check both bypassed — theoretical) would be running under their normal `app.tenant_id = <their tenant>` connection; RLS would scope the query to their tenant only, hiding cross-tenant rows. Defense-in-depth at the data layer.

**Forward-link to M.9**: the connection-factory contract `IRlsBypassDbContextFactory<TContext>` lands in M.9. M.8's handlers use the *normal* DbContext (no bypass); M.9 introduces the per-caller toggle. The M.8 handlers may need a small edit in M.9 to use the bypass factory; the contract is stable.

### 7.5 The audit trail is the third line

If a PlatformAdmin is compromised (Entra account takeover, token leak), the immediate damage is cross-tenant writes via M.8 endpoints. The audit_log captures every action with `actor_user_id`, `target_id`, `before_json`, `after_json`, `ip_address`, `user_agent`, `trace_id`. Forensic answer "what did the compromised account do?" is one SQL query.

**Pin**: every PlatformAdmin command MUST be `IAuditable`. Step 12's arch test asserts this. Drift fails CI.

### 7.6 Negative-path table

| Scenario | Expected response |
|---|---|
| Anonymous caller hits any `/admin/platform/*` endpoint | 401 Unauthorized, no audit row, no log line beyond auth middleware's standard rejection |
| Owner persona hits `GET /admin/platform/tenants` | 403 Forbidden from the `[Authorize]` filter; no audit row |
| Admin persona (not PlatformAdmin) hits `POST .../suspend` | 403 Forbidden from the `[Authorize]` filter; no audit row |
| PlatformAdmin hits `POST .../suspend` with empty Reason | 400 Bad Request from validator; `tenant.suspend.failed` audit row (validation runs BEFORE audit per pipeline order — verified `IdentityModule.cs:48-53`; **correction**: the audit behavior is AFTER validation, so a validator-failed command does NOT reach audit. The failure is logged by the validation behavior). Confirmed by re-reading the pipeline order. |
| PlatformAdmin hits `POST .../suspend` for unknown tenant id | 404 Not Found; `tenant.suspend.failed` audit row with the command JSON in `before_json` |
| PlatformAdmin hits `POST .../suspend` for already-Suspended tenant | 422 (or 500 depending on the existing `InvalidOperationException` ProblemDetails mapping); audit row `tenant.suspend.failed` |
| PlatformAdmin hits `POST .../suspend` happy path | 204 No Content; `tenant.suspend` audit row; `TenantSuspended` outbox event; sidebar banner appears on next page load for that tenant's Owner |
| PlatformAdmin's session token expires mid-action | 401 from the JwtBearer middleware; no audit row |
| PlatformAdmin tries to suspend their own tenant | 204 — allowed. M.8 does NOT block a PlatformAdmin from operating on their own tenant via the cross-tenant endpoints (Open question O7 in §Appendix B; the brief noted both stances are defensible). |

### 7.7 The CRITICAL anti-pattern to forbid

PlatformAdmin endpoints stamp `target_tenant_id` from the **route segment** — this is the ONLY place in the codebase where we trust the URL for a tenant id, because the call is explicitly cross-tenant and paired with the PlatformAdmin gate. Every other URL-trusted-tenant-id surface in the codebase is currently safe because the `TenantsAdminController` does NOT actually trust the URL value (it discards with `_ = tenantId` and uses `currentUser.TenantId` — verified `TenantsAdminController.cs:37, :50, :63, :76`). M.8 BREAKS that invariant *for the platform controller only*; the §8 arch test pins the invariant break to exactly that controller and no other.

**Pin**: a future controller routed under `/api/v1/admin/platform/*` MUST follow the same shape — controller-level `[Authorize(Roles="PlatformAdmin")]` + handler defense-in-depth + tenant-id-from-URL is the convention. The arch test enforces it.

**Decision: triple-defense pattern (filter + handler + audit). M.8's behavior bypass lit-up. The §8 arch test pins the role gate on every action. The audit trail is the after-the-fact forensic surface.**

---

## 8. Architecture / integration tests

### 8.1 The M.8 arch test pack

**`PlatformAdminEndpointRoleGateTests`** (Step 12 — the load-bearing arch test):

```csharp
public sealed class PlatformAdminEndpointRoleGateTests
{
    private static Type ControllerType
        => typeof(VrBook.Api.Controllers.TenantsPlatformController);

    [Fact]
    public void Controller_has_class_level_Authorize_PlatformAdmin()
    {
        var attr = ControllerType.GetCustomAttribute<AuthorizeAttribute>();
        attr.Should().NotBeNull();
        attr!.Roles.Should().Be("PlatformAdmin");
    }

    [Fact]
    public void Controller_route_is_api_v1_admin_platform_tenants()
    {
        var route = ControllerType.GetCustomAttribute<RouteAttribute>();
        route.Should().NotBeNull();
        route!.Template.Should().Be("api/v1/admin/platform/tenants");
    }

    [Fact]
    public void Every_action_method_either_has_Authorize_PlatformAdmin_or_inherits_class_level()
    {
        var actions = ControllerType.GetMethods()
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());
        foreach (var action in actions)
        {
            var methodAttr = action.GetCustomAttribute<AuthorizeAttribute>();
            var classAttr = ControllerType.GetCustomAttribute<AuthorizeAttribute>();
            (methodAttr ?? classAttr).Should().NotBeNull(
                $"{action.Name} has no Authorize attribute at method or class level.");
            ((methodAttr ?? classAttr)!.Roles ?? "").Should().Contain("PlatformAdmin",
                $"{action.Name} must require PlatformAdmin role.");
        }
    }

    [Fact]
    public void No_action_allows_Owner_or_Admin_roles_alone()
    {
        var actions = ControllerType.GetMethods()
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any());
        foreach (var action in actions)
        {
            var methodAttr = action.GetCustomAttribute<AuthorizeAttribute>();
            var roles = methodAttr?.Roles ?? ControllerType.GetCustomAttribute<AuthorizeAttribute>()?.Roles ?? "";
            roles.Split(',').Should().NotContain("Owner");
            roles.Split(',').Should().NotContain("Admin");
        }
    }

    [Fact]
    public void Every_platform_handler_does_defense_in_depth_check()
    {
        // Find every handler under Identity.Application.Platform.{Commands,Queries}
        var platformHandlers = typeof(IdentityModule).Assembly
            .GetTypes()
            .Where(t => t.Namespace?.Contains("Identity.Application.Platform") == true)
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)));

        foreach (var handler in platformHandlers)
        {
            // Source-level check: read the .cs file (relative path heuristic) or use Mono.Cecil
            // to assert the Handle method's IL references "IsPlatformAdmin" in the first few ops.
            //
            // Alternative simpler approach: use ReflectionEmit-like decompilation via dotPeek API,
            // OR (pragmatic) require every handler to inherit from a `PlatformAdminHandlerBase`
            // that does the check in a base class.
            //
            // For M.8 we ship the simple form: assert the handler's constructor takes ICurrentUser
            // (the only way to do the check). The runtime check is then a code-review item; integration
            // tests in Step 13 enforce the behavior.
            handler.GetConstructors().Single().GetParameters()
                .Should().Contain(p => p.ParameterType == typeof(ICurrentUser),
                    $"{handler.Name} must inject ICurrentUser to do the IsPlatformAdmin defense-in-depth check.");
        }
    }

    [Fact]
    public void Every_platform_write_command_is_IAuditable()
    {
        var platformWriteCommands = typeof(IdentityModule).Assembly
            .GetTypes()
            .Where(t => t.Namespace?.Contains("Identity.Application.Platform.Commands") == true)
            .Where(t => t.GetInterfaces().Any(i =>
                i == typeof(IRequest) ||
                (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))));

        foreach (var cmd in platformWriteCommands)
        {
            typeof(IAuditable).IsAssignableFrom(cmd).Should().BeTrue(
                $"{cmd.Name} must implement IAuditable so PlatformAdmin actions land in audit_log.");
        }
    }
}
```

That's 5 facts in one arch-test class. Strong pin.

### 8.2 Integration test pack (Step 13)

Per Step 13's matrix: 5 endpoints × 4 personas = 20 integration facts. Each is `[Theory] [InlineData]` row.

### 8.3 Other tests pinned by M.8

The existing arch tests continue to enforce their original invariants:

- **`TenantScopedCommandTests`** (OPS.M.4) — `SetTenantPlatformFeeBpsCommand` keeps `ITenantScoped`; `SuspendTenantCommand`/`ReactivateTenantCommand` do NOT (per D5). No change to the test; new commands aren't in the marker set.
- **`MeTenantQueryShapeTests`** (OPS.M.7) — `GetMyTenantQuery` continues to NOT implement `ITenantScoped`. M.8 doesn't touch.
- **`MeTenantDtoShapeTests`** (OPS.M.7) — `MeTenantDto` shape unchanged. M.8 introduces new DTOs (`PlatformTenantDto`, `PlatformTenantListItemDto`) with their own shape test (Step 7).

### 8.4 Future OPS.M.10 isolation test

M.10's cross-tenant isolation test pack will include:

- A two-tenant scenario where Owner A tries to hit `/admin/platform/tenants/<tenant-B>` → 403 (not PlatformAdmin).
- A two-tenant scenario where a PlatformAdmin successfully reads tenant A AND tenant B.
- The audit trail confirms the PlatformAdmin's cross-tenant read is logged (success on the platform endpoints; no log row on the M.8 GET endpoints since they don't `IAuditable` — open question O8 in §Appendix B: should reads also be audited?).

Forward-link: M.10's test pack will spec these scenarios per the M.10 plan.

---

## 9. Implementation guard rails (best practices)

Every M.8 PR must satisfy these. Arch tests enforce items marked **[arch]**; code review enforces the rest.

1. **PlatformAdmin endpoints stamp `target_tenant_id` from the route segment** — the only place we trust the URL for a tenant id; paired with the role gate. The §8 arch test pins the controller's route prefix and role attribute. **[arch — Step 12]**

2. **No PlatformAdmin endpoint may write to its own tenant via the bypass; if a PlatformAdmin is editing their own tenant they should go through the normal Owner endpoints to preserve normal audit semantics — OR the opposite stance, documented here.** Verdict for M.8: **a PlatformAdmin CAN write to their own tenant via the platform endpoints**. Reasoning: the only consequence is the audit row's `actor_role` is `"platform_admin"` instead of `"owner"`; both are accurate; the operator's intent is clearer when the platform-endpoint pathway is used. The alternative (force a PlatformAdmin to use the Owner pathway for their own tenant) adds runtime branching for zero security value. Open question O7 (§Appendix B): user can flip the stance if they prefer. **[code review]**

3. **Structured logging on every PlatformAdmin action**: every handler emits a Serilog `Information`-level line with `actor_platform_admin_user_id`, `target_tenant_id`, `action`, `reason` (where applicable). The format is documented in §3.5's `SuspendTenantHandler` example. **[code review]**

4. **No PlatformAdmin endpoint may delete a tenant** — `Tenant.Close` (verified `Tenant.cs:124-132`) is unreachable from M.8; deletion is a Phase 2 surface (tenant lifecycle policy hasn't been ratified for self-serve close). The M.8 endpoint inventory in §4 confirms. **[code review]**

5. **Validation runs before audit** — pipeline order is `Validation → TenantAuthorization → AuditLog → handler` (verified `IdentityModule.cs:48-53`). A validator-failed command does NOT land in audit_log. M.8 does not regress this order. **[code review]**

6. **Every PlatformAdmin command implements `IAuditable`** — pinned by the §8 arch test (Step 12 — `Every_platform_write_command_is_IAuditable`). **[arch — Step 12]**

7. **Every PlatformAdmin handler injects `ICurrentUser`** — for the defense-in-depth check. Pinned by Step 12. **[arch — Step 12]**

8. **Defense-in-depth check is the FIRST statement in `Handle`** — code review item; the §8 arch test's stronger form is deferred (Mono.Cecil IL inspection is heavy; the integration test pack in Step 13 covers the negative path via E2E facts). **[code review + Step 13 integration]**

9. **The `Reason` field on `SuspendTenantCommand` is non-empty after trim** — FluentValidation enforces. Step 4's tests pin both shapes (empty + whitespace-only reject). **[validator]**

10. **`actor_id` always comes from `currentUser.UserId`, never from request body** — per §3.5 D5. Code review pin. **[code review]**

11. **The bypass behavior log line stays at `Information` level** — verified `TenantAuthorizationBehavior.cs:61-64`. Operator-investigation runbooks rely on this being grep-able. **[code review]**

12. **PlatformAdmin promotion/demotion bypasses the MediatR pipeline** — by design (D8). The cmdlet's SQL UPDATE does NOT emit `UserPlatformAdminGranted` events (the events exist for a future API path; the cmdlet path doesn't raise them). Runbook documents the trade-off. **[runbook]**

### Arch tests summary

- `PlatformAdminEndpointRoleGateTests` (Step 12) — 5 facts.
- `UsersIsPlatformAdminSchemaTests` (Step 1) — 3 facts.
- `IsPlatformAdminClaimWiringTests` (Step 2) — 4 facts.
- `HttpCurrentUserIsPlatformAdminTests` (Step 2) — 3 facts.
- `ICurrentUserShapeTests` extension (Step 2) — 1 fact.
- `TenantAuthorizationBehaviorPlatformAdminBypassTests` (Step 3) — 4 facts.
- `AuditLogBehaviorPlatformAdminRoleTests` (Step 3) — 3 facts.
- `SuspendTenantCommandTests` (Step 4) — 5 facts.
- `SuspendTenantHandlerTests` (Step 4) — 4 facts.
- `ReactivateTenantHandlerTests` (Step 4) — 3 facts.
- `SetPlatformFeeEndpointTests` (Step 5) — 5 facts.
- `PlatformTenantStatsLookupTests` (Step 6) — 5 facts.
- `PlatformTenantDtoShapeTests` (Step 7) — 2 facts.
- `ListPlatformTenantsHandlerTests` (Step 7) — 8 facts.
- `GetPlatformTenantHandlerTests` (Step 7) — 5 facts.
- `ListPlatformTenantsEndpointTests` (Step 7) — 4 facts.
- `GetPlatformTenantEndpointTests` (Step 7) — 4 facts.
- `UserDtoShapeTests` extension (Step 8) — 1 fact.
- `GetMeHandlerIsPlatformAdminTests` (Step 8) — 2 facts.
- `MeEndpointIsPlatformAdminTests` (Step 8) — 1 fact.
- `DevAuthAdminPersonaPlatformAdminTests` (Step 9) — 3 facts.
- `PlatformEndpointAuthMatrixTests` (Step 13) — 20 facts.
- Web Vitest tests (Step 10) — ~25 facts across 5 files.

**Total: ~120 facts across ~22 test classes/files.** Larger than M.7 (~78) and smaller than M.5 (~150) — fitting for an auth-heavy slice.

---

## 10. (Reserved — no removed sections)

This rev does not promote any deferred decision, so no rev-summary block is needed. All decisions in §3 are locked at first authoring (with open questions O1-O8 in §Appendix B carved out as next-slice candidates).

---

## 11. Close-out — TBD

### Per-step commit ledger

| Step | Wave | Module(s) | Red commit | Green commit | Files touched |
|---|---|---|---|---|---|
| 1 | 1 | Identity | _pending_ | _pending_ | `User.cs`, `UserConfiguration.cs`, `UserEvents.cs`, `Migrations/2026MMDD_OpsM8a_Users_IsPlatformAdmin.cs`, `UsersIsPlatformAdminSchemaTests` (3 facts) |
| 2 + 3 + 8 | 2 | Contracts + Identity | _pending_ | _pending_ | `ICurrentUser.cs`, `HttpCurrentUser.cs`, `UserProvisioningMiddleware.cs`, `TenantAuthorizationBehavior.cs`, `AuditLogBehavior.cs`, `UserDto` extension, `GetMeHandler` edit, 7 test classes (~22 facts) |
| 9 | 2 | Identity | _pending_ | _pending_ | `Migrations/2026MMDD_OpsM8b_DevAuth_Admin_PlatformAdmin.cs`, `DevAuthHandler.cs` (optional claim addition), 1 test class (3 facts) |
| 4 + 5 + 6 + 7 | 3 | Contracts + Identity + Booking + Api | _pending_ | _pending_ | `SuspendTenantCommand.cs` + handlers + validators, `StripeOnboardingCommands.cs` edit, `IPlatformTenantStatsLookup.cs`, `PlatformTenantStatsLookup.cs`, `BookingModule.cs` (DI), `Identity.cs` (DTO bumps), `ListPlatformTenantsQuery.cs` + handler, `GetPlatformTenantQuery.cs` + handler, `TenantsPlatformController.cs`, `TenantsAdminController.cs` (delete platform-fee action), 8 test classes (~40 facts) |
| 10 | 3 | Web | _pending_ | _pending_ | `web/src/lib/api/platform.ts`, `web/src/hooks/usePlatformTenants.ts`, `web/src/hooks/useMe.ts`, `web/src/app/admin/platform/tenants/page.tsx`, `web/src/app/admin/platform/tenants/[tenantId]/page.tsx`, `web/src/components/platform/SuspendTenantModal.tsx`, `web/src/components/platform/SetPlatformFeeForm.tsx`, `web/src/components/layout/AdminSidebar.tsx` edit, 5 test files (~25 facts) |
| 11 | 3 | Tools | _pending_ | _pending_ | `tools/ops/vrbook-admin.ps1`, `infra/scripts/_common.ps1` (Invoke-Postgres helper), `docs/runbooks/platform-admin-seed.md` |
| 12 | 3 | Architecture tests | _pending_ | _pending_ | `PlatformAdminEndpointRoleGateTests.cs` (5 facts) |
| 13 | 3 | Integration tests | _pending_ | _pending_ | `PlatformEndpointAuthMatrixTests.cs` (20 facts) |

**Target test posture**: server `Category=Unit` baseline + ~90 new facts; arch tests +5; integration tests +20. Web vitest +25.

### Deviations from this plan

_(populated post-ship — currently empty)_

### Forward links

- **Slice OPS.M.9 — RLS policies + `IRlsBypassDbContextFactory<TContext>`**: M.9 will add `app.is_platform_admin = true` connection-level bypass for cross-tenant reads from the RLS policies. M.8's handlers may need a small edit in M.9 to use the bypass factory (the `IdentityDbContext.Tenants` read in `ListPlatformTenantsHandler` and `GetPlatformTenantHandler` is the call site). The bypass-factory contract is stable; M.8 ships nothing that conflicts.
- **Slice OPS.M.10 — Cross-tenant isolation test pack**: M.10 will sweep every M.8 endpoint with a two-tenant scenario. The M.8 §8 arch test plus the Step 13 integration matrix cover most of the surface; M.10 adds the holistic "Owner of A cannot reach tenant B's data via any endpoint" assertion, AND "PlatformAdmin can reach both", AND "the audit trail confirms both attempts".
- **Slice OPS.M.8.1 — Tenant Suspended enforcement (1 day)**: per §3.9 D9 / O3 default verdict (o3b). Extends `PlaceBookingHandler`, `CreatePropertyHandler`, `OnboardTenantStripeHandler`, `GenerateStripeAccountLinkHandler` to check `Tenant.Status == Suspended` and throw the appropriate `BusinessRuleViolationException`. New cross-module contract `ITenantStatusLookup` (or piggy-back on `ITenantStripeContextLookup`).
- **Phase 2 — Impersonation (per MULTI_TENANCY_OPS_PLAN §9)**: time-boxed claim swap; the PlatformAdmin clicks "Act as Tenant X"; API issues a 30-minute token with `app_tenant_id=X` + `impersonated_by=<super_admin_user_id>` claim. The audit trail's `impersonated_by` field answers "who actually did this". Carved out as O1; if the user promotes into M.8 it re-estimates to 6 days.
- **Phase 2 — `organizations` table + per-Org PlatformAdmin scoping**: the Phase 2 multi-org concept (per the brief — "Phase 2 Org concept (one PlatformAdmin per Org) is later"). PlatformAdmin would become a per-Org flag rather than platform-wide. The DB column on `identity.users` would survive (additive: `users.platform_admin_org_id` if non-null restricts the bypass scope); the `TenantAuthorizationBehavior.IsPlatformAdmin` check would consult both the flag and the Org membership.
- **Phase 2 — MFA enforcement at Entra Conditional Access policy level**: a Conditional Access policy on the `PlatformAdmin` Entra App Role requiring MFA. Documented in `docs/runbooks/platform-admin-seed.md` as a follow-up.
- **Slice OPS.6 — `/admin/platform/*` IP allowlist at Container App ingress level**: infrastructure change (Bicep); not in M.8 scope.
- **Slice 4 — Notifications for tenant suspend/reactivate**: once Slice 4 ships the ACS pipeline, a `tenant.suspended` template ACS email lands in the primary support inbox for the affected tenant. The trigger event `TenantSuspended` is already raised by `Tenant.Suspend` (verified `TenantEvents.cs:7`); Slice 4 adds a handler subscription. No M.8 code change.

---

## Appendix A — Verified codebase claims

Every concrete file/class name in §3-§5 is grounded in one of these. If any line drifts, the plan's *contract claim* is the contract — adjust the file path, not the contract.

| Claim | Source |
|---|---|
| `TenantAuthorizationBehavior.IsPlatformAdmin` returns `false` with a "Slice OPS.M.8 will replace this body" comment | `src/Modules/VrBook.Modules.Identity/Application/Behaviors/TenantAuthorizationBehavior.cs:78-89` |
| `TenantAuthorizationBehavior` pipeline order: validation → tenant-auth → audit | `IdentityModule.cs:48-53` + behavior class XML doc |
| `IBackgroundCommand` short-circuits the behavior before the PlatformAdmin check | `TenantAuthorizationBehavior.cs:49-52` |
| `TenantAuthorizationBehavior` logs `"PlatformAdmin bypass for {RequestType} on tenant {TenantId}"` at Information | `TenantAuthorizationBehavior.cs:61-64` |
| `ICurrentUser` exposes `UserId`, `B2CObjectId`, `Email`, `IsAuthenticated`, `IsOwner`, `IsAdmin`, `TenantId`, `HasRole`, `HasTenantRole` | `src/VrBook.Contracts/Interfaces/ICurrentUser.cs:7-40` |
| `User` aggregate has `IsOwner`, `IsAdmin`, `EmailVerified` flags + `GrantOwner`/`RevokeOwner`/`GrantAdmin`/`RevokeAdmin` methods | `src/Modules/VrBook.Modules.Identity/Domain/User.cs:19-21, 106-109` |
| User does NOT have `IsPlatformAdmin` today | `User.cs:11-121` (grep returns no match) |
| `HttpCurrentUser` reads claims from `accessor.HttpContext.User`; `AppUserIdItemKey = "VrBook:UserId"`; `TenantIdClaimType = "app_tenant_id"` | `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs:17-72` |
| `HttpCurrentUser.IsOwner` reads both `extension_isOwner` claim AND `ClaimTypes.Role = Owner`; same shape for `IsAdmin` | `HttpCurrentUser.cs:62-63` |
| `HttpCurrentUser.HasRole` reads both `IsInRole` and `ClaimTypes.Role`/`roles` claim | `HttpCurrentUser.cs:93-104` |
| `UserProvisioningMiddleware` reads `tenant_memberships` after `ProvisionUserCommand` and stamps `ClaimTypes.Role` + `app_tenant_id` claims | `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs:64-84` |
| `UserConfiguration.cs` defines columns `b2c_object_id`, `email`, `display_name`, `phone`, `is_owner`, `is_admin`, `email_verified`, `last_login_at`, audit columns | `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/UserConfiguration.cs:14-47` |
| `SetTenantPlatformFeeBpsCommand` exists at `Identity/Application/Tenants/Commands/`; ships dormant; implements `ITenantScoped` | `src/Modules/VrBook.Modules.Identity/Application/Tenants/Commands/StripeOnboardingCommands.cs:28-29, 94-105` |
| `TenantsAdminController.SetPlatformFee` is `[HttpPut("platform-fee")]` under `[Authorize(Roles="Owner,Admin")]` | `src/VrBook.Api/Controllers/TenantsAdminController.cs:25, 70-81` |
| `TenantsAdminController` never trusts the URL `tenantId`; uses `CallerTenantId()` | `TenantsAdminController.cs:28-29, :37, :50, :63, :76` |
| `Tenant` aggregate has `Suspend(reason, actorId)` (Active → Suspended), `Reactivate()` (Suspended → Active), `Activate()` (PendingOnboarding → Active), `Close()` | `src/Modules/VrBook.Modules.Identity/Domain/Tenant.cs:88-132` |
| `Tenant.Suspend` raises `TenantSuspended(TenantId, Reason, ActorId)`; `Tenant.Reactivate` raises `TenantActivated(TenantId)` | `Tenant.cs:109, :121` + `TenantEvents.cs:5, :7` |
| `Tenant.UpdateStripeAccountReadiness` auto-Activates on both-flags-true + PendingOnboarding; auto-Suspends with reason `stripe_capability_lost` on Active + flag loss | `Tenant.cs:161-179` |
| `Tenant.SetPlatformFeeBps` validates 0..10_000; does NOT raise an event | `Tenant.cs:181-189` |
| `TenantSuspended`, `TenantActivated`, `TenantClosed`, `TenantCreated` events exist | `src/VrBook.Contracts/Events/TenantEvents.cs:3-9` |
| `TenantStripeOnboarded`, `TenantStripeSuspended` events exist | `TenantEvents.cs:26-29` |
| `AuditLogBehavior` writes `<action>.failed` on exception; reads `actor_role` via `ResolveRole(currentUser)` returning "anonymous"/"admin"/"owner"/"guest" | `src/Modules/VrBook.Modules.Identity/Application/Behaviors/AuditLogBehavior.cs:46-50, 83-101` |
| `IAuditable` interface requires `AuditAction`, optional `AuditTargetType` + `AuditTargetId` | `src/Modules/VrBook.Modules.Identity/Application/Behaviors/IAuditable.cs:8-18` |
| `AuditLogEntry.Record` accepts nullable `tenantId` and stamps it (M.8 actor's, not target's) | `src/Modules/VrBook.Modules.Identity/Domain/AuditLogEntry.cs:34-59` |
| `MeTenantDto` shape carries `Id, Slug, DisplayName, Status, DefaultCurrency, PlatformFeeBps, StripeAccountStatus, ChargesEnabled, PayoutsEnabled, HasStripeAccount, PropertyCount, Onboarding` | `src/VrBook.Contracts/Dtos/Identity.cs:27-39` |
| `OnboardingProgressDto` shape: `IsComplete, NextStep` | `Identity.cs:46-48` |
| `OnboardingProgress.DeriveNextStep` + `DeriveIsComplete` static helpers in Identity module | `src/Modules/VrBook.Modules.Identity/Application/Tenants/Common/OnboardingProgress.cs:23-41` |
| `GetMyTenantQuery` does NOT implement `ITenantScoped`; handler derives tenant id from `ICurrentUser.TenantId` | `src/Modules/VrBook.Modules.Identity/Application/Tenants/Queries/GetMyTenantQuery.cs:11-18, :27-31` |
| `IdentityController.cs` has `GET /api/v1/me` and `GET /api/v1/me/tenant` actions | `src/VrBook.Api/Controllers/IdentityController.cs:21-26, :51-59` |
| DevAuth Admin persona has roles `["Owner", "Admin", "tenant_admin"]` and tenantId `00000000-...-0001` | `IdentityController.cs:84, :92` |
| DevAuth `Admin.Oid` is a stable hard-coded string | `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/DevAuthHandler.cs:67-73` |
| ADR-0014 says global roles → Entra App Roles (`Owner`, `Admin`) on `vrbook-api-<env>` registration | `docs/adr/0014-app-roles-global-db-per-tenant.md:24-33` |
| ADR-0014 specifies DB-wins precedence for per-tenant roles | `docs/adr/0014-app-roles-global-db-per-tenant.md:36-40` + OPS.M.2 confirmation |
| MULTI_TENANCY_OPS_PLAN §9 specifies Super Admin capabilities (list, detail, suspend/reactivate, impersonate, audit-filter) under `/super-admin/*` | `docs/MULTI_TENANCY_OPS_PLAN.md:173-181` |
| MULTI_TENANCY_OPS_PLAN §12 row 2 says "minimum three named super_admin users" | `docs/MULTI_TENANCY_OPS_PLAN.md:226` |
| AdminSidebar is a `'use client'` component reading `useMyTenant()` for the Continue-setup link | `web/src/components/layout/AdminSidebar.tsx:1-91` |
| AdminSidebar's existing items list: Dashboard, Properties, Calendar, Bookings, Pricing, Guests, Messages, Reviews, Reports, Sync, Notifications, Amenities, Settings | `AdminSidebar.tsx:33-47` |
| AdminLayout is the server-component wrapper rendering `<AdminSidebar />` + header + main | `web/src/app/admin/layout.tsx:1-28` |
| `grant-self-admin.ps1` is the existing PowerShell precedent reading Entra tenant from `infra/.state/<env>.json` | `infra/scripts/grant-self-admin.ps1:1-77` |
| `infra/scripts/_common.ps1` exposes `Read-State` | `grant-self-admin.ps1:34, :36` |
| `IPropertyCountByTenant` cross-module contract exists in Contracts | per OPS.M.7 §4.2 |
| `ITenantStripeContextLookup` cross-module contract exists in Contracts | per OPS.M.5 §3.4 |
| `OnboardingReturnUrl` / `OnboardingRefreshUrl` config in StripeOptions | per OPS.M.5 §3.12 |
| OPS.M.5 §3.16 D16 says M.8 will gate `IsPlatformAdmin`; the dormant `SetTenantPlatformFeeBpsCommand` ships in M.5 awaiting M.8 | `docs/OPS_M_5_PLAN.md:163-166` |
| OPS.M.7 §11 forward-link says M.8 reuses `MeTenantDto`/`OnboardingProgressDto` shape with `OwnerEmail` addition | `docs/OPS_M_7_PLAN.md:1063` |

---

## Appendix B — Open questions (8 carved-out)

All M.8 decisions in §3 are locked. The brief explicitly said the user wants Option A (M.4 → M.10 complete before Slice 4), and the M.8 ship is the next-step in that order. These open questions are carve-outs the user may want to promote into M.8 scope (and accept a re-estimate) or defer to follow-up slices.

### O1 — Impersonation ("Act as Tenant X")

MULTI_TENANCY_OPS_PLAN §9 explicitly lists impersonation in the Super Admin capability set. The brief did NOT mention it. M.8 carves it out:

- **Adds 1.5-2 days**: token-issuance endpoint that mints a 30-minute access token with `app_tenant_id = X` + `impersonated_by = <super_admin_user_id>` claims; `UserProvisioningMiddleware` recognizes `impersonated_by` and stamps `actor_impersonated_by` on every audit row; web "Act as" button + banner.
- **Risk**: claim-swap surface is sensitive; testing requires the token-issuance pathway to be airtight; MFA enforcement (deferred §1.2 row 5) is a soft prerequisite.
- **Verdict default**: defer to a follow-up named "Slice OPS.M.8.2 — Super Admin impersonation". Flag to user.

**Promote to M.8 if**: the operator UX clearly needs "act as" before Phase 1.5 demo. Otherwise defer.

### O2 — Audit-log read endpoint + UI

"Show me every PlatformAdmin action against tenant X" is a SQL query today. A UI is ~1 day:

- New endpoint `GET /admin/platform/tenants/{tenantId}/audit-log?page=…`.
- Web page rendering the rows under the tenant detail.

**Verdict default**: defer; the SQL query suffices for the first PlatformAdmin walkthroughs. Promote when the audit-trail UI becomes friction.

### O3 — Tenant Suspended enforcement

Per §3.9 D9, three stances:
- (o3a) Ship full enforcement in M.8; re-estimate to 4.5 days.
- (o3b) Ship narrow scope in M.8; 1-day follow-up Slice OPS.M.8.1.
- (o3c) Hold off entirely; suspend is warning-only.

**Verdict default: o3b.**

### O4 — `Tenant.SetPlatformFeeBps` domain event

Should the fee-change event-source? Today the aggregate method does not raise an event (`Tenant.cs:181-189`). Adding `TenantPlatformFeeBpsChanged(TenantId, OldBps, NewBps, ActorId)` would let Slice 4 (or any future module) react. M.8 does NOT add it because no consumer exists; carve-out for future.

### O5 — `GrantPlatformAdminCommand` + endpoint

D8 picks Powershell-only for the seed mechanism. A future API endpoint (gated to PlatformAdmin) would let a PlatformAdmin promote another via the web UI. Pro: easier onboarding flow. Con: privilege-escalation surface; the bootstrap problem (first PlatformAdmin) still requires the cmdlet.

**Verdict default**: defer; the cmdlet path is sufficient through Phase 1.5 demo.

### O6 — N+1 PropertyCount on the list page

§6.9 documents the N+1 (25 list rows = 25 PropertyCount round-trips). Acceptable at operator-facing volume; if it becomes UX-slow, swap to a bulk-count contract.

**Verdict default**: ship the N+1; profile later.

### O7 — Can a PlatformAdmin write to their own tenant via the platform endpoints?

§9 row 2 picks "yes". Alternative: force them through the Owner endpoints. The user may prefer the stricter stance; flagged for review.

**Verdict default**: yes (allow).

### O8 — Should GET endpoints also be audited?

Currently `GET /admin/platform/tenants` and `GET .../{id}` do NOT `IAuditable` (queries don't implement it). A "PlatformAdmin browsed tenant X's data" audit trail might be desirable for forensic review.

**Verdict default**: defer; the SQL connection identity is sufficient for now. Promote if compliance ratchets up.

---

**Plan ends.**
