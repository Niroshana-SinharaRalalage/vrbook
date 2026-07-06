# Slice OPS.M.15 — App-role legacy claim reads + `[Authorize(Roles="Owner,Admin")]` drop

- **Status:** DRAFT — awaiting reviewer sign-off on §5 questions before execution.
- **Date:** 2026-07-06.
- **Author:** Platform Enterprise Architect (agent) via M.15 planning consult.
- **Predecessors:** [`OPS_M_12_CLOSE_OUT.md`](OPS_M_12_CLOSE_OUT.md), [`OPS_M_14_CLOSE_OUT.md`](OPS_M_14_CLOSE_OUT.md).
- **Deferrers:** M.14 §3 explicitly deferred both the `[Authorize(Roles="Owner,Admin")]` drop and the underlying `IsOwner` / `IsAdmin` accessor cleanup to M.15 to keep them semantically bundled. [`OPS_M_10_2_F11_TOKEN_AUTH_DEEP_DIVE.md`](OPS_M_10_2_F11_TOKEN_AUTH_DEEP_DIVE.md) §Slice-OPS.M.15 sketches the endpoint of this plan; M.15 is the execution slice.
- **Scope:** ONE vertical slice. Retires every pre-ADR-0014 legacy authorization surface: the `extension_isOwner` / `extension_isAdmin` claim reads, the `OwnerClaim` / `AdminClaim` constants, the `IsOwner` / `IsAdmin` accessors on `ICurrentUser` (as they exist today — reshaped, not deleted; see §2.4), and the shape of every `[Authorize(Roles="Owner,Admin")]` decorator on API controllers. `PlatformAdmin` role + `Owner`/`Admin` App Role definitions in Entra STAY — they're the current shape ADR-0014 pinned. `identity.users.is_owner` / `is_admin` DB columns STAY (out of scope; called out in §1.M.15.7 as follow-up).
- **Explicitly NOT in this slice:**
  - Drop of `identity.users.is_owner` / `is_admin` columns + `User.GrantOwner/RevokeOwner/GrantAdmin/RevokeAdmin` domain methods — the DB flags feed `UserDto.IsOwner/IsAdmin` which the SPA still shows on `/api/v1/me`. Deferring to a follow-up (M.15.1 or M.17) after the SPA is refactored to drive nav from `MembershipRoles` instead.
  - Drop of `Owner` + `Admin` App Role definitions from the Entra `vrbook-api-<env>` app registration. **They must stay** — the token's `roles` claim is what the new decorators read.
  - `PlatformAdmin` role writes in `UserProvisioningMiddleware` — that's the current shape ADR-0014 mandates (DB-authoritative, materialized as a claim).
  - `_pre_m13_snap` schema cleanup — 2026-08-04.
  - Housekeeping / M.16 turnover module fanout — orthogonal.

---

## §0 What we're doing + why now

### §0.1 The domain gap

Post-ADR-0014, the codebase carries two parallel authorization mechanisms for the *same* Owner/Admin role decision. `HttpCurrentUser.IsOwner` reads `extension_isOwner` OR `HasRole("Owner")` — the OR is a belt on top of the App Roles braces, retained across M.0 through M.14 as a legacy safety net while every surface migrated to Entra App Roles. Post-M.12/M.14, the belt is provably dead weight: the ONLY producers of `extension_isOwner` claims are (a) the M.14 `TestAuthHandler` (which also writes the correct `ClaimTypes.Role` claim on the same code path — line 90–99) and (b) the SPA's `useAuth.ts:38` reader (which drives no gating server-side). No real Entra token today carries `extension_isOwner`; the ADR-0014 close-out on 2026-06-26 confirmed empirically that CIAM tokens do not emit `extension_*` claims. Every `[Authorize(Roles="Owner,Admin")]` decorator in `src/VrBook.Api/Controllers/*` is already resolving against the token's native `roles` claim from the `vrbook-api-<env>` App Role assignments; the extension-claim path is unreachable in production.

### §0.2 Why now

- M.12 landed the social IdP gate — it reads `IdentityProvider` cleanly through the current-shape accessor and never touches `extension_*`. Any further growth on this surface (M.12.1 Microsoft, promote endpoint) will reuse the same shape; dropping the legacy before the next auth-shape slice ships means every new surface starts from the current shape.
- M.14 landed the mock JwtBearer scheme with a comment (`TwoTenantTestAuthHandler.cs:41-47`) explicitly noting "the role claims are here only to pass the token-level gate. Drops entirely when M.15 replaces those decorators with MembershipRoles-based checks." The scaffolding was left in place as a deliberate M.15 hand-off.
- The current shape has a subtle drift risk: any new controller under `src/VrBook.Api/Controllers/` copy-pasting `[Authorize(Roles="Owner,Admin")]` from a sibling ships a gate that (a) is satisfied by any Entra token carrying the `Owner` OR `Admin` App Role regardless of tenant, and (b) implicitly conflates the two roles. The M.15 replacement is `[Authorize]` + tenant-scoped role check in the handler (via `HasTenantRole` or a `RequireMembershipRoleAttribute` filter — see §2.3). Once M.15 lands, every controller is either PlatformAdmin-gated (`TenantsPlatformController`) or authenticated + handler-tenant-scoped; the "Owner/Admin gate" as a top-level construct is gone.
- Cleaning up now prevents a class of future audit-review findings.

### §0.3 Successful-case walk (post-ship)

- Tenant admin signs in via `AdminSignUpSignIn` Entra flow. Entra emits access token with `roles: ["Admin"]` (from App Role assignment on `vrbook-api-staging`) + `oid` + `email` + `email_verified`. NO `extension_*` claim.
- Request hits `/api/v1/bookings/{id}/confirm`. `UserProvisioningMiddleware` runs → stamps `AppUserId`, `IsPlatformAdmin`, `MembershipRoles`, `ActiveTenantItemKey`, `app_tenant_id` claim, `PlatformAdmin` role claim if applicable. No `extension_*` reads, no `Owner`/`Admin` role synthesis from any legacy source.
- Post-M.15 controller shape: `[Authorize]` on `BookingsController.Confirm` (no `Roles = "Owner,Admin"`). Handler-level guard: `TenantAuthorizationBehavior` rejects on tenant mismatch; `OwnerActionHandler.TransitionAsync` runs. `currentUser.HasTenantRole(booking.TenantId, "tenant_admin")` is the explicit permission check where any is needed — see §2.3.
- 200 OK. Zero behavior change from the caller's perspective.

### §0.4 Adversarial-case walk (regression detection)

- A future PR adds `PublicOrdersController.ExportAll` with copy-pasted `[Authorize(Roles = "Owner")]`. Arch test `OpsM15_NoLegacyOwnerAdminRoleAttributeTests` (§2.5) walks every action in the API assembly and asserts that no `AuthorizeAttribute.Roles` string contains `"Owner"` or `"Admin"` (allowlist: `"PlatformAdmin"`). PR fails CI on the arch test with a specific rule-name reference telling the author to use `[Authorize]` + handler-level `HasTenantRole` OR add to the allowlist with justification. The class of drift the plan closes is exactly this.

---

## §1 Sub-commit sequence

Sub-commit convention: `Slice OPS.M.15.N: <summary>`. All commits target `develop`. Each ends CI-green under `dotnet test --filter "Category!=Integration"` + integration where noted. RED-then-GREEN ordering is practical for M.15.1 (arch test first, red until M.15.2 lands) and M.15.5 (integration test first, red until M.15.3 completes). Sequence:

```
M.15.1 RED   — Arch tests: legacy claim symbols banned + Authorize(Roles=Owner/Admin) banned + IsOwner/IsAdmin caller allowlist.
M.15.2 GREEN — HttpCurrentUser: drop OwnerClaim/AdminClaim constants + ReadBoolClaim, reshape IsOwner/IsAdmin to pure HasRole.
M.15.3 GREEN — Controller migration: drop [Authorize(Roles="Owner,Admin")] on all controllers, keep [Authorize] + PlatformAdmin.
M.15.4 GREEN — Handler-level HasTenantRole guards where owner-equality-based checks now need explicit role-string (§2.3).
M.15.5 GREEN — Retire ICurrentUser.IsOwner / IsAdmin + call-site sweep + TestPersona reshape + TestAuthHandler cleanup.
M.15.6 GREEN — SPA legacy claim reader drop + useAuth.ts + arch test enforcing.
M.15.7 DOCS  — Runbook + ADR-0014 amendment + close-out. Also documents the deferred User/DB-column drop.
```

**7 sub-commits; one doc-only (M.15.7); six code-carrying. Session budget: 2 sessions.**
- Session 1 — M.15.1 (RED) + M.15.2 + M.15.3 + M.15.4 (all-server backend cleanup).
- Session 2 — M.15.5 + M.15.6 (call-site sweep + SPA) + M.15.7 (close-out).

### M.15.1 — RED. Arch tests land first.

**Land first + RED because** these are the load-bearing invariants the rest of the slice restores. Committing them red on `develop` means every subsequent GREEN sub-commit is provably driven by the pinned shape.

Files touched:
- `tests/VrBook.Architecture.Tests/OpsM15_NoLegacyExtensionClaimSymbolsTests.cs` (NEW, 4 facts):
  1. `HttpCurrentUser` source contains no `"extension_isOwner"` / `"extension_isAdmin"` string literal.
  2. `HttpCurrentUser` type has no member named `OwnerClaim` / `AdminClaim` (reflection).
  3. No `src/**/*.cs` file (excluding EF migration Designer / ModelSnapshot files) contains identifier `OwnerClaim` or `AdminClaim`.
  4. `HttpCurrentUser` source contains no `ReadBoolClaim` method identifier (reflection over private methods).
- `tests/VrBook.Architecture.Tests/OpsM15_NoOwnerAdminRoleAttributeTests.cs` (NEW, 3 facts):
  1. Walk every `ControllerBase`-derived type in the API assembly; assert no method or class carries an `AuthorizeAttribute` whose `Roles` property contains `"Owner"` or `"Admin"` (allowlist string set: `{"PlatformAdmin"}`).
  2. Assert every controller action is EITHER `[Authorize]` (no Roles), `[Authorize(Roles="PlatformAdmin")]`, `[AllowAnonymous]`, or the whole controller carries the class-level equivalent.
  3. Assert `TenantsPlatformController` still has `[Authorize(Roles="PlatformAdmin")]` at the class level.
- `tests/VrBook.Architecture.Tests/OpsM15_ICurrentUserRoleShapeTests.cs` (NEW, 2 facts):
  1. `ICurrentUser` contract does NOT expose `IsOwner` / `IsAdmin` boolean members (RED until M.15.5 removes them). Skip-until-M.15.5 with `Skip = "Enabled after M.15.5 lands."`.
  2. `HttpCurrentUser.HasRole(string)` still exists and returns `bool`.

CI: **red** on this commit (intentional). Every subsequent commit flips one or more facts green.

### M.15.2 — GREEN. `HttpCurrentUser` claim-reader drops.

- Drop `OwnerClaim` / `AdminClaim` constants, `ReadBoolClaim` private method.
- Reshape `IsOwner => HasRole("Owner")`, `IsAdmin => HasRole("Admin")`.
- Update class-level XML-doc.

Tests: `HttpCurrentUserRoleReaderTests` (NEW, Unit, 4 facts) — `HasRole` returns true for both `ClaimTypes.Role` and native `roles` shapes; `IsOwner`/`IsAdmin` reflect `HasRole`; anonymous returns false.

CI: green.

### M.15.3 — GREEN. Controller `[Authorize(Roles="Owner,Admin")]` drop.

Drop the role gate from 12 controllers in `src/VrBook.Api/Controllers/` (list per architect analysis): AdminController, BookingsController, IdentityController, NotificationsController, PaymentsController, PricingController, PropertiesController (with §5-Q3 hold on line 233), ReportsController, ReviewsController, SyncController, TenantsAdminController. Keep `TenantsPlatformController` unchanged (`[Authorize(Roles = "PlatformAdmin")]`).

Also `AuthExtensions.cs`: delete unused `AddPolicy("OwnerOrAdmin", ...)` and `AddPolicy("Admin", ...)` — no callers per grep.

XML-doc updates on affected handlers (~7 files).

Tests: `AuthorizeAttributeShapeSmokeTests` (NEW, Integration, 3 facts).

CI: green.

### M.15.4 — GREEN. Handler-level `HasTenantRole` guards.

For sites where the OLD `[Authorize(Roles="Owner,Admin")]` was load-bearing (booking transitions, sync-worker triggers, etc.), add `currentUser.HasTenantRole(tenantId, "tenant_admin")` at the top of the handler. Sites where owner-equality was already in the handler get no new guard.

Tests: `OwnerActionHandlerRoleGateTests` (NEW, Unit, 3 facts).

CI: green.

### M.15.5 — GREEN. Retire `ICurrentUser.IsOwner`/`IsAdmin` + call-site sweep.

- Drop `IsOwner`/`IsAdmin` from `ICurrentUser`, `HttpCurrentUser`, `AnonymousCurrentUser`.
- Migrate 8-10 business-logic call sites to `HasTenantRole(tenantId, "tenant_admin")`.
- Reshape `AuditLogBehavior.ResolveRole` to derive from `MembershipRoles`.
- Reshape `TestPersona` / `TestAuthHandler` / `TwoTenantTestAuthHandler` per §5-Q4.
- Update `UserDto` per §5-Q1.

Fact 1 of `OpsM15_ICurrentUserRoleShapeTests` flips from Skip to Fact.

CI: green.

### M.15.6 — GREEN. SPA legacy claim reader drop.

- `web/src/lib/auth/useAuth.ts` — remove `isOwner`/`isAdmin` fields + `extension_isOwner`/`extension_isAdmin` claim reads.
- `web/src/lib/api/me.ts` — depends on §5-Q1 answer.
- `web/src/components/layout/SiteHeaderNav.tsx` — reshape `isOperator` derivation.
- Web arch test `no-extension-claim-reads.test.ts` (NEW).

CI: green.

### M.15.7 — DOCS. Runbook + ADR-0014 amendment + close-out.

- `docs/adr/0014-app-roles-global-db-per-tenant.md` — Amendment 2026-07 section.
- `docs/identity/roles-architecture.md` — supersede §4.4.
- `docs/MASTER_PLAN.md` — flip M.15 row from ⏭ to ✅.
- `docs/OPS_M_15_CLOSE_OUT.md` (NEW).

No CI (per `feedback_no_ci_for_doc_only_commits`).

---

## §2 Locked answers to design decisions (auth-shape invariants)

### §2.1 App Roles `roles` claim IS the production shape for Owner/Admin

Verified via three signals: (a) `AuthExtensions.cs:104-108` `AddPolicy("OwnerOrAdmin"...)` uses `RequireRole` — broken for `extension_*` claims; (b) `HttpCurrentUser.IsOwner` explicitly bakes in `|| HasRole("Owner")` belt; (c) test personas write the `ClaimTypes.Role` claim, not `extension_*`.

### §2.2 App Roles claim IS emitted on Entra-local admin sign-ins

App Role assignments on `vrbook-api-<env>` are token-shape-invariant regardless of the user flow. M.12 locked `AdminSignUpSignIn` = Local Accounts only, does NOT strip App Role claims.

### §2.3 `[Authorize(Roles="Owner,Admin")]` replacement is `[Authorize]` + `TenantAuthorizationBehavior` + handler-level `HasTenantRole`

Three-layer defence:
- Layer 1 (framework): `[Authorize]` gates authenticated-only.
- Layer 2 (pipeline): `TenantAuthorizationBehavior` rejects cross-tenant.
- Layer 3 (handler): `HasTenantRole(tenantId, "tenant_admin")` where role-string is load-bearing.

### §2.4 `ICurrentUser.IsOwner` / `IsAdmin` — REMOVE outright, not reshape

After M.15.5's call-site sweep, ZERO business-logic sites reference them. Keeping as `HasRole("Owner")` synonyms is a landmine.

### §2.5 `UserProvisioningMiddleware` does NOT synthesize Owner/Admin role claims

Verified `UserProvisioningMiddleware.cs:167-181`: only synthesizes `PlatformAdmin`. Add arch fact to `OpsM15_NoLegacyExtensionClaimSymbolsTests`.

---

## §3 Migration surface

| Sub-commit | Files touched (concrete list) | Category |
|---|---|---|
| M.15.1 | `tests/VrBook.Architecture.Tests/OpsM15_*` (3 NEW files) | Arch tests (RED) |
| M.15.2 | `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs` + `HttpCurrentUserRoleReaderTests.cs` (NEW) | Claim reader |
| M.15.3 | 12 files under `src/VrBook.Api/Controllers/*` + `AuthExtensions.cs` + 8 XML-doc updates + `AuthorizeAttributeShapeSmokeTests.cs` (NEW) | Controllers |
| M.15.4 | `TransitionHandlers.cs` + audit-driven additions (3-5 files) + `OwnerActionHandlerRoleGateTests.cs` (NEW) | Handler guards |
| M.15.5 | `ICurrentUser.cs` + `HttpCurrentUser.cs` + `AnonymousCurrentUser.cs` + 8 handler files + `UserDto.cs` (§5-Q1) + `UserMapping.cs` + `TestPersona.cs` + `TestAuthHandler.cs` + `TwoTenantTestAuthHandler.cs` + `AuditLogBehavior.cs` | Call-site sweep |
| M.15.6 | `web/src/lib/auth/useAuth.ts` + `web/src/lib/api/me.ts` + `web/src/components/layout/SiteHeaderNav.tsx` + arch test (NEW) | SPA |
| M.15.7 | ADR + runbook + MASTER_PLAN + close-out (NEW) | Docs |

**No .env / Bicep changes.** `EntraExternalId:*` keys stay; App Role definitions stay.

---

## §4 Risks + mitigations

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| 1 | A real admin token doesn't actually carry the `roles` claim (App Role misconfigured) — Layer-3 role check rejects → 403 | Low | HIGH | Pre-M.15 smoke walk verifies `/api/v1/me/tenants` returns Memberships with role. |
| 2 | M.15.3 → M.15.4 sequencing gap briefly exposes an admin-only endpoint to intra-tenant guest | Medium | Medium | Same session; `AuthorizeAttributeShapeSmokeTests` catches; split per-controller if unacceptable. |
| 3 | `TestPersona` reshape breaks a test reading `IsOwner`/`IsAdmin` fields | Low | Low | Grep-driven; 3-file blast radius enumerated. |
| 4 | SPA `useMe()` drops `isOwner`/`isAdmin` but a page still branches on them | Medium | Low | 5-file blast radius, all in M.15.6. |
| 5 | Ops audit flags Owner/Admin App Roles as "unused" and deletes them → all admin requests break | Medium over time | HIGH | ADR-0014 amendment locks App Role definitions as CURRENT SHAPE. Runbook warns. |
| 6 | `identity.users.is_owner`/`is_admin` DB columns confuse readers post-M.15 | Medium | Low | M.15.7 documents as deliberate follow-up. |
| 7 | Arch test walker misses a nested/generic controller | Low | Medium | Reuse `EndpointCoverageArchTest` walker pattern. |

---

## §5 Questions the owner should lock BEFORE execution

### §5-Q1: Drop `IsOwner`/`IsAdmin` from `UserDto` wire contract now, OR keep them?

- **A (recommended):** KEEP for M.15; drop in follow-up. Preserves `/api/v1/me` shape, avoids SPA-side ripple in the same slice.
- B: Drop now. Cleaner one-slice cleanup, wider SPA blast radius.

### §5-Q2: For sites where `[Authorize(Roles="Owner,Admin")]` was load-bearing, is `HasTenantRole(tenantId, "tenant_admin")` the right shape, OR introduce a distinct `tenant_owner` token?

- **A (recommended):** Yes, `tenant_admin` (matches M.13.6 `MembershipRoles`).
- B: Introduce `tenant_owner` distinct from `tenant_admin`. Deferred slice; NOT in M.15.

### §5-Q3: `PropertiesController.cs:233` carries `[Authorize(Roles = "Admin")]` — platform-scope or tenant-scope?

Grep the endpoint before deciding. If platform-scope: `[Authorize(Roles = "PlatformAdmin")]`. If tenant-scope: `[Authorize]` + handler `HasTenantRole(tid, "tenant_admin")`.

### §5-Q4: `TestPersona` reshape — drop `IsOwner`/`IsAdmin` in favor of `Roles: IReadOnlyList<string>`, OR keep the shape and just stop emitting `extension_*` claims?

- **A (recommended):** Reshape to `Roles`. Matches production Entra token shape (list of App Role tokens).
- B: Keep the boolean flags.

### §5-Q5: Add an arch-lock policy in `AuthExtensions` referencing `Owner`/`Admin` role literals, to prevent ops from deleting the App Role definitions in Entra?

- A: Yes — pin policy that gates nothing.
- **B (recommended):** No, runbook + ADR amendment is the right home.

### §5-Q6: `HttpCurrentUser.PlatformAdminItemKey` / `PlatformAdminRole` — cleanup or CURRENT SHAPE?

**Answered:** CURRENT SHAPE. These are OPS.M.8 §3.2 pattern — DB-authoritative platform-admin flag surfaced as synthesized role claim. No change needed.

---

## §6 Close-out checklist

1. [ ] Reviewer sign-off on §5 questions (Q1-Q6).
2. [ ] M.15.1 lands red; commit message pins expected ~4 arch-test failures.
3. [ ] M.15.2 lands; RED count drops.
4. [ ] M.15.3 lands.
5. [ ] M.15.4 lands.
6. [ ] M.15.5 lands; arch tests fully green.
7. [ ] M.15.6 lands; SPA arch test green.
8. [ ] M.15.7 lands.
9. [ ] Staging walk: admin sign-in → `/api/v1/me/tenants` returns Memberships with role; owner-side booking transitions work; guest → 403 via handler guard; PlatformAdmin → platform tenants works.
10. [ ] Confirm no App Role definition removed from `vrbook-api-staging`/`vrbook-api-prod`.
11. [ ] Follow-up ticket for DB column drop + `UserDto` field drop (if §5-Q1-A).

---

## §7 Locked answers (2026-07-06)

All §5 questions locked to the architect's recommended option. Owner directive
2026-07-06 (captured in [`feedback_technical_decisions_are_architect_call`](../../.claude/projects/c--Work-BookingApp/memory/feedback_technical_decisions_are_architect_call.md)):
"All these are technical questions and you need to consult system-architect and
find the best recommendation." Per that rule, the architect's recommendations
below are the locked answers:

- **§5-Q1 (`UserDto.IsOwner`/`IsAdmin` drop)** → **A. Keep for M.15, drop in follow-up.**
  Rationale: tighter one-thing-at-a-time scope for M.15; SPA-side ripple lands
  in a smaller follow-up that also drops the underlying DB columns.
- **§5-Q2 (handler role token)** → **A. `HasTenantRole(tid, "tenant_admin")` matching M.13.6.**
  No new role tokens introduced in M.15. If `tenant_owner` is ever needed
  (§5-Q2-B), it lands in a separate promote/invite slice.
- **§5-Q3 (`AdminAmenitiesController.cs:233` scope)** → **PlatformAdmin.**
  Answered by grep: the controller is `AdminAmenitiesController` for the
  shared amenity catalog (not tenant-scoped). Migrates to
  `[Authorize(Roles = "PlatformAdmin")]` in M.15.3.
- **§5-Q4 (`TestPersona` reshape)** → **A. Reshape to `Roles: IReadOnlyList<string>`.**
  Matches production Entra token shape; closes M.14's vestigial-scaffolding
  comment on `TwoTenantTestAuthHandler`.
- **§5-Q5 (arch-lock policy for Owner/Admin role literals)** → **B. No arch-lock policy.**
  Runbook + ADR-0014 amendment (M.15.7) is the correct home for the "do not
  delete these App Role definitions" warning. A policy that gates nothing is
  dead code + a landmine.
- **§5-Q6 (`PlatformAdminItemKey`/`PlatformAdminRole`)** → **CURRENT SHAPE.**
  Pre-answered by the architect. No change in M.15.

Ready to execute M.15.1.
