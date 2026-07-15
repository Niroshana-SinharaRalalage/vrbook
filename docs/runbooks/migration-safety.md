# Runbook — Migration safety (zero-downtime, forward-only)

> **Authoritative checklist** for every schema change (VRB-303). The go-live
> runbook links here — this is the single source of truth. Companion:
> [migrator-job-failure.md](migrator-job-failure.md) (what to do when the job fails)
> and [../ops/drills/migration-drill.md](../ops/drills/migration-drill.md) (the drills).

## The model (as-built, `src/VrBook.Migrator`)

- **Forward-only.** ~75 migrations across 10 module DbContexts. There is no
  `Down`/rollback path in the pipeline — recovery is **forward-fix** (a new
  migration) or **PITR restore** (VRB-304), never a down-migration.
- **Runs as a pre-deploy Container App Job** (`caj-vrbook-migrator-<env>`) that
  applies each context's pending migrations, then runs two idempotent backfills.
  On success it exits **0**; on any failure it logs `Fatal "Migrator failed"`
  and exits **1** (which must halt the deploy before the app revision promotes).
- **Identity migrations run first.** The migrator applies contexts in DI
  registration order (`Program.cs`: Identity → Catalog → Pricing → Booking →
  Payment → Reviews → Sync → Messaging → Loyalty → Notifications), so
  `identity.*` tables a cross-schema reference depends on already exist.
- **`HostAbortedException` is a *design-time* artifact only** (`dotnet ef` invoking
  the migrator to obtain a DbContext). The **runtime** job never runs the host,
  so it never throws it — do not treat a HostAbortedException seen locally during
  `dotnet ef migrations add` as a deploy failure.

## Pre-merge checklist — every PR that adds a migration

- [ ] **Expand/contract, split across releases.** A column add is
      **add (nullable) → backfill → enforce NOT NULL**, and each step ships in a
      **separate** release. Never ship a destructive/tightening change in the
      **same** release as the code that stops using the old shape — the previous
      app revision must keep working for the whole blue-green window.
- [ ] **No `DROP COLUMN` / `DROP TABLE` / `ALTER … NOT NULL` (without a prior
      backfill release) in a code-coupled release.** Drops come one release
      *after* the code that stopped reading the column.
- [ ] **No `RENAME`.** Model it as add-new + backfill + (later) drop-old.
- [ ] **Cross-schema references are `IF EXISTS`-guarded.** Any DDL/DML in one
      module's migration that touches another schema (`catalog.*`, `booking.*`, …)
      must guard with `IF EXISTS (SELECT 1 FROM information_schema.tables …)`.
      Identity runs first, but nothing else has a guaranteed order.
      (Burned twice — see `reference_cross_schema_migration_trap`.)
- [ ] **New index on a hot/large table uses `CREATE INDEX CONCURRENTLY`** (out of
      a transaction) so it doesn't lock writes during the deploy.
- [ ] **Backfill is idempotent and set-based**, safe to re-run (the job may retry).
- [ ] **Designer/snapshot regenerated via `dotnet ef migrations add`** — never
      hand-edited. Strip the UTF-8 BOM the tool emits (`reference_ef_migration_bom_charset`).
- [ ] **Migration ran locally against a Postgres** (not just built) — a build
      does not catch a bad SQL fragment.

## Pre-deploy checklist — the release

- [ ] **Connection string sets `Database=vrbook`.** `postgres-cs` in Key Vault
      must never be `Database=postgres` (the built-in system DB) — that silently
      creates ghost schemas. (`reference_postgres_db_must_be_vrbook`.)
- [ ] **`Bootstrap:E2e:Enabled=false` in prod.** Verified as-built: Bicep
      `bootstrapE2eTenantEnabled = env == 'staging'`, so prod resolves `false` and
      `SeedE2EBackfill` is a no-op — the `is_e2e=true` marker never appears on a
      prod tenant. Do not override this in `prod.bicepparam`.
- [ ] **Any KV secret a new migration/env-var needs is seeded *before* the
      deploy** (the job can't start otherwise; `reference_kv_secret_bind_before_deploy`).
- [ ] **Migrator Job exits 0** before the api/web revision promotes. A non-zero
      exit halts the deploy → follow [migrator-job-failure.md](migrator-job-failure.md).
- [ ] **The `alert-vrbook-<env>-migrator-fail` alert is armed** (VRB-306) so a
      failed job pages the on-call.

## Reference-data seeding (idempotent, declarative)

- **Platform admins** — `Bootstrap:SeedPlatformAdmins` (Bicep array) →
  `SeedPlatformAdminsBackfill`: an active row per email is inserted once; a second
  run finds the existing row and no-ops (also writes `identity.migration_audit`).
- **Amenities (25)** — shipped as a data migration; re-running the migrator does
  not duplicate them.
- **E2E fixture** — `SeedE2EBackfill`, staging-only (above); no-op elsewhere.

Re-running the whole migrator on an already-migrated, already-seeded DB is a
**no-op** by construction — this is the property the idempotency drill asserts.

## Recovery

Forward-only means **no down-migrations**. Two recovery paths:

1. **Forward-fix (preferred for a logic error):** write a new migration that
   corrects the bad state, deploy it. Fast, no data loss. See the forward-fix
   drill.
2. **PITR restore (for a destructive/partial failure):** restore the DB to the
   pre-migration timestamp. The mechanics + RPO/RTO live in **VRB-304** (the PITR
   substrate); this runbook references it rather than duplicating it.
