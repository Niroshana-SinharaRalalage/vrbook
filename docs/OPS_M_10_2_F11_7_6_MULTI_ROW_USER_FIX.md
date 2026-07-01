# OPS.M.10.2 F11.7.6 — Multi-row-per-email fix (Provisioning upsert)

- **Slice**: OPS.M.10.2 F11.7.6
- **Status**: Design (locked, ready for TDD)
- **Author**: system-architect (2026-06-30)
- **Parent**: OPS.M.10.2 F11 staging enablement
- **Preceded by**: F11.7.5.10 (interim widening — `5acb5bb`)

---

## §1 Symptom + verified root cause

### Symptom (walk-3, 2026-06-30)

Owner Confirm / Reject 403s with:

```
Cross-tenant write rejected for ConfirmBookingCommand:
  attempted=…0001  actual=<null>
```

The `<null>` is `ICurrentUser.TenantId`, read by the M.4 gate at
`src/Modules/VrBook.Modules.Identity/Application/Behaviors/TenantAuthorizationBehavior.cs:96`.
The gate falls through to `throw new CrossTenantAccessException(...)` because:

1. `currentUser.IsPlatformAdmin` is `false` for this request (so the
   PA bypass at line 60 doesn't fire), AND
2. `currentUser.TenantId` is `null` (so the equality check at line 96 rejects).

Both facts trace to the SAME `identity.users` row read on this request.

### Root cause (verified against source, 2026-06-30)

The caller's diagnosis is **confirmed**. Chain of evidence:

**Provisioning is oid-only.** `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/ProvisionUserHandler.cs:14`:

```csharp
var existing = await users.GetByB2CObjectIdAsync(cmd.B2CObjectId, cancellationToken);
if (existing is not null) { ... return existing.Id; }
var user = User.Provision(cmd.B2CObjectId, new Email(cmd.Email), ...);
```

No email lookup. If oid is new, `User.Provision` runs and inserts a brand-new
row with `Id = Guid.NewGuid()`. The middleware
(`src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs:59`)
then stamps THAT row's id onto `HttpContext.Items` as `AppUserId`. Membership
and PA lookups at lines 68–76 target that id.

**Email has no uniqueness constraint.**
`src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/UserConfiguration.cs:22-26`:

```csharp
// Slice 4 polish: relaxed to a non-unique index. DevAuth personas can
// share an inbox (e.g. niroshanaks@gmail.com) for end-to-end staging
// verification. ...
b.HasIndex(u => u.Email);
```

Backed by migration
`src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/20260622144933_Slice4_DropEmailUnique.cs`
which drops the unique index and recreates it non-unique.

**Result**: sign-in flow `A` (e.g. DevAuth stub with oid `dev-owner-00000000`)
provisions row `U_A`. Sign-in flow `B` (real Entra with oid `abc-real-oid-guid`)
provisions row `U_B`. Same email on both rows.

**Bootstrap targets one row; the current session uses the other.** Before
F11.7.5.10 the bootstrap operator promoted only the first matching row. The
current session's row was the OTHER one. On that row `is_platform_admin=false`
and `TenantMembership.UserId != U_currentSession.Id`.

Additional divergence source: `SetPersonaEmailHandler.Handle`
(`src/Modules/VrBook.Modules.Identity/Application/Users/Commands/SetPersonaEmailCommand.cs:52`)
looks up by `B2CObjectId` and rewrites `Email`. So a DevAuth persona whose
inbox was pointed at `niroshanaks@gmail.com` collides with the real Entra
sign-in row for the same human when that human later signs in through the
real IdP.

**Diagnosis stands.** The interim in `5acb5bb` widens bootstrap to promote
ALL rows sharing an email, but it does NOT prevent divergence on future
sign-ins, and it does NOT unify the identity for downstream FK references
(`tenant_memberships.user_id`, `booking.bookings.guest_user_id`, audit
`actor_user_id`). F11.7.6 is that structural fix.

---

## §2 Candidate analysis

### A. Provisioning upsert by (oid ∪ email)

`ProvisionUserHandler.Handle` looks up by oid; on miss, looks up by email;
on email-hit + oid-miss, **mutates the existing row's `B2CObjectId` to the
new oid** and refreshes the login stamps. On email-hit-multi (already-multi
DB), pick the row with the highest privilege (is_platform_admin > has-membership >
oldest CreatedAt) as the survivor and merge lower rows into it via a
domain-visible `ClaimOidForExistingProfile` call (see §3 for detail).

**Verified feasibility**: `User.B2CObjectId` currently has a private setter
(`src/Modules/VrBook.Modules.Identity/Domain/User.cs:14`) and is never
mutated in the domain. Adding a `ClaimOidForExistingProfile(newOid)` method
is a small aggregate surface change. The unique index on
`(b2c_object_id)` (`UserConfiguration.cs:15`) stays — the new oid replaces
the old one on the row, so uniqueness is preserved.

**Pros**:
- No schema change → no cross-schema FK migration.
- Zero downtime.
- Reversible: revert the handler; existing rows keep their (rewritten) oid;
  no data loss.
- Preserves ADR-0014 DB-wins precedence (`is_platform_admin` is still a
  column on the surviving row).

**Cons**:
- Rewriting a claimed identifier is philosophically noisy. But the Entra
  `oid` is stable *per identity*, not stable across "same human, different
  authentication flow"; the DevAuth stub oid is not a real Entra oid, so
  overwriting it with the real one on real-Entra first-login is the
  correct semantic — the DevAuth row was a placeholder.
- Role-address emails shared by real humans (`ops@company.com`) create
  cross-account access. Mitigation: F11.7.6 restricts the email→oid rebind
  path so that if BOTH the surviving row's oid AND the incoming oid are
  real Entra oids (both parse as `Guid.TryParse` — see §3 for the tight
  form; the earlier draft used a `dev-` prefix heuristic but that leaks
  because `dev-` isn't a reserved namespace in Entra), the rebind is
  refused with `email_already_claimed` (409). Only the DevAuth-persona
  side is ever rewriteable. See §3.

### B. Unique constraint on `identity.users.email` + backfill migration

Migration merges duplicates (privilege-wins survivor), remaps FKs
(`identity.tenant_memberships.user_id`, `booking.bookings.guest_user_id`,
`identity.audit_log.actor_user_id`, `messaging.threads.guest_user_id`,
`reviews.reviews.guest_user_id`), then enforces uniqueness. A fresh
oid arriving with an already-claimed email would then either (i) 23505 —
poor UX, OR (ii) route the sign-in to the surviving user id
(still needs handler logic — becomes candidate A on top of a hard DB
constraint).

**Verified feasibility**:
- `bookings.guest_user_id` has NO cross-schema FK
  (`src/Modules/VrBook.Modules.Booking/Infrastructure/Persistence/Migrations/20260606181431_InitBookingSchema.cs:26`
  is a plain uuid column, not a foreign key — the modular monolith crosses
  schemas, not FKs), so the "FK remap" is really "UPDATE
  bookings SET guest_user_id = @survivor WHERE guest_user_id IN (@losers)"
  in each module's schema. Same for reviews, messaging, audit.
- `tenant_memberships` has a partial unique index
  `(user_id, tenant_id) WHERE deleted_at IS NULL` per M.1 (referenced
  by `TenantMembership.Revive` XML comment,
  `src/Modules/VrBook.Modules.Identity/Domain/TenantMembership.cs:88-93`);
  merging two losers into one survivor could 23505 if both had active
  memberships in the same tenant. Migration must dedup memberships too.

**Pros**:
- Enforces the invariant our code already assumes: one email = one identity
  handle.
- No sign-in-time surprise (candidate A still runs on top of it).

**Cons (why not first)**:
- Requires a multi-schema, multi-module migration. Modular monolith rule
  is "each module owns its schema; migrations are per-module." A user-id
  merge crosses `identity`, `booking`, `reviews`, `messaging` — a
  coordinated deploy step, not a single `Add-Migration`.
- The DevAuth staging use case
  (`UserConfiguration.cs:22` says "DevAuth personas can share an inbox
  ...for end-to-end staging verification") — the WHOLE POINT of dropping
  the unique index in Slice 4 — would regress. We would have to reintroduce
  the constraint gated on the environment or a config flag. That's fragile.
- Rollback plan is a fresh migration (can't reverse a merge — data is gone).
  The Slice 4 comment shows this constraint was already deliberately relaxed.
  Re-enforcing it invalidates that design decision without new evidence
  that we NEED it (candidate A is sufficient).

### C. M.4 gate consults email OR oid for PA bypass

The gate `TenantAuthorizationBehavior.cs:60` would read "ANY user with this
email has `is_platform_admin=true`" instead of "the current user has
`is_platform_admin=true`". Requires a claim carrying the email so the gate
can query without an extra round-trip per request.

**Pros**:
- No data mutation.

**Cons**:
- Does NOT fix the tenant-claim problem: `ICurrentUser.TenantId` is stamped
  from the `TenantMembership` rows joined on `UserId`
  (`UserProvisioningMiddleware.cs:68-71`) — those rows still belong to the
  OTHER user id. So even with a PA email bypass, the M.4 gate might pass
  but `ICurrentUser.TenantId` is still null; every downstream handler that
  reads `TenantId` (RLS layer, `BackgroundTenantScope` capture in
  `CancelBookingHandler`, audit stamps) sees null, and the operator's PA
  bypass on the gate does not automatically restore `TenantId`.
- Widens the trust surface from `is_platform_admin` (single row property)
  to `email` (mutable per-row). A compromised IdP that changes a user's
  email → escalation. Or a `SetPersonaEmail` DevAuth call in staging
  that a config typo could then expose. This is a step BACKWARD from
  ADR-0014's "DB is the sole source of truth per row" invariant.
- Fails ADR-0014 §"Consequences → Positive → Audit-friendly" — grants are
  no longer 1:1 with rows; auditing "who is a PA right now" becomes
  a `GROUP BY email` scan.

### D. (Not needed)

Candidate A alone, done properly with survivor logic for the multi-row
DB we already have in staging, is enough. Adding B on top is future work
if we later see a case where "the DB happens to have two rows for the
same person" during a request that doesn't route through the handler.
That path doesn't exist today (every authenticated request goes through
`UserProvisioningMiddleware` before hitting a handler).

---

## §3 Picked candidate + rationale

**Candidate A** (Provisioning upsert by oid ∪ email, with survivor merge on
multi-row-hit) is the correct fix.

### Why A over B

- **Modular-monolith constraint**: B requires coordinated migrations across
  four module schemas. The identity module cannot 100% enforce email
  uniqueness without also cleaning up sibling-schema FKs, and no other
  module's migrations depend on this. A puts the fix where the causality is
  (provisioning) and lets each module keep its schema self-contained.
- **Slice 4 design intent**: `UserConfiguration.cs:26` deliberately relaxed
  the constraint. B reverses a design decision without new evidence.
  A closes the actual bug (provisioning creates diverging rows) without
  reversing the DB constraint.
- **Blast radius**: A is a handler + aggregate method + tests. B is a
  cross-module data migration during a live-staging window. A is deployable
  in one CI pass.

### Why A over C

- ADR-0014 explicitly puts `is_platform_admin` on the ROW as the source
  of truth. C dilutes that. A preserves it.
- C does not fix the `TenantId` null (the actual symptom) — the M.4 gate
  bypass would pass, but every downstream `ICurrentUser.TenantId` read
  still sees null.

### Survivor policy for existing multi-row data

For an existing staging DB with N rows for one email, choose the survivor
row (highest privilege first, tiebreak by earliest CreatedAt for stable
oid preservation):

1. Any row with `is_platform_admin = true`.
2. Any row with at least one active `tenant_memberships` row.
3. Earliest `CreatedAt`.

Non-survivor rows are soft-deleted (`DeletedAt = NOW()`, `DeletedBy = NULL`
for system-initiated); their oid is NOT preserved (the survivor keeps its
own oid). The next sign-in flow that carried a non-survivor's oid will hit
`GetByB2CObjectIdAsync → null`, then the new email-lookup branch will find
the survivor and rebind that survivor's oid to the incoming (real Entra) oid.

**FK remap for existing multi-row DBs**: because we soft-delete the losers
rather than hard-delete, the module-schema FKs
(`bookings.guest_user_id`, `reviews.reviews.guest_user_id`,
`messaging.threads.guest_user_id`, `audit_log.actor_user_id`,
`tenant_memberships.user_id`) still resolve. Bookings authored under a
loser row can still be read; new bookings authored via the surviving-row
session will be under the survivor. No cross-module data mutation needed.

### Guardrails

The oid rebind path (email-hit + oid-miss) is dangerous if two different
humans genuinely share an inbox (role addresses, distribution lists). The
handler MUST refuse to rebind when:

- The row has been "logged into as a real Entra identity before" — i.e.,
  `B2CObjectId` does NOT start with `dev-`, AND the incoming oid also does
  NOT start with `dev-`, AND they differ. In this case the safe action is:
  throw a business error (`BusinessRuleException("email_already_claimed")`),
  do NOT provision a new row (because that's the very bug we're fixing).
- The row was soft-deleted (`DeletedAt != null`). In this case: provision
  a fresh row (the loser row is not a candidate).

For all other cases (DevAuth-only origin → real Entra takeover; DevAuth
→ DevAuth email edit): rebind is safe.

---

## §4 Commit sequence

Each commit is a full vertical slice — code + tests + green CI — per the
"single-slice-per-commit" convention used by prior F11 slices.

### F11.7.6.1 — Failing tests for the provisioning upsert (RED)

**Scope**: Add failing unit tests (`tests/VrBook.Modules.Identity.Tests`)
that pin the intended `ProvisionUserHandler` behavior. No production code
changes. TDD entry gate.

**Files**:
- `tests/VrBook.Modules.Identity.Tests/Users/ProvisionUserHandlerTests.cs` (new)
  - `Handler_by_oid_hit_is_unchanged` — existing coverage baseline.
  - `Handler_by_email_hit_oid_miss_rebinds_oid_when_row_is_dev_origin`
  - `Handler_by_email_hit_oid_miss_throws_when_both_oids_are_real_entra`
  - `Handler_by_email_hit_oid_miss_when_multi_row_selects_platform_admin_survivor`
  - `Handler_by_email_hit_oid_miss_when_multi_row_selects_membership_survivor`
  - `Handler_by_email_hit_oid_miss_ignores_soft_deleted_rows`
  - `Handler_by_email_miss_provisions_new_row` — unchanged path.

**Tests**: the tests themselves.
**Local validation**: `dotnet test tests/VrBook.Modules.Identity.Tests --filter "FullyQualifiedName~ProvisionUserHandlerTests"` → expect all new tests to FAIL (compile errors OK if new methods don't yet exist).
**CI expectation**: RED. This is the RED half of the TDD pair.

### F11.7.6.2 — GREEN: `IUserRepository.GetActiveByEmailAsync` + `User.ClaimOidForExistingProfile`

**Scope**: Domain + repo surface for the upsert.

**Files**:
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/IUserRepository.cs` —
  add `Task<IReadOnlyList<User>> GetActiveByEmailAsync(string email, CancellationToken ct)`.
  Returns non-soft-deleted rows only.
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/UserRepository.cs` —
  impl using EF (`db.Users.Where(u => ((string)(object)u.Email) == email).ToListAsync(ct)`).
  Global query filter already excludes soft-deleted (uses `AggregateRoot.DeletedAt`).
- `src/Modules/VrBook.Modules.Identity/Domain/User.cs` — add:
  ```csharp
  public void ClaimOidForExistingProfile(string newOid) { ... }
  ```
  Preconditions: not soft-deleted; `newOid` non-empty. Raises `UserOidRebound`
  domain event (new; see contracts change below) — for the audit trail.
- `src/VrBook.Contracts/Events/IdentityEvents.cs` — add
  `UserOidRebound(Guid UserId, string OldOid, string NewOid)`.

**Tests**:
- `tests/VrBook.Modules.Identity.Tests/Domain/UserOidRebindTests.cs` (new) —
  domain-side test for `ClaimOidForExistingProfile` (preconditions, event
  raised, uniqueness at aggregate is caller's problem).
- Existing `ProvisionUserHandlerTests` still fail — handler not touched yet.

**Local validation**:
```
dotnet test tests/VrBook.Modules.Identity.Tests --filter "Category!=Integration"
```
Domain tests pass; handler tests still fail (intentional).

**CI expectation**: yellow (handler tests still red); this is a scaffold
commit. Squash-safe.

### F11.7.6.3 — GREEN: `ProvisionUserHandler` upsert-by-oid∪email

**Scope**: Rewrite the handler body per §3 policy.

**Files**:
- `src/Modules/VrBook.Modules.Identity/Application/Users/Commands/ProvisionUserHandler.cs` —
  new flow:
  1. `existing = users.GetByB2CObjectIdAsync(oid)` — hit → refresh (unchanged branch).
  2. Miss → `matches = users.GetActiveByEmailAsync(email)`.
  3. If `matches.Count == 0` → provision new (unchanged branch).
  4. If `matches.Count >= 1` → pick survivor per §3 ranking.
  5. Guardrail: if BOTH `survivor.B2CObjectId` AND the incoming `oid`
     are real Entra oids, throw
     `BusinessRuleException("email_already_claimed", detail...)`
     (do NOT provision — this is the ambiguous role-address case).
     **Real-Entra oids are always GUID-shaped**; DevAuth persona oids
     are the literal strings `dev-owner-00000000`, `dev-guest-00000001`,
     `dev-admin-00000002` (see `DevAuthHandler.cs:49-73`). The tight
     form of the check is therefore:
     ```csharp
     static bool IsRealEntraOid(string oid) => Guid.TryParse(oid, out _);
     if (IsRealEntraOid(survivor.B2CObjectId) && IsRealEntraOid(oid))
     {
         throw new BusinessRuleException(
             "email_already_claimed",
             $"Email '{email}' is already claimed by a different Entra identity ({survivor.Id}).");
     }
     ```
     Earlier draft used `!oid.StartsWith("dev-")` — rejected because
     `dev-` isn't reserved by Entra; the IdP could theoretically issue
     a `dev-`-prefixed oid. `Guid.TryParse` is the load-bearing shape
     check because DevAuth's three persona oids are the ONLY non-GUID
     oids in the system.
  6. Otherwise → `survivor.ClaimOidForExistingProfile(oid)`; refresh login
     stamps; return `survivor.Id`.

**Tests**:
- All F11.7.6.1 tests now GREEN.
- No changes to existing tests expected; middleware behavior unchanged
  (still hands the returned user id to `HttpContext.Items`).

**Local validation**:
```
dotnet test tests/VrBook.Modules.Identity.Tests --filter "Category!=Integration"
```
Full identity module tests green.

**CI expectation**: green.

### F11.7.6.4 — Data-heal migration for existing multi-row DBs

**Scope**: One-shot SQL to soft-delete non-survivor rows in staging (and
any prod DB that ever hit this path — none should, but defensive).

**Files**:
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/YYYYMMDDHHMMSS_OpsM10_2_F11_7_6_SoftDeleteDuplicateUsers.cs` (new)

Migration body (Postgres SQL, wrapped in EF's `MigrationBuilder.Sql`):

```sql
-- OPS.M.10.2 F11.7.6 — soft-delete non-survivor duplicate user rows.
-- Survivor per row-group is chosen by:
--   1. is_platform_admin desc
--   2. active_memberships count desc
--   3. created_at asc
-- The survivor keeps its oid; non-survivors are soft-deleted so their FK
-- references (bookings.guest_user_id, reviews, messaging, audit) still
-- resolve.
WITH ranked AS (
    SELECT
        u."Id",
        u.email,
        u.is_platform_admin,
        u.created_at,
        (SELECT COUNT(*) FROM identity.tenant_memberships tm
           WHERE tm.user_id = u."Id" AND tm.deleted_at IS NULL) AS active_memberships,
        ROW_NUMBER() OVER (
            PARTITION BY u.email
            ORDER BY u.is_platform_admin DESC,
                     (SELECT COUNT(*) FROM identity.tenant_memberships tm
                        WHERE tm.user_id = u."Id" AND tm.deleted_at IS NULL) DESC,
                     u.created_at ASC
        ) AS rn
    FROM identity.users u
    WHERE u.deleted_at IS NULL
)
UPDATE identity.users u
   SET deleted_at = NOW(),
       deleted_by = NULL,   -- system-initiated; no actor id
       updated_at = NOW()
  FROM ranked r
 WHERE u."Id" = r."Id" AND r.rn > 1;
```

**Down migration**: idempotent no-op with a comment
(`-- Cannot reverse: survivor merge is destructive at the tenant-membership
layer if we ever add per-tenant deduplication.`). Reversibility strategy
for staging: `pg_dump identity.users, tenant_memberships` BEFORE running
the migration on staging; restore from that dump is the rollback.

**Note on bypassing `User.Deactivate()`**: The migration writes
`deleted_at`/`deleted_by`/`updated_at` directly via raw SQL rather than
routing through the `User.Deactivate(reason, actorId)` domain method at
`User.cs:146-155`. This is deliberate:

- `Deactivate` raises a `UserDeactivated` domain event; the migration
  runs at deploy time with no handlers registered (grep confirms no
  `UserDeactivated` consumer in `src/Modules`).
- `Deactivate` requires a non-nullable `actorId Guid`; this is a
  system-initiated heal with no actor. Passing `Guid.Empty` would be a
  lie in the audit log.
- The migration is a one-shot data heal, not part of the aggregate's
  normal lifecycle.

A future maintainer who wants to "fix" this by routing through the
domain method should note this trade-off. Adding a `UserAutoHeal`
domain method with nullable `actorId` semantics is out-of-scope for
F11.7.6; the raw SQL is correct for the one-shot use.

**Tests**:
- `tests/VrBook.Api.IntegrationTests/Identity/DuplicateUserHealMigrationTests.cs`
  (new) — Category=Integration, connection-string gated. Seeds 3 rows
  for one email (one PA, one with membership, one plain), runs the
  migration via `DbContext.Database.Migrate()`, asserts one survivor
  (the PA), two soft-deleted.

**Local validation**:
```
dotnet ef migrations add OpsM10_2_F11_7_6_SoftDeleteDuplicateUsers \
  --project src/Modules/VrBook.Modules.Identity \
  --startup-project src/VrBook.Api \
  --context IdentityDbContext
dotnet build
dotnet test tests/VrBook.Api.IntegrationTests --filter "FullyQualifiedName~DuplicateUserHealMigrationTests"
```

**CI expectation**: green. Integration test runs against the CI Postgres
service container.

### F11.7.6.5 — Arch tests locking the invariant

**Scope**: Source-text scans in `tests/VrBook.Architecture.Tests` that
prevent a future regressor from reintroducing the oid-only provisioning
shape.

**Files**:
- `tests/VrBook.Architecture.Tests/ProvisionUserHandlerShapeTests.cs` (new).

Assertions (regex over
`src/Modules/VrBook.Modules.Identity/Application/Users/Commands/ProvisionUserHandler.cs`):

1. Handler body contains `GetActiveByEmailAsync` — proves the email
   fallback branch exists.
2. Handler body contains `ClaimOidForExistingProfile` — proves the rebind
   path exists.
3. Handler body contains the string literal `"email_already_claimed"` —
   proves the guardrail exists.
4. `IUserRepository.cs` declares `GetActiveByEmailAsync` — signature check.

Existing shape-scan pattern is
`tests/VrBook.Architecture.Tests/OwnerActionTenantResolutionTests.cs`;
copy its `ReadControllerSource()` helper (rename to `ReadHandlerSource`).

**Tests**: the scans themselves — no runtime dependency, no DB.

**Local validation**:
```
dotnet test tests/VrBook.Architecture.Tests --filter "FullyQualifiedName~ProvisionUserHandlerShapeTests"
```

**CI expectation**: green.

### F11.7.6.6 — Integration coverage: end-to-end multi-oid convergence

**Scope**: One end-to-end test that reproduces the staging bug in a fixture.

**Files**:
- `tests/VrBook.Api.IntegrationTests/Identity/MultiOidConvergenceTests.cs`
  (new). Category=Integration. Uses `IdentityApiFixture`.

Test:

1. Sign in through DevAuth as `dev-owner-...` — provisions row `U_A` with
   email `pro@vrbook.local`.
2. Rewrite `U_A.Email` to `niroshanaks@gmail.com` via `SetPersonaEmail`.
3. Simulate a fresh real-Entra login: hit any authenticated endpoint with
   a bearer token whose `oid` claim is `real-oid-xyz` and `emails` claim
   is `niroshanaks@gmail.com`.
4. Assert (a) NO new row is provisioned — `identity.users` count is
   unchanged; (b) `U_A.B2CObjectId` is now `real-oid-xyz` (the rebind);
   (c) `GET /api/v1/me` on the new session returns `U_A.Id`.

**Local validation**:
```
dotnet test tests/VrBook.Api.IntegrationTests --filter "FullyQualifiedName~MultiOidConvergenceTests"
```

**CI expectation**: green.

### F11.7.6.7 — Remove the F11.7.5.10 interim widening + close-out doc

**Scope**: Now that provisioning converges rows at write time, the
`BootstrapOperator` widened-target loop can revert to single-row semantics.
Not strictly required (the loop is idempotent and harmless), but leaving
the widened loop in place while the underlying hazard is fixed is dead
code that misleads future readers.

**Files**:
- `src/VrBook.Api/Controllers/IdentityController.cs` — revert the
  `usersWithEmail` loop back to single-user semantics (rename local var
  back to `user`, remove `usersWithEmail.Count` response field). Keep the
  F11.7.5.10 comment block as a historical note pointing to F11.7.6.
- Close-out §11 in `docs/OPS_M_10_2_F11_7_6_MULTI_ROW_USER_FIX.md`
  (append to this file after CI green).

**Tests**: existing bootstrap tests (if any) still pass; no new tests.

**Local validation**: `dotnet test --filter "Category!=Integration"`.

**CI expectation**: green.

---

## §5 Backfill plan (data heal — F11.7.6.4 detail)

Dupes identified by:

```sql
SELECT email, COUNT(*) AS rowcount
  FROM identity.users
 WHERE deleted_at IS NULL
 GROUP BY email
HAVING COUNT(*) > 1;
```

**Survivor selection** (§3 policy, restated as SQL — see F11.7.6.4 for the
CTE):

1. `is_platform_admin DESC`
2. `(count of active tenant_memberships) DESC`
3. `created_at ASC`

**FK remap**: NOT NEEDED because losers are soft-deleted, not hard-deleted.
Their `Id` values remain valid uuid references. Existing
`bookings.guest_user_id`, `reviews.reviews.guest_user_id`,
`messaging.threads.guest_user_id`, and `audit_log.actor_user_id` values
continue to resolve. The row is present, just marked `deleted_at IS NOT NULL`
so it no longer participates in the global query filter for `IdentityDbContext`.

**Reversibility strategy for the staging window**:

1. Immediately before applying the F11.7.6.4 migration on staging, take:
   ```
   pg_dump --schema=identity --data-only \
           --table=identity.users \
           --table=identity.tenant_memberships \
           > f11_7_6_pre_heal.sql
   ```
2. If the migration causes a regression, roll back by restoring the two
   tables from `f11_7_6_pre_heal.sql` (truncate + restore), then revert
   the F11.7.6.3 code deploy.

**Prod**: The bug shipped to staging only; prod's `identity.users` should
have zero multi-row groups. The migration runs on prod but the CTE affects
zero rows.

---

## §6 Arch-test additions

Two arch-test files, both regex source-text scans (no Roslyn needed,
matching `tests/VrBook.Architecture.Tests/OwnerActionTenantResolutionTests.cs`
style):

### `ProvisionUserHandlerShapeTests.cs` (new, ships in F11.7.6.5)

- **`Handler_reads_by_oid_first`** — asserts `GetByB2CObjectIdAsync`
  precedes `GetActiveByEmailAsync` in the source text. Fails if a refactor
  inverts the order (which would spam-provision on every real-Entra
  sign-in that happens to share an email with a stale row).
- **`Handler_has_email_fallback`** — asserts `GetActiveByEmailAsync` is
  called. Fails if a regressor removes the fallback and reverts to
  provision-new-on-oid-miss.
- **`Handler_has_role_address_guardrail`** — asserts the literal
  `"email_already_claimed"` appears. Fails if the guardrail is silently
  removed (which would let a compromised Entra tenant hijack an existing
  PA account by changing its email to match).
- **`IUserRepository_exposes_GetActiveByEmailAsync`** — asserts the
  interface declares the method. Fails if it's removed and the handler
  starts inlining EF queries again.

### `UserAggregateRebindShapeTests.cs` (new, ships in F11.7.6.5)

- **`User_has_ClaimOidForExistingProfile`** — asserts the method exists on
  the aggregate. Fails if a refactor moves the rebind out to the handler
  (leaking domain state mutation into infra).
- **`User_B2CObjectId_setter_is_private`** — asserts `public string
  B2CObjectId { get; private set; }`. Fails if a regressor makes it
  publicly settable (bypassing the domain event).

---

## §7 Integration-test additions

In `tests/VrBook.Api.IntegrationTests/Identity/`:

### `MultiOidConvergenceTests.cs` (F11.7.6.6, detailed in §4)

The end-to-end reproducer.

### `DuplicateUserHealMigrationTests.cs` (F11.7.6.4, detailed in §4)

Migration correctness check.

### `ProvisioningUpsertTests.cs` (F11.7.6.3 supplement, optional)

Real-DB variant of the unit-test scenarios in F11.7.6.1 — same assertions
but through the mediator end-to-end, so we catch any EF query-filter
weirdness on the `IsDeleted` filter that the in-memory unit tests can't
see.

---

## §8 Residual risks + out-of-scope

### Residual risks

1. **Role-address collision** (`ops@company.com` shared by two real humans).
   The §3 guardrail throws `email_already_claimed` — but that means the
   second human's sign-in fails. Mitigation: a follow-up slice can add an
   admin-facing "unlink email" surface (`PATCH
   /api/v1/admin/users/{id}/unlink-email`) that decouples the row from the
   email so the second person can sign in fresh. Out of scope for
   F11.7.6.

2. **Race on concurrent sign-in of a fresh oid with an existing email.**
   Two parallel real-Entra logins for the same email → both hit
   `GetByB2CObjectIdAsync → null`, both hit
   `GetActiveByEmailAsync → 1 row`, one wins the rebind, the other's
   `SaveChangesAsync` throws optimistic-concurrency (`RowVersion` guard on
   `AggregateRoot`). The failure is retryable at the middleware layer,
   which catches provisioning failures and logs
   (`UserProvisioningMiddleware.cs:103`). Acceptable — retries land on
   the just-rebound row and hit the oid-hit branch.

3. **DevAuth `SetPersonaEmail` after real-Entra sign-in**. Would rewrite
   the survivor row's email. Next real-Entra sign-in from a DIFFERENT
   human (whose email got assigned to the persona) would hit the guardrail
   or the rebind path depending on oids. This is a DevAuth-only path,
   already prod-gated at handler + controller (§F8 audit #20), so no
   prod risk. Add a runbook note that `SetPersonaEmail` on a DevAuth
   persona that shares an email with a real-Entra row is a "collision
   step" — not a code fix.

4. **Orphaned `TenantMembership` rows for soft-deleted losers**. The
   migration soft-deletes the loser user rows but does NOT soft-delete
   their `identity.tenant_memberships` rows. The middleware queries
   memberships by `user_id`; a lookup by the loser row's `Id` still
   returns those memberships. The loser row itself is invisible to the
   middleware (global query filter on `IdentityDbContext` respects
   `deleted_at`), so a real-Entra sign-in that hits the loser oid can't
   even happen post-migration (the oid was rebound to the survivor).
   The orphaned memberships are dead-weight rows visible via
   `IgnoreQueryFilters()`. Not a correctness bug — a cleanliness bug.
   `TenantMembershipConfiguration.cs:23-26` declares
   `.OnDelete(DeleteBehavior.Cascade)` for `User→TenantMembership`, but
   this is EF Cascade on **hard-delete**; soft-delete via `UPDATE
   deleted_at = NOW()` does not trigger the cascade. Cleanup pass
   scoped for a future OPS.M.11 hygiene slice.

### Out of scope

- **DB-level `UNIQUE (email) WHERE deleted_at IS NULL` partial index**
  (candidate B). Reconsider after 60 days of production observability if
  we see zero divergence. If yes, add the constraint in a future slice
  as belt-and-suspenders.
- **Cross-schema FK enforcement** (`bookings.guest_user_id` REFERENCES
  `identity.users.Id`). Would require breaking the "one schema per
  module" invariant. Not this slice.
- **Merging tenant_memberships across multi-row survivors** with active
  memberships in DIFFERENT tenants. Today's staging DB has one such
  case at most; the current soft-delete-losers approach leaves the
  loser's memberships intact but the loser row hidden. The membership
  join in `UserProvisioningMiddleware` won't find them (it joins on
  `UserId`, which is the loser's id, which the middleware never returns
  from provisioning anymore). Effectively they become orphaned. Deferred
  to a future slice.
- **DevAuth stub oid stability across environments** — `dev-owner-...`
  shape is baked into `DevAuthPersonas.Owner`. Not touching.

---

## §11 Close-out (append after CI green on F11.7.6.7)

_To be filled in after the final commit lands._
