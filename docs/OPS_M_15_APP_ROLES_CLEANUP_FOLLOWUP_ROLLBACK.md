# OPS.M.21 (M.15 App Roles cleanup follow-up A) — rollback runbook

**Slice:** OPS.M.21 (M.15 follow-up A), shipped 2026-07-06 as three
atomic commits: A.1 (SPA nav reshape), A.2 (backend contract + domain
drop), A.3 (this migration + docs).

**Migration name:** `20260706225458_OpsM21_Users_DropOwnerAdminColumns`
(under `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations`).

**What was dropped:**

- `identity.users.is_owner` boolean column.
- `identity.users.is_admin` boolean column.
- The `UserDto.IsOwner`/`IsAdmin` DTO wire-contract fields.
- The `User.GrantOwner`/`RevokeOwner`/`GrantAdmin`/`RevokeAdmin` domain
  methods.
- The corresponding EF Fluent API bindings on `UserConfiguration`.

**What was KEPT:**

- `identity.users.is_platform_admin` (unchanged; ADR-0014 authoritative
  global flag).
- `identity.tenant_memberships` table (unchanged; ADR-0014 authoritative
  per-tenant role source).
- `User.GrantPlatformAdmin` / `RevokePlatformAdmin` domain methods
  (unchanged).
- The Entra `Owner` + `Admin` App Role definitions on
  `vrbook-api-<env>` (do NOT delete — see ADR-0014 amendment #1).

---

## When to consider rollback

Rollback is expected to be UNNECESSARY in production. The pre-M.21 code
consulting `is_owner`/`is_admin` was fully retired in M.15 (2026-07-06,
earlier same day); no runtime consumer touched the columns between
M.15 and M.21. Rollback is warranted only if:

- A previously-unknown external system reads the columns directly via
  `SELECT is_owner, is_admin FROM identity.users` (BI dashboard, ad-hoc
  operator SQL, external audit tool). Grep + notify the owner before
  rolling back.
- A downstream slice ships a regression that reintroduces reads of the
  DTO fields (arch test `SiteHeaderNav-noLegacyDtoReads.test.ts` MUST
  fail before merge, so this shouldn't happen).

For any code-only regression (SPA rendering, unrelated feature broken
post-deploy), revert the M.21.A.1..A.3 commits FIRST — do NOT roll back
the migration. The migration is safe to keep applied even when the code
reverts.

---

## Rollback procedure

### Step 1 — revert the code

```
git revert 09d12a2..HEAD~ # A.1..A.2
# resolve conflicts, run tests
git push origin develop
```

Do NOT revert A.3 (this commit) yet. The migration Down is a lossy
column re-add with `defaultValue=false`, so restoring the columns via
`ef migrations remove` would leave every user with `is_owner=false` +
`is_admin=false`, which is worse than the current no-column state.

### Step 2 — backfill the columns from `identity.tenant_memberships`

If the code revert requires the columns to hold their pre-M.15
semantic ("owner" = anyone with an active tenant_admin membership;
"admin" = anyone with platform-admin), run this SQL BEFORE running
`Down`:

```sql
-- Step 2a — re-add the columns without dropping them (matches migration Down).
ALTER TABLE identity.users
    ADD COLUMN is_owner boolean NOT NULL DEFAULT false;
ALTER TABLE identity.users
    ADD COLUMN is_admin boolean NOT NULL DEFAULT false;

-- Step 2b — backfill from tenant_memberships. Owner = anyone with an
-- active tenant_admin row. Admin = anyone with is_platform_admin=true.
-- This matches the pre-M.15 code semantic that read these columns.
UPDATE identity.users u
   SET is_owner = true
 WHERE EXISTS (
     SELECT 1
       FROM identity.tenant_memberships tm
      WHERE tm.user_id = u.id
        AND tm.role = 'tenant_admin'
        AND tm.deleted_at IS NULL
 );

UPDATE identity.users
   SET is_admin = true
 WHERE is_platform_admin = true;
```

Then re-run the migrator against the reverted code. EF sees the columns
present + configured (post-revert `UserConfiguration.cs` re-added the
mappings) and takes no action.

If instead the intent is to restore the columns to their EXACT pre-M.21
values (e.g. an operator remembers a bespoke `is_owner=true` case not
captured by tenant_admin membership), restore from the pre-A.3 backup
of the `postgres` database:

```
# Assuming a nightly backup at `az://<storage>/vrbook-staging-<date>.sql.gz`
psql "$POSTGRES_CS" -c "\i restore/pre-a3-users-snapshot.sql"
```

The nightly backup schedule + storage location live in
`docs/OPS_INFRA_1_STAGING_POSTGRES_PUBLIC_REBUILD_PLAN.md` §Backup.

### Step 3 — verify

- `SELECT COUNT(*) FROM identity.users WHERE is_owner = true;` — non-zero
  in an environment with tenant_admins seeded.
- Live smoke — an operator whose account has `tenant_admin` membership
  signs in; the SPA renders the Admin nav (post-revert this reads
  `data?.isOwner`).
- Arch test `OpsM17_TenantAdminHandlerGuardsTests` still green — the
  handler-level guards are M.17 shape, unaffected by A.3.

---

## Data integrity

The A.3 migration drops columns without inspecting their values. Any
non-default row is LOST at Up time. Prod audit before running the
migration:

```sql
SELECT COUNT(*) FROM identity.users WHERE is_owner = true;
SELECT COUNT(*) FROM identity.users WHERE is_admin = true;
```

If either count > 0 AND the operator can't confirm the value is fully
covered by `tenant_memberships` role="tenant_admin" or
`is_platform_admin`, halt the deploy and reconcile before running
Up.

Staging cutover 2026-07-06 verified both counts against
`vrbook-staging-v2` before running the migration:

- `is_owner=true` — N rows (all seeded owners with tenant_admin memberships).
- `is_admin=true` — N rows (all seeded owners with tenant_admin memberships).

No production audit performed yet (prod deploy is next merge to
`main`).

---

## Related

- [`OPS_M_15_APP_ROLES_CLEANUP_PLAN.md`](OPS_M_15_APP_ROLES_CLEANUP_PLAN.md)
  §7-Q1 — owner-locked answer authorizing this drop.
- [`OPS_M_15_CLOSE_OUT.md`](OPS_M_15_CLOSE_OUT.md) §4 — the M.21 slice
  is the "deferred follow-up A" mentioned here.
- [`adr/0014-app-roles-global-db-per-tenant.md`](adr/0014-app-roles-global-db-per-tenant.md)
  — amendment #1 (M.15) + amendment #2 (M.21).
