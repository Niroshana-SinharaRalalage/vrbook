# Drill — DB migration safety (migration · forward-fix · idempotency)

> **VRB-303.** Three rehearsals that prove a schema change deploys without
> downtime, recovers when it fails, and is safe to re-run. Discipline they
> enforce lives in [../../runbooks/migration-safety.md](../../runbooks/migration-safety.md).
>
> **Execution status (2026-07-15): DEFERRED — procedure authoritative, not yet run.**
> Running these needs read access to the staging Azure resources (to trigger the
> migrator job and observe revisions/alerts). That access is the **same owner
> decision** parked for the VRB-306 alert drills. This document is ready to
> execute the moment access lands; results get recorded in the "Runs" table below
> and VRB-303 flips to DONE with VRB-306.

## Environment

Run against **staging** (`rg-vrbook-staging`) — ideally against a
**PITR-restored copy** of prod-shaped data once VRB-304 provides the restore
substrate; until then, staging itself is the target. Never drill against prod.

Prereqs: staging az login (Contributor on the RG, or Reader + job-start), the
migrator job `caj-vrbook-migrator-staging`, the API app `ca-vrbook-api-staging`.

---

## Drill 1 — Migration drill (zero-downtime assertion)

**Goal:** pending migrations apply while the *current* app revision keeps serving.

1. Pick a branch with ≥1 pending migration (or craft an expand-only test migration).
2. Start a continuous smoke against the **live** revision and leave it running:
   ```bash
   while true; do
     code=$(curl -s -o /dev/null -w '%{http_code}' \
       "https://<api-fqdn>/api/v1/properties?limit=1")
     echo "$(date -u +%H:%M:%S) $code"; sleep 2
   done
   ```
3. Trigger the migrator job (as the pre-deploy step does):
   ```bash
   az containerapp job start -n caj-vrbook-migrator-staging -g rg-vrbook-staging
   az containerapp job execution list -n caj-vrbook-migrator-staging -g rg-vrbook-staging -o table
   ```
4. Let the new app revision deploy/activate.

**Pass:** the smoke shows **only 200s** across the whole window (migration +
revision swap) — no 5xx, no gap. The migrator execution succeeds (exit 0). This
holds **only** if the migration is expand-only (the checklist guarantees it); a
tightening change in a code-coupled release is exactly what this drill would
catch as a blip.

---

## Drill 2 — Forward-fix drill (failed migration → recovery)

**Goal:** a failing migration halts the deploy loudly, and a forward-fix recovers.

1. On a throwaway branch, add a deliberately-failing migration (e.g. reference a
   non-existent column, or a cross-schema statement **without** the `IF EXISTS`
   guard) so the migrator throws.
2. Trigger the migrator job. **Expect:** execution **fails** (non-zero exit); the
   logs carry `Fatal "Migrator failed"`; the app revision is **not** promoted.
   ```kql
   ContainerAppConsoleLogs_CL
   | where TimeGenerated > ago(30m)
   | where Log_s startswith '{"@t"'
   | extend e = parse_json(Log_s)
   | where tostring(e['@l']) in ("Error","Fatal")
   | where tostring(e.SourceContext) startswith "VrBook.Migrator"
        or tostring(e.SourceContext) startswith "Microsoft.EntityFrameworkCore"
   | project TimeGenerated, level=tostring(e['@l']), msg=tostring(e['@m'])
   ```
3. **Expect:** `alert-vrbook-staging-migrator-fail` (VRB-306) fires and pages the
   on-call within its window.
4. **Recover forward:** replace the bad migration with a corrected one, re-trigger
   the job, confirm exit 0 and a healthy revision.

**Pass:** failure is non-silent (exit 1 + Fatal log + alert + deploy halted), and
the forward-fix restores a consistent DB with no down-migration.

---

## Drill 3 — Backfill idempotency drill

**Goal:** re-running the migrator changes nothing (safe on every deploy/retry).

1. Snapshot counts before:
   ```sql
   SELECT
     (SELECT count(*) FROM identity.users)                        AS users,
     (SELECT count(*) FROM identity.users WHERE is_platform_admin) AS admins,
     (SELECT count(*) FROM catalog.amenities)                     AS amenities,
     (SELECT count(*) FROM identity.tenants WHERE is_e2e)         AS e2e_tenants;
   ```
2. Run the migrator job **twice** back-to-back (no code change between runs).
3. Re-run the count query.

**Pass:** every count is **identical** across the two runs — `SeedPlatformAdmins`
finds existing rows and no-ops, amenities don't duplicate, and (staging) the E2E
fixture is idempotent. Any delta is a non-idempotent backfill and a bug.

---

## Runs

| Date | Env | Drill | Result | Notes |
|------|-----|-------|--------|-------|
| 2026-07-16 | staging | 1 · migration (zero-downtime) | ✅ observed | `cd-staging-api` run **29466376892** (SHA 722a9ba): `migrate (Postgres)` → `deploy api` → `smoke (api)` all **success**, in that order — the migrator ran to completion **before** the API revision promoted (old revision served throughout = zero-downtime by construction) and post-deploy smoke passed. Direct GitHub Actions evidence, no Azure access needed. |
| _n/a_ | staging | 2 · forward-fix | documented-deferred | Needs a **write** action (deploy a deliberately-failing migration); procedure above is authoritative, live run deferred per TL. |
| _n/a_ | staging | 3 · idempotency | verified-by-design | Confirmed in code (read-only): `SeedPlatformAdminsBackfill` upserts-by-email + no-ops on existing; `SeedE2EBackfill` gated off in prod; both run post-migration. Live twice-run deferred (needs `job start`, a write). |
