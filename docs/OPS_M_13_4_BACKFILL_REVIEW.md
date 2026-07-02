# OPS.M.13.4 Backfill Migration — Design Review

- **Date:** 2026-07-02
- **Reviewer perspective:** Paired architect on M.13.4 (the single-commit backfill).
- **Predecessors:** [`OPS_M_13_IDENTITY_REDESIGN_PLAN.md`](./OPS_M_13_IDENTITY_REDESIGN_PLAN.md) — the plan under review. [`OPS_M_10_2_F11_ARCHITECTURAL_REVIEW.md`](./OPS_M_10_2_F11_ARCHITECTURAL_REVIEW.md) §Ev-E — migrator's BYPASSRLS grant.
- **Current CI green head:** `c49dee8` (M.13.3 fixup). Middleware sends `ProvisionOrLinkUserCommand`; `identity.user_identities` + `identity.migration_audit` exist and take writes on fresh sign-ins.

---

## Top-of-doc flags — findings that must be reconciled before writing M.13.4

**F1. The FK-holder list in the plan is under-enumerated.** Plan §2.1 claims 7 tables. I find **at least 10** tables with user-Guid columns that will hold non-survivor ids post-collapse — plus **every aggregate root's `created_by` / `updated_by` / `deleted_by` audit columns** (dozens of tables across every module). Detail in Section 1 below. The plan's step-3 UPDATEs will leave these unrewritten and the API will keep writing new rows against the survivor id going forward — but historical audit attribution across every module aggregate silently drifts. This must be a conscious decision, not an oversight.

**F2. The step 7 partial UNIQUE index is already created by M.13.2 — not by M.13.4.** Plan §2.7 step 7 and §3.4 step 7 both say the M.13.4 migration creates `users_email_active_lower_uq`. But the shipped M.13.2 migration `20260701225121_OpsM13_UserIdentitiesAndMigrationAudit.cs:135-139` already creates it. Two consequences:
  - The plan step 7 is redundant — remove it or make it `CREATE UNIQUE INDEX IF NOT EXISTS`.
  - **Ordering hazard:** M.13.2 shipped this index against a DB that may still hold multi-row-per-email hazards (see F3). If M.13.2 ran cleanly on staging, staging currently has no case-insensitive email duplicates active. If it did NOT run cleanly, M.13.2 crashed and M.13.4 has a different problem than the one it was designed to solve. Verify staging DB state before writing M.13.4.

**F3. Post-M.13.3 code writes `b2c_object_id = 'm13-placeholder-{id:N}'` for fresh users.** `src/Modules/VrBook.Modules.Identity/Domain/User.cs:104-112` sets a placeholder. Any fresh sign-in on staging since M.13.3 landed has produced a row with a non-Entra-shaped `b2c_object_id`. Plan §3.4 step 4 regex-classifies non-UUID oids as `provider='test'`, which would emit bogus `user_identities` rows for these placeholders. **Fix in M.13.4 step 4:** exclude `b2c_object_id LIKE 'm13-placeholder-%'` — do not create a UserIdentity row for those users' placeholder oid at all.

**F4. `OpsM13_EmailCanonicalUsersShapeTests` (referenced by user as the RED test M.13.4 must green) does not exist yet.** Only `OpsM13_UserIdentitiesSchemaShapeTests` (M.13.2) and `OpsM13_ProvisioningEmailFirstShapeTests` (M.13.3) are on disk. M.13.4 must both **create** this test file and make it pass. Plan §4 M.13.1 listed it as the RED-in-M.13.1 shape test, but the M.13.1 RED commit didn't ship it. Fold into M.13.4.

**F5. Plan's soft-delete step 5 does NOT set `deleted_by`.** Plan §3.4 step 5 only sets `deleted_at` + `updated_at`. `identity.users.deleted_by` is a `uuid?` column. Existing F11.7.6.4 heal sets `deleted_by = NULL` explicitly. We should preserve that explicit NULL to signal "system-initiated" and never a colliding user Guid. Section 2 elaborates.

**F6. Survivor picker is NOT a total ordering.** Plan §3.4 precedence: PA DESC → active-membership-count DESC → CreatedAt ASC. If two rows in the same email group have identical PA + membership-count + CreatedAt (down to the microsecond — extremely unlikely but not impossible for scripted seeds), the ROW_NUMBER assignment is non-deterministic. Add `"Id" ASC` as final tiebreaker. Section 2 elaborates.

**F7. In-flight sign-ins during the migration.** M.13.4 runs inside a Container Apps Job **before** the API revision update, per the migrator gate. But the OLD API revision keeps serving until the new one goes live. An in-flight sign-in mid-migration by the OLD API image could INSERT a fresh users row that survives the snapshot but wasn't part of the survivor map. Section 4 covers this — turns out self-healing thanks to M.13.2's partial UNIQUE.

---

## Section 1 — Current state audit

### 1.1 Tables holding a Guid pointing at `identity.users.Id`

Enumerated by grepping every EF configuration file for `user_id`-shaped columns. The plan claims 7; I find **more**. Column names are the actual DB names (snake_case) from the configurations:

| # | Schema.Table | Column | Nullable | Soft-deletable | Enforced FK at DB? | In plan? |
|---|---|---|---|---|---|---|
| 1 | `identity.tenant_memberships` | `user_id` | NOT NULL | yes (`deleted_at`) | **yes** (CASCADE) | yes |
| 2 | `identity.user_identities` | `user_id` | NOT NULL | yes (`deleted_at`) | **yes** (CASCADE) | new in M.13.2 — no historical rows to rewrite |
| 3 | `identity.audit_log` | `actor_user_id` | **NULLABLE** | no | no | yes |
| 4 | `catalog.properties` | `owner_user_id` | NOT NULL | yes (`deleted_at`) | no | yes |
| 5 | `booking.bookings` | `guest_user_id` | NOT NULL | yes (`deleted_at`) | no | yes |
| 6 | `reviews.reviews` | `guest_user_id` | NOT NULL | yes (`deleted_at`) | no | yes |
| 7 | `messaging.threads` | `guest_user_id` | NOT NULL | yes (`deleted_at`) | no | yes |
| 8 | `messaging.threads` | `owner_user_id` | NOT NULL | yes (`deleted_at`) | no | yes |
| 9 | `messaging.messages` | `sender_user_id` | NOT NULL | no (message-level immutable) | no | **NO — MISSING** |
| 10 | `messaging.messages` | `recipient_user_id` | NOT NULL | no | no | **NO — MISSING** |
| 11 | `notifications.notification_log` | `recipient_user_id` | NOT NULL | (schema audit-only) | no | yes |

Plus **every aggregate root in every module** carries `created_by uuid`, `updated_by uuid`, `deleted_by uuid` populated from `ICurrentUser.UserId` via `src/VrBook.Infrastructure/Persistence/BaseDbContext.cs:88-111` (`ApplyAudit`). Tables affected include but are not limited to: `catalog.properties`, `catalog.amenities`, `booking.bookings`, `booking.holds`, `booking.availability_blocks`, `reviews.reviews`, `messaging.threads`, `notifications.notification_log`, `loyalty.*`, `sync.*`, plus `identity.users` / `identity.tenants` / `identity.tenant_memberships`.

**Recommendation:**
1. Add `messaging.messages` (2 columns) to the plan's §3.4 step 3 — this is the biggest missed rewrite.
2. Make an **explicit decision** on the aggregate audit columns (`*_by`). Two options:
   - **(a) Rewrite them all** — one UPDATE per table joined against `_work_survivor_map`. This is the semantically clean choice; historical "who did what" now points at survivor. ~20 extra UPDATE statements; small volume; single-transaction OK for staging (10 users).
   - **(b) Leave them** — historical audit columns hold dead user Guids that no longer resolve. Not a functional bug (nothing FKs onto them; no code follows a `created_by` → `identity.users` join today), but it makes forensic audit incomplete.
   - **Recommended: (a).** F11's whole diagnosis was "we don't know what actually happened." Leaving orphan audit Guids is the same class of blindness.

**Only two of these columns have a DB-level FK** (tenant_memberships and user_identities). The rest are un-constrained `uuid` columns. That means **no NOT-NULL FK constraint fires mid-migration** for the app tables — the risk is Section 3 territory but the answer is simpler than the plan implies.

### 1.2 Direct references to `users.b2c_object_id` in app code (post-M.13.3)

Places that touch the column or aggregate property, verified against `c49dee8`:

| Path | Line | Nature | Post-M.13.4 status |
|---|---|---|---|
| `src/Modules/VrBook.Modules.Identity/Domain/User.cs` | 14 | Property declaration | Must drop |
| `src/Modules/VrBook.Modules.Identity/Domain/User.cs` | 46-76 | `[Obsolete] Provision(b2cObjectId, ...)` overload | Must delete |
| `src/Modules/VrBook.Modules.Identity/Domain/User.cs` | 96-120 | New `Provision(email, ...)` sets placeholder | Placeholder assignment removed |
| `src/Modules/VrBook.Modules.Identity/Domain/User.cs` | 213-236 | `ClaimOidForExistingProfile` | Delete |
| `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/UserConfiguration.cs` | 14-15 | Column mapping + old unique index | Delete both lines |
| `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/UserRepository.cs` | 8-9 | `GetByB2CObjectIdAsync` | Delete (unused post-M.13.3) |
| `src/Modules/VrBook.Modules.Booking/Application/Commands/PlaceBookingHandler.cs` | 90 | `currentUser.Email ?? currentUser.B2CObjectId ?? "Guest"` | Safe — reads `ICurrentUser` (JWT-side), not DB. Kept. |
| `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs` | 64-66 | `B2CObjectId` property reads JWT `oid` claim | Safe — token-side accessor. Rename in a separate commit. |
| `src/VrBook.Contracts/Interfaces/ICurrentUser.cs` | 13 | `string? B2CObjectId` in interface | Keep — token-side accessor stays. |
| `src/VrBook.Infrastructure/Common/AnonymousCurrentUser.cs` | 12 | Returns `null` | No change |
| `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/DevAuthHandler.cs` | 58 | Comment only | No change |
| `tests/VrBook.Api.IntegrationTests/Identity/TenantSchemaMigrationTests.cs` | 195, 261 | Raw INSERT with `b2c_object_id` column | **BREAKS** — must update to omit that column post-M.13.4 |

**Cutover surface:**
- Zero application read-paths hit the DB column directly (they go through the aggregate property, which we drop with the column in the same commit).
- Zero cross-module reads (nothing outside Identity module references `B2CObjectId`).
- **One test file** (`TenantSchemaMigrationTests.cs`) breaks; update in same M.13.4 commit.
- The `ICurrentUser.B2CObjectId` claim-side accessor keeps working — it reads JWT `oid`, not DB. `PlaceBookingHandler`'s guest-name fallback still functions.

**Conclusion:** the DB-column drop and code-reference cleanup fit cleanly in one commit. No cross-module blast radius. This is safe.

---

## Section 2 — Algorithm review (§3.4 walk-through)

**Preamble: which role runs this.** `MigrateAsync()` in `src/VrBook.Migrator/Program.cs:71` uses the migrator's connection (role `vrbook_migrator`, with BYPASSRLS granted by `20260628131731_OpsM9_Identity_RlsPolicies.cs:32-40`). Cross-schema UPDATEs against `booking.*`, `reviews.*`, `messaging.*`, `notifications.*`, `catalog.*` go through `MigrationBuilder.Sql` — this bypasses EF's `TenantGucCommandInterceptor` (which lives on `DbContext` command interception). So `app.tenant_id` GUC remains empty. Combined with BYPASSRLS on the role, **no RLS policy will silently truncate our UPDATEs.** Ev-E holds.

### Step 1 — Snapshot to `_pre_m13_snap`
- SQL is safe under BYPASSRLS.
- **Missing from snapshot list:** `messaging.messages` (sender/recipient columns), plus every aggregate's `created_by`/`updated_by`/`deleted_by` if we rewrite them per §1.1 Option (a). Add.
- Ordering: OK — pure `CREATE TABLE AS TABLE` reads.
- migration_audit INSERT is present. Good.

### Step 2 — Survivor pick
- BYPASSRLS OK. Reads only `identity.users` and `identity.tenant_memberships` (neither under RLS — Ev-E).
- **F6 tiebreaker fix.** Add `"Id" ASC` after `created_at ASC` in the ORDER BY. This costs nothing and closes a total-ordering gap.
- Preferred shape:
  ```sql
  CREATE TEMP TABLE _work_survivor_map AS
  WITH ranked AS (
      SELECT
          u."Id",
          lower(u.email) AS email_key,
          ROW_NUMBER() OVER (
              PARTITION BY lower(u.email)
              ORDER BY u.is_platform_admin DESC,
                       (SELECT COUNT(*) FROM identity.tenant_memberships tm
                          WHERE tm.user_id = u."Id" AND tm.deleted_at IS NULL) DESC,
                       u.created_at ASC,
                       u."Id" ASC                  -- F6: total ordering
          ) AS rn
      FROM identity.users u
      WHERE u.deleted_at IS NULL
  ),
  survivors AS (SELECT "Id", email_key FROM ranked WHERE rn = 1)
  SELECT r."Id" AS non_survivor_id, s."Id" AS survivor_id
    FROM ranked r
    JOIN survivors s ON s.email_key = r.email_key
   WHERE r.rn > 1;
  ```
- migration_audit row for step 2: **missing in plan**. Add one recording `SELECT COUNT(*) FROM _work_survivor_map` as `affected_count`.

### Step 3 — FK rewrites
- BYPASSRLS OK for all cross-schema tables.
- **Ordering:** current shape is fine because each UPDATE is independent — the survivor_map is fixed.
- **Missing from step 3 (per F1):**
  - `messaging.messages.sender_user_id`
  - `messaging.messages.recipient_user_id`
  - All aggregate `created_by` / `updated_by` / `deleted_by` columns if we adopt §1.1 Option (a).
- **Tenant_memberships collision guard (step 3a):** the plan's `NOT EXISTS` clause is correct; no fix needed.
- Each UPDATE should emit its own `migration_audit` row. Wrapper pattern:
  ```sql
  WITH updated AS (
      UPDATE booking.bookings b SET guest_user_id = m.survivor_id
        FROM _work_survivor_map m
       WHERE b.guest_user_id = m.non_survivor_id
      RETURNING 1
  )
  INSERT INTO identity.migration_audit
      (id, migration_name, step_name, affected_count, notes, executed_at)
  SELECT gen_random_uuid(),
         'OpsM13_UserIdentities_And_EmailCanonical',
         'rewrite_booking_bookings.guest_user_id',
         (SELECT COUNT(*) FROM updated), NULL, NOW();
  ```
  Repeat per column. This is how F11.7 should have written its migrations and didn't — this fix realizes Ev-F.

### Step 4 — Populate `user_identities`
- BYPASSRLS OK; `identity.user_identities` is not under RLS.
- **F3 placeholder exclusion.** Add to WHERE clause on both INSERT SELECTs:
  ```sql
  AND u.b2c_object_id NOT LIKE 'm13-placeholder-%'
  ```
- **Split into 4a (survivors) + 4b (non-survivors linked to survivor).** This makes step 4 emit exactly one identity row per **live human**, all pointing at survivor ids. Cleaner semantics.
  ```sql
  -- Step 4a — identity rows only for SURVIVORS
  INSERT INTO identity.user_identities (...)
  SELECT gen_random_uuid(), u."Id", ..., u.b2c_object_id, ...
    FROM identity.users u
   WHERE u.deleted_at IS NULL
     AND u.b2c_object_id NOT LIKE 'm13-placeholder-%'
     AND NOT EXISTS (SELECT 1 FROM _work_survivor_map m WHERE m.non_survivor_id = u."Id");
  -- Step 4b — identity rows for NON-SURVIVORS mapped to their survivor
  INSERT INTO identity.user_identities (...)
  SELECT gen_random_uuid(), m.survivor_id, ..., u.b2c_object_id, ...
    FROM identity.users u
    JOIN _work_survivor_map m ON m.non_survivor_id = u."Id"
   WHERE u.b2c_object_id NOT LIKE 'm13-placeholder-%'
  ON CONFLICT (provider, external_id) DO NOTHING;
  ```
- migration_audit row: add one per INSERT.

### Step 5 — Soft-delete non-survivors
- **F5 fix.** Set `deleted_by = NULL` explicitly. The `AggregateRoot.DeletedBy` column is nullable — NULL is the standard "system-initiated" signal (F11.7.6.4 uses NULL; F11.7.7 uses NULL). No system-actor UUID collides with a real user Guid — `NULL` is unambiguous. Do NOT pick a magic Guid; do NOT pick Guid.Empty.
  ```sql
  UPDATE identity.users u
     SET deleted_at = NOW(),
         deleted_by = NULL,
         updated_at = NOW()
    FROM _work_survivor_map m
   WHERE u."Id" = m.non_survivor_id;
  ```
- CASCADE FK from `identity.user_identities.user_id` triggers ONLY on hard DELETE, not soft-delete. Safe.

### Step 6 — Drop column + old index
- `DROP INDEX IF EXISTS` guards against a rename.
- `ALTER TABLE identity.users DROP COLUMN b2c_object_id` — hard drop is fine because we're inside one transaction.
- migration_audit row: add.

### Step 7 — Create partial UNIQUE — REMOVE OR IF NOT EXISTS
- **F2: this is already done by M.13.2.** Change to `CREATE UNIQUE INDEX IF NOT EXISTS` OR remove the step entirely. Recommend keep as `IF NOT EXISTS` for defensive re-creation semantics.

### Step 8 — Final audit — OK.

### Soft-delete actor conclusion (per F5)
`deleted_by = NULL`. No magic UUID. Never `Guid.Empty` (real seed collision hazard).

---

## Section 3 — RLS / cross-schema considerations

### 3.1 BYPASSRLS confirmed at connection level for migrator
- `20260628131731_OpsM9_Identity_RlsPolicies.cs:32-40` grants `BYPASSRLS` to the role `vrbook_migrator`.
- Deploy-config verification: staging config uses migrator credentials. Flagged for the deploy-day runbook (Section 7).

### 3.2 DbContext SET SESSION mechanism does NOT interfere
- `TenantGucCommandInterceptor` executes `SELECT set_config('app.tenant_id', ..., true)` per DbContext command. But M.13.4 runs via `MigrationBuilder.Sql` on `IdentityDbContext.Database.MigrateAsync()`. Since the migrator role has BYPASSRLS, this doesn't block writes.

### 3.3 Cross-schema UPDATE nullability
- All FK columns in the plan's list are `NOT NULL` except `identity.audit_log.actor_user_id` (nullable). None of the UPDATEs assign NULL — they all rewrite to a valid survivor Guid. No NOT-NULL constraint fires.

### 3.4 Nothing else
- No CHECK constraints reference `identity.users.Id` values.
- No trigger on `identity.users`.
- The soft-delete update stamps `updated_at`; `RowVersion` is not incremented in the SQL. That's fine (raw SQL bypasses the RowVersion arch guardrail — M.13.4 is a data-heal, same pattern as F11.7.6.4).

---

## Section 4 — Rollback + partial-apply story

### 4.1 Single transaction — confirmed
`ctx.Database.MigrateAsync()` wraps the migration `Up()` in a Postgres transaction by default. All 8 steps of M.13.4 sit in ONE transaction. If any step fails, everything rolls back automatically — including the `_pre_m13_snap` schema creation.

**Add to the migration's XML doc comment:** "Runs inside EF Core's default per-migration transaction. Any step failure rolls back the entire migration atomically."

### 4.2 Crash at step 5 of 8 — what happens
- Transaction rolls back. DB is in pre-M.13.4 state. `_pre_m13_snap` doesn't exist. `_work_survivor_map` is a TEMP table (auto-dropped).
- migration_audit has NO rows from this run.
- `__EFMigrationsHistory` does NOT record M.13.4 as applied.
- Fix the migration → re-deploy migrator → it retries M.13.4 from scratch. **Fully idempotent by construction.**

### 4.3 Per-step transactions — rejected
- Con: partial state is a debugging nightmare.
- Con: complicates the migration authoring.
- **Recommendation: single transaction.**

### 4.4 Estimated lock-hold time
- **Staging (10 users, single-tenant):** < 500 ms. Safe.
- **Hypothetical prod 10K users:** 30-60 seconds lock hold on `identity.users`. Would block live sign-ins.
- **For M.13.4 staging shipping this slice: single-tx is fine.** Prod cutover reopens the question.

---

## Section 5 — Test plan

### 5.1 Move `OpsM13_EmailCanonicalUsersShapeTests` from RED to GREEN
Per F4, this file doesn't exist yet. M.13.4 must **create** it. Recommended shape:

```csharp
// tests/VrBook.Architecture.Tests/OpsM13_EmailCanonicalUsersShapeTests.cs
public sealed class OpsM13_EmailCanonicalUsersShapeTests
{
    [Fact]
    public void User_aggregate_has_no_B2CObjectId_property() { ... }

    [Fact]
    public void IdentityDbContext_snapshot_omits_b2c_object_id_column() { ... }

    [Fact]
    public void Users_email_active_lower_uq_index_present_in_snapshot() { ... }

    [Fact]
    public void ProvisionUserHandler_symbol_is_gone() { ... }
}
```

M.13.4's migration + code-drops turn all four green.

### 5.2 Integration test — `UserBackfillMigrationTests`
Category=Integration. Seed shape:
- **Group A (survivor picking):** 3 users sharing `email='alice@example.com'`:
  - `A1` — `is_platform_admin=false`, 0 memberships, `created_at=T0`
  - `A2` — `is_platform_admin=true`, 0 memberships, `created_at=T1` (later)
  - `A3` — `is_platform_admin=false`, 2 memberships, `created_at=T2`
  - Expected survivor: A2 (PA beats everyone).
- **Group B (case-insensitive collapse):** 2 users, `alice@EXAMPLE.com` and `ALICE@example.com`.
- **FKs planted on non-survivors A1 and A3:** one row each in every FK-holder table incl. `messaging.messages`.
- **Distinct b2c_object_id per user:** A1 = `'oid-a1-guid'`, A2 = real-shape UUID, A3 = `'m13-placeholder-000...'`.

Assertions after running M.13.4:
1. Exactly 1 row with `email ILIKE 'alice@example.com'` AND `deleted_at IS NULL` — that row is A2.
2. A1 and A3 both have `deleted_at IS NOT NULL` and `deleted_by IS NULL`.
3. Every FK column above equals A2 (survivor id).
4. `identity.user_identities` rows: exactly 2 rows for A2. **NO row for A3's placeholder oid.**
5. `identity.migration_audit` has one row per step (13+ rows).
6. `_pre_m13_snap.users` has 5 rows.
7. `users_email_active_lower_uq` index exists and blocks a fresh insert of `'ALICE@example.com'`.

### 5.3 Additions to the plan's integration test
- Add `messaging.messages` rewrite assertion (F1).
- Add placeholder-oid exclusion (F3).
- Add ties-on-CreatedAt-fall-through-to-Id-ASC test.
- Add re-run idempotency test.

### 5.4 Fixture consideration — fresh-DB IF EXISTS guards
Cross-schema tables may not yet exist in test testcontainer when Identity's migration runs. **Wrap each cross-schema UPDATE with an `IF EXISTS` schema check** (per F11.7.7 pattern). Adds ~5 lines per FK-holder table.

---

## Section 6 — Sequencing recommendation

### 6.1 One commit vs. split
**Recommended: one commit.** Rationale:
- DDL (drop column, create partial unique) and DML (backfill) are semantically coupled.
- Single transaction gives clean rollback.
- Plan's §4 sub-commit structure treats M.13.4 as one commit. Confirm.

**Smallest CI-green step:** migration file + User.cs property drop + UserConfiguration.cs mapping removal + TenantSchemaMigrationTests.cs seed fix + new OpsM13_EmailCanonicalUsersShapeTests.cs file, all in one commit.

### 6.2 Code changes outside the migration
Confirming plan:
- `User.cs` — drop `B2CObjectId` property, drop `[Obsolete] Provision(b2cObjectId, ...)`, drop `ClaimOidForExistingProfile`, remove placeholder-set in email-first `Provision`.
- `UserConfiguration.cs` — remove `.HasColumnName("b2c_object_id")` and its unique index; add the partial-UNIQUE on lower(email) at the EF model level.
- `UserRepository.cs` — delete `GetByB2CObjectIdAsync`.
- `TenantSchemaMigrationTests.cs` — remove `b2c_object_id` column + literal from the two raw INSERT statements.
- `ICurrentUser.B2CObjectId` — **defer** rename to a follow-up commit; don't couple to the backfill.

---

## Section 7 — Deploy day-of runbook

### 7.1 Pre-deploy (30 min before CD job runs)
1. **Verify migrator role + credentials.** Run against staging DB:
   ```sql
   SELECT rolname, rolbypassrls FROM pg_roles WHERE rolname = 'vrbook_migrator';
   ```
   Expect: `t`. If `f`, halt.
2. **Take a manual pg_dump of the identity schema + FK-holder tables.**
3. **Snapshot current row counts** into a text file.

### 7.2 Deploy — normal path
CD kicks off. Migrator job runs. Because container-app job stdout does not reliably reach Log Analytics, do NOT rely on job logs. Poll the DB directly:

```sql
SELECT step_name, affected_count, executed_at, notes
  FROM identity.migration_audit
 WHERE migration_name = 'OpsM13_UserIdentities_And_EmailCanonical'
 ORDER BY executed_at;
```

Expected shape (~13-17 rows depending on Option a/b decision).

### 7.3 Diagnostics — how to read migration_audit
- **All rows present** → migration completed successfully.
- **No rows at all** → migration failed OR migrator job never ran.
- **Partial rows** → **should be impossible** given single-transaction guarantee.

### 7.4 Identify a partial-apply
`SELECT COUNT(*) FROM identity.users WHERE b2c_object_id IS NOT NULL` — should ERROR because the column is dropped.

### 7.5 Reverse a wrong survivor pick
Manual repick with FK-fixup + audit row insertion. Full SQL template in the runbook body.

### 7.6 Full rollback (worst case)
1. Take API offline.
2. Restore identity.users + FK columns from `_pre_m13_snap`.
3. Re-add `b2c_object_id` column, restore values, re-create the old unique index.
4. Drop `users_email_active_lower_uq`.
5. Delete M.13.4 row from `__EFMigrationsHistory`.
6. Re-deploy the pre-M.13.4 API image.

### 7.7 In-flight sign-in during migration (F7 mitigation)
**No data corruption from in-flight sign-ins**, thanks to M.13.2's partial UNIQUE already being in place. Small UX hit: any user signing in mid-migration might see a transient 500. Acceptable for staging.

### 7.8 Runbook checklist (deploy day)
- [ ] Pre-deploy: pg_dump captured + stored.
- [ ] Pre-deploy: row counts snapshotted.
- [ ] BYPASSRLS verified on `vrbook_migrator`.
- [ ] CD triggered.
- [ ] Post-run: query `identity.migration_audit` — expected rows present.
- [ ] Post-run: `SELECT COUNT(*) FROM identity.users WHERE deleted_at IS NULL` matches expected.
- [ ] Post-run: fresh sign-in as `niroshanaks@gmail.com` → resolves to survivor id.
- [ ] Post-run: walk (sign-in → pick tenant → confirm booking) passes.
- [ ] `_pre_m13_snap` schema retained.
- [ ] +30 days: schedule cleanup migration `OpsM13a_Drop_PreM13_Snapshot`.

---

## Appendix — Delta summary from plan §3.4

The exact SQL edits needed in M.13.4 vs. what the plan documents:

1. **Step 2** — Add `"Id" ASC` as final tiebreaker (F6).
2. **Step 3** — Add UPDATE for `messaging.messages.sender_user_id` and `messaging.messages.recipient_user_id` (F1).
3. **Step 3** — Decide + implement audit-column rewrites (`created_by`, `updated_by`, `deleted_by`) across every aggregate table (F1 Option a recommended).
4. **Step 3** — Wrap each UPDATE in a `WITH ... RETURNING` + `INSERT INTO identity.migration_audit` idiom to make Ev-F's "we don't know what happened" problem structurally impossible.
5. **Step 3** — Add `IF EXISTS (SELECT 1 FROM information_schema.tables ...)` guard around each cross-schema UPDATE for fresh-DB safety in tests (§5.4).
6. **Step 4** — Split into 4a (survivor identities) + 4b (non-survivor identities → survivor). Exclude `b2c_object_id LIKE 'm13-placeholder-%'` (F3).
7. **Step 5** — Add `deleted_by = NULL` explicitly (F5).
8. **Step 7** — Change to `CREATE UNIQUE INDEX IF NOT EXISTS` OR remove (F2 — M.13.2 already created it).
9. **All steps** — Every step emits a `migration_audit` row.

---

## Critical Files for Implementation

- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations/{ts}_OpsM13_CollapseEmailCanonical.cs` (NEW — the M.13.4 migration itself)
- `src/Modules/VrBook.Modules.Identity/Domain/User.cs` (drop `B2CObjectId` property, drop `[Obsolete] Provision(b2cObjectId, ...)`, drop `ClaimOidForExistingProfile`, remove `m13-placeholder-` assignment)
- `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/UserConfiguration.cs` (drop `b2c_object_id` mapping + old unique index; add EF-model partial unique on `lower(email) WHERE deleted_at IS NULL`)
- `tests/VrBook.Architecture.Tests/OpsM13_EmailCanonicalUsersShapeTests.cs` (NEW — the arch test the plan promised in M.13.1 but never landed; §5.1)
- `tests/VrBook.Api.IntegrationTests/Identity/UserBackfillMigrationTests.cs` (NEW — the seed-hazard-run-migration-assert integration test; §5.2)
- `tests/VrBook.Api.IntegrationTests/Identity/TenantSchemaMigrationTests.cs` (drop the `b2c_object_id` column + literal from the two raw INSERT statements)
