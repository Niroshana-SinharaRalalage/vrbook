# OPS.M.2 — TenantId claim wiring + ICurrentUser shape update (Plan)

**Status**: Proposed — awaiting user review.
**Author**: Plan agent (architect) consult, 2026-06-26.
**MASTER_PLAN reference**: `docs/MASTER_PLAN.md` §2 row OPS.M.2.
**MULTI_TENANCY reference**: `docs/MULTI_TENANCY_OPS_PLAN.md` §10 row OPS.M.2 + §2.
**Roles-architecture reference**: `docs/identity/roles-architecture.md` §4.3 (forward-look middleware enrichment) + §6 (tenant scoping).
**ADR**: `docs/adr/0014-app-roles-global-db-per-tenant.md` — formal decision for the dual-axis (App Roles + DB memberships) authorization model.
**Predecessor**: OPS.M.1 closed 2026-06-26 — `Tenant` broadened, `TenantMembership` aggregate landed, `Slice5_Tenant_Membership_Schema` migration shipped, default tenant `00000000-0000-0000-0000-000000000001` seeded. See `docs/OPS_M_1_PLAN.md`.
**Sequence**: After OPS.M.1; before OPS.M.3 (bulk `tenant_id` column rollout) and OPS.M.4 (`TenantAuthorizationBehavior`).
**Estimate**: 1.5 days. Single-engineer; backend-heavy with one small FE update for the DevAuth persona switcher.

This plan is the contract. OPS.M.2 is **middleware + ICurrentUser surface work only** — no new tables; no controller `[Authorize]` rewrites; no MediatR pipeline behavior. The migration shipped in OPS.M.1 already produced the substrate; OPS.M.2 makes the schema actually do something at request time.

---

## 1. Scope summary

OPS.M.2 produces:
1. `UserProvisioningMiddleware` enrichment: after `ProvisionUserCommand` returns the app `userId`, load all live `tenant_memberships` for that user and stamp `ClaimTypes.Role` (e.g. `tenant_admin`) + a single `app_tenant_id` claim (from the `IsPrimary=true` membership, if any) onto the request's `ClaimsPrincipal`.
2. `ICurrentUser` gains `Guid? TenantId` and `bool HasTenantRole(Guid tenantId, string role)`. The existing `IsOwner`/`IsAdmin` properties stay (see §2.1).
3. `HttpCurrentUser` reads `app_tenant_id` and implements the new method by inspecting the principal's claims (no DB hit).
4. `AnonymousCurrentUser` extended with `TenantId => null` / `HasTenantRole(...) => false` so background workers stay safe.
5. DevAuth persona snapshots gain a `TenantId` field; `DevAuthHandler` stamps it on the synthetic principal so DevAuth and Entra paths converge on the same `app_tenant_id` claim shape.
6. `IdentityApiFixture.ResetAsync` extended to also truncate `tenant_memberships` so integration tests are repeatable.
7. Integration tests covering: primary-membership populates `TenantId`; cross-tenant `HasTenantRole` is negative; multi-membership user retains all `tenant_admin` role claims while `app_tenant_id` reflects only the primary; user with zero memberships sees `TenantId == null`; DevAuth Owner persona populates `app_tenant_id = 00000000-...-0001`.
8. Doc footer updates: MULTI_TENANCY row OPS.M.2 → shipped; MASTER_PLAN row OPS.M.2 → ✅; roles-architecture.md §4.3 strikes the "(forward-look)" note.

OPS.M.2 does **not** touch:
- Any module-table `tenant_id` column (OPS.M.3).
- Any controller `[Authorize]` attribute (OPS.M.4).
- The `TenantAuthorizationBehavior` MediatR pipeline behavior (OPS.M.4).
- The `IsOwner`/`IsAdmin` → `IsTenantAdmin`/`IsSuperAdmin` rename (OPS.M.4 — explicitly deferred per `docs/OPS_M_1_PLAN.md` §2.1).
- Tenant-switching UX (OPS.M.7).
- Any membership-grant API (OPS.M.4 + OPS.M.8 — the admin endpoint).
- App Role assignments on Entra (`Owner`/`Admin` stay; OPS.M.0 wired these and they continue working unchanged).

---

## 2. Up-front decisions (ratified)

### 2.1 `IsOwner` / `IsAdmin` rename — **still deferred** to OPS.M.4

Per `docs/OPS_M_1_PLAN.md` §2.1 the rename to `IsTenantAdmin` / `IsSuperAdmin` is bundled with the controller-attribute rewrite. Every existing `[Authorize(Roles="Owner,Admin")]` controller and every handler that reads `currentUser.IsOwner` / `currentUser.IsAdmin` stays untouched. OPS.M.2 is purely additive on the interface.

### 2.2 Role-claim representation — **uniform `ClaimTypes.Role`**

Add `tenant_admin` / `tenant_member` membership roles as additional `ClaimTypes.Role` claims on the principal alongside `Owner` / `Admin`. Rationale: JwtBearer auto-maps Entra App Roles to `ClaimTypes.Role`; DevAuth synthesises the same; `HttpCurrentUser.HasRole` already reads `ClaimTypes.Role`. Adding `tenant_admin` is additive — `IsInRole("Owner")` keeps working, `[Authorize(Roles="Owner,Admin")]` keeps working. Per-tenant scope is recovered via `HasTenantRole(tenantId, role)` which also checks `app_tenant_id`.

`HasTenantRole(tenantId, role)` semantics: returns `true` iff the principal has a `ClaimTypes.Role` claim whose value equals `role` (ordinal-case-insensitive) AND has an `app_tenant_id` claim whose value parses to `tenantId`.

### 2.3 Caching — **none in OPS.M.2**

The membership read is `Where(m => m.UserId == userId && m.DeletedAt == null)`, hot-path-indexed (`ix_tenant_memberships_user`), ~1 row in steady state, ~1ms. Revisit in OPS.M.4 with the `TenantAuthorizationBehavior` measured in aggregate.

### 2.4 Multi-tenant memberships — **all roles stamped, primary only as `app_tenant_id`**

User with memberships `[(A, tenant_admin, primary=true), (B, tenant_admin, primary=false)]`:
- Two `ClaimTypes.Role = tenant_admin` claims (set membership; `IsInRole("tenant_admin")` is true).
- `app_tenant_id = A` only.
- `HasTenantRole(A, "tenant_admin")` = true.
- `HasTenantRole(B, "tenant_admin")` = **false** in OPS.M.2 — user must "switch" to act on B (UX in OPS.M.7).

### 2.5 `app_tenant_id` claim shape — **string-formatted UUID, lowercase**

Claim type: `"app_tenant_id"` (lowercase, prefix `app_` to avoid future Entra claim collisions). Value: `Guid.ToString()` `d` format. `HttpCurrentUser.TenantId` parses with `Guid.TryParse`, returns `null` on absence or parse failure. Matches `docs/MULTI_TENANCY_OPS_PLAN.md` §2's token-shape note.

### 2.6 DevAuth wiring — **new persona snapshot field, not a new cookie**

`DevAuthPersonas.Snapshot` gains `Guid? TenantId`. The existing `vrbook-dev-persona` cookie selects the persona; tenant id is derived from the snapshot. One source of truth per persona — flipping the cookie atomically swaps identity + tenant. All three personas map to the default tenant (`00000000-...-0001`) by default; Guest's `TenantId` stays `null`.

### 2.7 Membership seeding for DevAuth Owner — **claim-only, no DB row**

DevAuth Owner gets `app_tenant_id` stamped directly by `DevAuthHandler` without inserting a `tenant_memberships` row. The middleware enrichment is **additive only**: if `app_tenant_id` already exists on the principal (DevAuth path), it does NOT re-stamp. If `tenant_memberships` returns rows (Entra path with grants in place), it stamps both `role` claims and `app_tenant_id` from primary. Keeps DevAuth a "synthetic identity"; staging Entra cutover uses the real grant path; integration tests cover both.

### 2.8 `ICurrentUser` surface area — **add exactly two members**

`TenantId` + `HasTenantRole`. No other properties added — `EmailVerified` etc. stay reachable via `IUserRepository`.

---

## 3. Contract (locked)

### 3.1 `ICurrentUser`

```csharp
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? B2CObjectId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsOwner { get; }            // stays — rename deferred to OPS.M.4
    bool IsAdmin { get; }            // stays — rename deferred to OPS.M.4

    /// <summary>
    /// The tenant the caller is currently acting as. Read from the
    /// <c>app_tenant_id</c> claim stamped by <see cref="UserProvisioningMiddleware"/>
    /// (Entra path) or <see cref="DevAuthHandler"/> (DevAuth path). Null for guests
    /// and for any caller without a primary tenant membership.
    /// </summary>
    Guid? TenantId { get; }

    bool HasRole(string role);

    /// <summary>
    /// True iff the caller has the given per-tenant role for the given tenant.
    /// Reads <c>ClaimTypes.Role</c> for the role match AND verifies
    /// <c>app_tenant_id</c> equals <paramref name="tenantId"/>. The
    /// <see cref="HasRole"/> alone cannot answer "WHICH tenant" — that's why this
    /// method exists.
    /// </summary>
    bool HasTenantRole(Guid tenantId, string role);
}
```

### 3.2 Claim contract

| Claim type | Value shape | Stamped by | Read by |
|---|---|---|---|
| `app_tenant_id` | lowercase canonical UUID string | `UserProvisioningMiddleware` (Entra path, primary membership's `TenantId`) OR `DevAuthHandler` (DevAuth path, persona snapshot's `TenantId`) | `HttpCurrentUser.TenantId`, `HttpCurrentUser.HasTenantRole` |
| `ClaimTypes.Role` = `"tenant_admin"` | constant | `UserProvisioningMiddleware` per membership (Entra) OR `DevAuthHandler` directly (DevAuth, for Owner/Admin personas) | `HttpCurrentUser.HasTenantRole` and existing `[Authorize(Roles=...)]` machinery |
| `ClaimTypes.Role` = `"tenant_member"` | constant | Same as above (no consumer in OPS.M.2; UI in Phase 2) | Reserved |

`ClaimTypes.Role = "Owner"` and `"Admin"` continue to be stamped exactly as today (Entra App Roles → JwtBearer auto-map, plus DevAuth's existing `claims.Add` block). The new claims are additive; nothing is removed.

### 3.3 `HasTenantRole` algorithm (reference)

```csharp
public bool HasTenantRole(Guid tenantId, string role)
{
    if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(role)) return false;
    var principal = accessor.HttpContext?.User;
    if (principal is null || principal.Identity?.IsAuthenticated != true) return false;
    if (!HasRole(role)) return false;
    var claimValue = principal.FindFirstValue(TenantIdClaimType);
    return Guid.TryParse(claimValue, out var claimTenant) && claimTenant == tenantId;
}
```

`TenantIdClaimType = "app_tenant_id"` defined as a public const on `HttpCurrentUser`.

---

## 4. Step-by-step plan (4 steps; Step 1 is safe in-session)

### Step 1 — `ICurrentUser` contract + `AnonymousCurrentUser` + `HttpCurrentUser` reader (S, ~30m)

**File (edit)**: `src/VrBook.Contracts/Interfaces/ICurrentUser.cs` — add the two new members per §3.1.

**File (edit)**: `src/VrBook.Infrastructure/Common/AnonymousCurrentUser.cs` — `TenantId => null`; `HasTenantRole(...) => false`.

**File (edit)**: `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs` — add `public const string TenantIdClaimType = "app_tenant_id";`; implement `TenantId` getter + `HasTenantRole` method.

**Acceptance**: `dotnet build` green. No behavior change yet (no stamping side). All existing tests continue to pass.

### Step 2 — DevAuth persona-snapshot extension + tenant claim stamping (S, ~45m)

**File (edit)**: `DevAuthHandler.cs` — add `Guid? TenantId` to `DevAuthPersonas.Snapshot`. Owner + Admin snapshots get default-tenant id; Guest stays null. In `HandleAuthenticateAsync`, after existing role claim additions: conditionally add `app_tenant_id` claim if persona has tenant id; add `ClaimTypes.Role = "tenant_admin"` for IsOwner personas.

**File (edit)**: `IdentityController.DevAuthController.Personas` — extend response shape with `tenantId`.

**File (edit, web FE)**: `web/src/lib/api/devAuth.ts` — extend `DevPersonaInfo` with `readonly tenantId: string | null;`. (Visual UI unchanged; field is for future tenant-switcher.)

**Acceptance**: `dotnet build` green; existing `IdentityFlowTests` still pass.

### Step 3 — `UserProvisioningMiddleware` enrichment (M, ~2h)

**File (edit)**: `UserProvisioningMiddleware.cs` — after the existing `ctx.Items[...] = userId;`, add membership read + role/`app_tenant_id` claim stamping. Inject `IdentityDbContext db` as a new `InvokeAsync` parameter. Guard with `alreadyStamped` check so DevAuth's synthetic claim is not double-stamped or trampled. Wrap in the existing try/catch so DB failures don't break sign-in (logs warning, continues without role claims).

**Acceptance**: `dotnet build` green. Existing `IdentityFlowTests` continue to pass — DevAuth Owner persona's `IsOwner` path is unchanged.

### Step 4 — Integration tests + debug endpoint + fixture extension + doc footers (M, ~2h)

**Files**:
- `tests/.../IdentityApiFixture.cs` — extend `ResetAsync` to truncate `tenant_memberships`.
- `tests/.../Identity/TenantClaimWiringTests.cs` (new) — 6 tests per §6 below.
- `src/VrBook.Api/Controllers/IdentityController.cs` — add `GET /api/v1/dev-auth/current-tenant` debug endpoint gated by `DevAuth:AllowAnonymous`.
- `docs/MASTER_PLAN.md`, `docs/MULTI_TENANCY_OPS_PLAN.md`, `docs/identity/roles-architecture.md` — close-out footers.

**Tests (per §6)**:
1. DevAuth Owner stamps default-tenant `TenantId`.
2. DevAuth Guest has null `TenantId`.
3. DB membership round-trip: seed → middleware reads → `HasTenantRole(default, "tenant_admin")` is true.
4. Cross-tenant negative: `HasTenantRole(otherTenant, ...) == false`.
5. No-membership user: `TenantId == null`.
6. Multi-membership: primary determines `TenantId`; non-primary tenant fails `HasTenantRole` (the OPS.M.7 gap surfacing).

**Acceptance**: full test suite green.

---

## 5. Migration concerns (none)

Zero schema changes. The middleware reads from `tenant_memberships` which already exists. Safe to ship in staging without further migration.

**Sharp edge — staging operator note**: Real Entra users in staging won't have `TenantId` populated until OPS.M.8's admin endpoint exists OR an operator runs:

```sql
INSERT INTO identity.tenant_memberships
    ("Id", user_id, tenant_id, role, is_primary,
     created_at, updated_at, row_version)
VALUES
    (gen_random_uuid(),
     (SELECT "Id" FROM identity.users WHERE b2c_object_id = '<entra-oid>'),
     '00000000-0000-0000-0000-000000000001',
     'tenant_admin', true,
     NOW(), NOW(), 0);
```

This is a one-line bridge until OPS.M.8 lands.

---

## 6. Test strategy summary

| Layer | What's covered | Where |
|---|---|---|
| Build | `ICurrentUser` contract extension compiles | `dotnet build` |
| Integration | DevAuth Owner stamps `app_tenant_id` + `tenant_admin` | `TenantClaimWiringTests.cs` |
| Integration | DevAuth Guest = null `TenantId` | same |
| Integration | DB membership round-trip | same |
| Integration | Cross-tenant negative | same |
| Integration | No-membership user | same |
| Integration | Multi-membership: primary wins for `TenantId` | same |
| Regression | Existing `IdentityFlowTests` (8 tests) | unchanged |
| Regression | Existing `TenantSchemaMigrationTests` (7 tests) | unchanged |

**Explicitly NOT tested**: per-handler tenant scoping (OPS.M.4); cross-tenant write isolation (OPS.M.10); tenant-switching UX (OPS.M.7).

---

## 7. Non-goals

| Item | Owner slice |
|---|---|
| `tenant_id` column on module tables | OPS.M.3a |
| Backfill module rows | OPS.M.3b |
| `NOT NULL tenant_id` flip | OPS.M.3c |
| `TenantAuthorizationBehavior` | OPS.M.4 |
| Drop `[Authorize(Roles="Owner,Admin")]` in favor of tenant-aware policies | OPS.M.4 |
| Rename `ICurrentUser.IsOwner`/`IsAdmin` | OPS.M.4 |
| `GrantTenantRoleCommand` + admin endpoint | OPS.M.8 |
| Tenant-switching UX | OPS.M.7 |
| Membership cache | OPS.M.4 (YAGNI today) |
| RLS policy enforcement | OPS.M.9 |
| EF nav property `User.Memberships` | Never in OPS.M.2 (direct query cleaner) |

---

## 8. Out of scope (future phases)

Per `docs/MULTI_TENANCY_OPS_PLAN.md` §11 — Phase 2+ items stay out (self-serve sign-up, per-tenant Entra tenants, subdomain routing, per-tenant ACS, etc.).

---

## 9. Scope-cut order (drop top first if 1.5-day budget bites)

1. Test #6 (multi-membership). Covered by behavior in tests #3+#4. **Recommended cut if long.**
2. Debug endpoint `/api/v1/dev-auth/current-tenant`. Alternative is `ConfigureTestServices` injection. **Not recommended.**
3. `tenant_admin` role-claim from DevAuth — rely solely on DB-seed path. **Not recommended.**
4. Doc footer cleanup. **Recommended cut if long.**
5. Fixture `ResetAsync` truncation. **Not recommended.**

Never falls: Step 1 (contract), Step 3 (middleware enrichment).

---

## 10. Open questions for reviewer

1. **`app_tenant_id` claim name**: lowercase-with-underscores, matching MTOP §2. Confirm.
2. **DevAuth Admin persona**: also gets `tenant_admin` of default tenant per §2.6 (recommended), OR super-admin is tenant-less in dev? Flag preference.
3. **`current-tenant` debug endpoint**: ship gated by `DevAuth:AllowAnonymous` (recommended), OR use `ConfigureTestServices` injection only (no production-shipped endpoint)?
4. **Membership read pattern**: direct `db.Set<TenantMembership>()` query in middleware (recommended), OR `User.Memberships` EF nav with `Include`?
5. **Source-of-truth precedence**: DevAuth's `app_tenant_id` claim wins over later DB memberships per §2.7. Confirm this is correct (or flip in OPS.M.4).
6. **Test isolation**: confirm no concurrency hazard with existing tests sharing `IdentityApiCollection`.

---

## 11. What gets approved by this document

If you approve:
1. Plan commits as `docs/OPS_M_2_PLAN.md`.
2. Step 1 commits (`OPS.M.2 — ICurrentUser.TenantId + HasTenantRole contract`).
3. Step 2 commits (`OPS.M.2 — DevAuth persona TenantId + tenant_admin stamping`).
4. Step 3 commits (`OPS.M.2 — UserProvisioningMiddleware tenant claim enrichment`).
5. Step 4 commits (`OPS.M.2 — Integration tests + debug endpoint + doc footers`).
6. Staging deploy. Verify with `GET /api/v1/dev-auth/current-tenant` (DevAuth Owner cookie): `tenantId == 00000000-...-0001`.

If you reject or want changes: point at the specific §2 decision or the specific Step in §4; I revise and re-submit.
