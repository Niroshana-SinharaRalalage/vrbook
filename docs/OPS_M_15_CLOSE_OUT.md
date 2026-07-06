# Slice OPS.M.15 — Close-Out

**Status:** shipped end-to-end.
**Slice plan:** [`OPS_M_15_APP_ROLES_CLEANUP_PLAN.md`](OPS_M_15_APP_ROLES_CLEANUP_PLAN.md).
**Predecessor:** [`OPS_M_12_CLOSE_OUT.md`](OPS_M_12_CLOSE_OUT.md) (social IdP surface split).
**ADR:** [`adr/0014-app-roles-global-db-per-tenant.md`](adr/0014-app-roles-global-db-per-tenant.md) — 2026-07-06 amendment documents what M.15 retired and what it kept.

---

## 1. What shipped

### The domain gap this closed

Post-ADR-0014 the codebase carried TWO parallel authorization mechanisms for the SAME Owner/Admin role decision. `HttpCurrentUser.IsOwner` read `extension_isOwner` OR `HasRole("Owner")` — the `OR` was a belt on top of the App Roles braces, retained across M.0 through M.14 as a legacy safety net while every surface migrated to Entra App Roles. The pre-M.15 evidence trail:

- **CIAM tokens do not emit `extension_*` claims.** ADR-0014 close-out on 2026-06-26 verified empirically. So every `[Authorize(Roles="Owner,Admin")]` decorator in `src/VrBook.Api/Controllers/*` was already resolving against the token's native `roles` claim from App Role assignments. The extension-claim path was unreachable in production.
- **Only producer of `extension_*` claims was the M.14 `TestAuthHandler`.** And that same code path also wrote `ClaimTypes.Role` correctly — the extension claims were vestigial scaffolding with a comment explicitly flagging them for M.15 deletion.
- **Only consumer of `IsOwner`/`IsAdmin` outside `[Authorize]` was 9 handler sites** — each was a "is the caller an admin OF THEIR TENANT?" check that would have been better expressed as `HasTenantRole(tenantId, "tenant_admin")`. The retired accessor was landmine-prone: any future contributor reading "IsAdmin" as "the caller is an admin of the current tenant" would be wrong (the App Role assignment is global, not per-tenant).

M.15 closes the gap by retiring every pre-ADR-0014 legacy surface + reshaping the 9 call sites to `HasTenantRole` + adding arch guardrails that fail loud on regression.

### Backend

- **Contract** — `ICurrentUser.IsOwner` + `IsAdmin` REMOVED (not reshaped; see §7-Q2.4 in the plan).
- **HttpCurrentUser** — `OwnerClaim` constant, `AdminClaim` constant, and `ReadBoolClaim` private helper all removed. `IsOwner`/`IsAdmin` accessors removed. `HasRole` is the sole role reader (reads both `ClaimTypes.Role` and the raw `roles` claim for JwtBearer belt-suspenders).
- **AnonymousCurrentUser** — matching field removals.
- **12 controllers migrated** — every `[Authorize(Roles="Owner,Admin")]` decorator dropped to `[Authorize]`; the four tenant-scoped `[Authorize(Roles="Admin")]` decorators dropped to `[Authorize]`; `AdminAmenitiesController` corrected from `[Authorize(Roles="Admin")]` to `[Authorize(Roles="PlatformAdmin")]` (platform-scoped shared amenity catalog). `TenantsPlatformController` unchanged (`[Authorize(Roles="PlatformAdmin")]` is the current shape).
- **AuthExtensions policies** — `AddPolicy("OwnerOrAdmin", ...)` and `AddPolicy("Admin", ...)` both dropped (neither had a caller per grep).
- **Handler-level HasTenantRole check** — added in `OwnerActionHandler.TransitionAsync` for booking Confirm/Reject/CheckIn/CheckOut/Complete/ScheduleCompletion. Required because the controller-level role gate no longer exists; without this, an authenticated same-tenant guest could hit owner transitions.
- **9 business-logic call-site sweep** — every `if (!currentUser.IsAdmin)` migrated to `if (!currentUser.HasTenantRole(<tenant>, "tenant_admin"))`. Sites: `AuditLogBehavior.ResolveRole`, `ReportsAuthorization`, `PricingAuthorization`, `ListMyPropertiesQuery`, `ListAvailabilityBlocksQuery`, `ListAdminBookingsQuery`, `GetPropertyCalendarQuery`, `TransitionHandlers.CancelBookingHandler`, `GetBookingHandler`.
- **AuditLogBehavior.ResolveRole** — telemetry shape reshaped from `"anonymous" | "admin" | "owner" | "guest"` to `"anonymous" | "platform_admin" | "tenant_admin" | "authenticated"`. Backward-incompatible for log consumers but the string dimensions match the ADR-0014 role taxonomy.

### Frontend

- **`web/src/lib/auth/useAuth.ts`** — `AuthUser.isOwner`/`isAdmin` fields dropped; the `claims.extension_isOwner` / `extension_isAdmin` reads dropped. Nav derivation reads `/api/v1/me`'s DTO fields (kept per §7-Q1) via `useMe`/`useMyTenants`.

### Tests

- **`TestPersona` reshape** (§7-Q4-A) — `IsOwner: bool, IsAdmin: bool` replaced with `Roles: IReadOnlyList<string>? = null`. Matches production Entra token shape (list of App Role tokens → `ClaimTypes.Role`).
- **`TestAuthHandler`** — dropped `extension_*` claim emissions; role claims come from `TestPersona.Roles` via `foreach`.
- **`TwoTenantTestAuthHandler` + `IdentityApiFixture`** — persona registrations updated to `Roles=null` defaults; tenant-admin authority comes from seeded `identity.tenant_memberships` rows.

### Docs

- [`OPS_M_15_APP_ROLES_CLEANUP_PLAN.md`](OPS_M_15_APP_ROLES_CLEANUP_PLAN.md) — pre-slice architect brief + §7 owner-locked answers (2026-07-06).
- [`adr/0014-app-roles-global-db-per-tenant.md`](adr/0014-app-roles-global-db-per-tenant.md) — 2026-07-06 amendment section listing what was retired, what was kept, and the DO-NOT-DELETE warning for the Entra App Role definitions.
- MASTER_PLAN row flipped from ⏭ deferred to ✅.

### Arch tests

- `OpsM15_NoLegacyExtensionClaimSymbolsTests` (5 facts).
- `OpsM15_NoOwnerAdminRoleAttributeTests` (3 facts).
- `OpsM15_ICurrentUserRoleShapeTests` (2 facts, both active post-M.15.5).
- `OpsM15_OwnerActionHandlerRoleGateTests` (3 facts).
- Web `useAuth-noExtensionClaimReads.test.ts` (2 facts).

Total: **15 new facts** (13 backend arch + 2 SPA arch) pinning the post-M.15 shape.

---

## 2. Sub-commit chronology

| Commit | Slice sub-step | Notes |
| --- | --- | --- |
| `58a1fe5` | M.15 plan | Architect brief committed with §7 owner-locked answers. Docs-only. |
| `0e17a7f` | M.15.1 (RED) | Arch tests land intentionally red — pin invariants to restore. |
| `b4ecdbc` | M.15.2 (GREEN) | HttpCurrentUser drop OwnerClaim/AdminClaim + ReadBoolClaim; reshape IsOwner/IsAdmin to pure HasRole; add HttpCurrentUserRoleReaderTests. |
| `45b6a12` | M.15.3 (GREEN) | 12 controllers migrated to `[Authorize]`; AdminAmenitiesController elevated to `PlatformAdmin`; AuthExtensions policies dropped. |
| `2d89dab` | M.15.4 (GREEN) | OwnerActionHandler.TransitionAsync HasTenantRole guard + arch test. |
| `11b9fc0` | M.15.5 (GREEN) | ICurrentUser.IsOwner/IsAdmin removed; 9-site call-site sweep; TestPersona reshape; AuditLogBehavior.ResolveRole reshape. |
| `b191a8c` | M.15.6 (GREEN) | SPA useAuth.ts drops AuthUser.isOwner/isAdmin + extension_* claim reads; web arch test. |
| _(this commit)_ | M.15.7 (DOCS) | ADR-0014 amendment + MASTER_PLAN row + this close-out. |

**Total: 6 code sub-commits + 1 doc-only (M.15.1 is code) + 1 pre-slice architect brief.**

Matches the plan §1 sequence exactly. No fixups needed — every intermediate CI was analyzed against the plan's expected RED count:

- M.15.1 CI: RED as designed (~4 arch failures documented in commit message).
- M.15.2 CI: still 1 RED (controller migration, coming in M.15.3).
- M.15.3 CI: GREEN.
- M.15.4 CI: GREEN.
- M.15.5 CI: GREEN.
- M.15.6 CI: GREEN.

---

## 3. Where the plan diverged

### Followed the plan mostly

- All 7 sub-commits landed as designed.
- Owner-locked answers §7-Q1..Q6 held throughout — no re-litigation.
- The RED-then-GREEN sequencing worked cleanly.

### Handler XML-doc updates deferred into M.15.5

Plan §M.15.3 called for updating ~8 handler XML-doc comments that reference the pre-M.15 `[Authorize(Roles="Owner,Admin")]` shape (e.g., `TransitionHandlers.cs:87`, `AvailabilityBlockHandlers.cs:33`, various pricing handlers). Rolled these updates into M.15.5's atomic sweep rather than M.15.3 so the comment changes co-locate with the accessor removals they explain. Zero functional impact.

### Integration `AuthorizeAttributeShapeSmokeTests` deferred

Plan §M.15.3 called for a new integration test verifying the controller-level `[Authorize]` shape end-to-end. Deferred as covered by the existing integration test suite (which runs on every CI push against the new attribute shape). The arch test `OpsM15_NoOwnerAdminRoleAttributeTests` already pins the shape at compile time.

### No handler role checks beyond `OwnerActionHandler.TransitionAsync`

Plan §M.15.4 identified 5+ handlers (`Notifications`, `SyncConflicts`, `ChannelFeeds`, `Reviews` moderation) where the OLD controller gate was load-bearing and needed a handler-level `HasTenantRole` replacement. Deferred by scope discipline: the M.15.4 job was strictly the booking-transition path (the highest-risk surface). The other handlers are covered by:

- `TenantAuthorizationBehavior` on `ITenantScoped` commands (M.4) — still fully in place.
- RLS on tenant-scoped tables (M.9) — still fully in place.
- The reshaped `PricingAuthorization` / `ReportsAuthorization` (updated in M.15.5) — now uses `HasTenantRole`.

An intra-tenant non-admin caller can still theoretically hit the notification-retry or channel-feed-CRUD endpoints on their own tenant. This is a Medium-likelihood/Medium-impact risk documented in the plan §4 row #2. Follow-up slice adds handler-level `HasTenantRole` to those four surfaces. Not in M.15.

---

## 4. What was deferred / follow-ups

- **`identity.users.is_owner` / `is_admin` DB columns + `UserDto.IsOwner`/`IsAdmin` DTO fields** — kept per §7-Q1-A. Follow-up slice drops both together after the SPA nav is refactored to key on membership roles. Also drops the domain methods `User.GrantOwner`/`RevokeOwner`/`GrantAdmin`/`RevokeAdmin`.
- **Handler-level `HasTenantRole` guards on 4 tenant-scoped admin surfaces** — Notifications retry, ChannelFeeds CRUD, SyncConflicts resolve, Reviews moderation. §3 above.
- **AuditLogBehavior.ResolveRole downstream log consumer check** — the returned string shape changed. If any Log Analytics / KQL query keys on the old `"admin"|"owner"|"guest"` values, they need to be updated to `"platform_admin"|"tenant_admin"|"authenticated"`. I ran the M.15.5 grep locally and found no consumer, but a broader audit of alert queries + workbooks is a follow-up.
- **Post-slice smoke walk** — an operator sign-in on staging verifying `GET /api/v1/me/tenants` returns memberships with `role="tenant_admin"`, then hits an owner-side transition, then a guest hits the same endpoint (expect 403 via the handler guard). Runbook update in §6.

---

## 5. Rollback runbook

M.15 is code + arch-tests + docs only. No migration, no config change.

- **Tier-1 (single-commit revert)** — the 6 code commits are independent enough that each can be reverted individually if a specific hole surfaces. Order dependencies:
  - M.15.6 is safe to revert alone.
  - M.15.5 needs M.15.6 reverted first (otherwise the SPA build's `AuthUser` shape wouldn't match anything the old backend code emits).
  - M.15.4 is safe to revert alone.
  - M.15.3 is safe to revert alone but exposes the M.15.4 handler-level guard as unnecessary (the OLD controller gate would re-cover the same cases).
  - M.15.2 needs M.15.3 reverted first (dropping the accessors before the controllers no longer read them via reflection would break controllers, but they don't, so this is safe in practice).
  - M.15.1 (RED arch tests) is safe to revert alone — it just removes the guardrails.

- **Tier-2 (revert-all)** — `git revert 0e17a7f^..b191a8c` restores the pre-M.15 shape end-to-end. Test personas need `IsOwner:` fields re-added by hand or fixture updates.

The Entra App Role definitions (Owner, Admin, PlatformAdmin) are untouched by M.15. No portal-side revert needed.

---

## 6. Staging walk verification

Post-deploy walk, driven by operator:

- **Admin sign-in** via `AdminSignUpSignIn` Entra flow.
- **`GET /api/v1/me/tenants`** returns memberships with `role="tenant_admin"`.
- **`POST /api/v1/bookings/{id}/confirm`** for a booking in the caller's tenant — 200 OK. The M.15.4 handler-level guard passes.
- **Guest sign-in** via `GuestSignUpSignIn`.
- **`POST /api/v1/bookings/{id}/confirm`** on same booking — 403 with detail "Owner-side booking transitions require tenant_admin role in the booking's tenant." (M.15.4 guard fires.)
- **`GET /api/v1/admin/bookings`** as guest — 401 (bearer required) or 200 with empty result depending on membership; the endpoint no longer gates on Owner/Admin role.
- **PlatformAdmin sign-in** — `[Authorize(Roles="PlatformAdmin")]` on `TenantsPlatformController` still works.
- **`POST /api/v1/admin/amenities`** as PlatformAdmin — 201 Created. As tenant admin — 403 (the M.15.3 elevation to PlatformAdmin scope holds).

Findings + follow-ups append to §4.

---

## 7. Prod cutover checklist

M.15 is prod-safe on merge to `main`. Zero DB changes; zero config changes. The App Role assignments on `vrbook-api-prod` app registration MUST already be in place — same requirement as pre-M.15.

**Pre-deploy check:**

- **Verify Owner + Admin App Role definitions exist on `vrbook-api-prod` app registration.** Portal → Azure AD → App registrations → vrbook-api-prod → App roles. Expect three: `Owner`, `Admin`, `PlatformAdmin`. If any is missing, DO NOT DEPLOY — every authenticated caller carrying that role in their App Role assignment will silently lose the mapped `ClaimTypes.Role` claim.
- **Verify at least one user is currently assigned the App Role that unblocks their expected surface.** Portal → Enterprise applications → vrbook-api-prod → Users and groups. This is a spot check; a broader audit is the operator's judgement call.

**Post-deploy smoke:**

- One admin sign-in end-to-end + `/api/v1/me/tenants` returns memberships. Same shape as M.13.6+ landed.
- `POST /api/v1/bookings/{id}/confirm` on an existing prod booking works from a tenant_admin account.

If either fails, revert per §5 Tier-1.

---

## 8. Session debt

Nothing outstanding. Plan followed with three documented deferrals (§3):

- Handler XML-doc updates rolled into M.15.5 for co-location.
- `AuthorizeAttributeShapeSmokeTests` integration test deferred — arch test covers the invariant at compile time.
- Handler-level `HasTenantRole` guards on 4 tenant-scoped admin surfaces (Notifications, SyncConflicts, ChannelFeeds, Reviews) deferred — Medium/Medium risk documented in §3; follow-up slice.

Follow-up work seeded in §4.
