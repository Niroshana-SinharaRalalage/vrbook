# Runbook — DB migrator Job failure

> Severity: **Sev1** → on-call. Alert: `alert-vrbook-<env>-migrator-fail` (VRB-306).
> The migrator runs as a manual/deploy-triggered Container App Job
> (`caj-vrbook-migrator-<env>`). A failed migration usually means the latest
> deploy is **half-applied** — treat as a release blocker.

## What the alert means

The migrator logged an `Error`/`Fatal` from `VrBook.Migrator` or
`Microsoft.EntityFrameworkCore` that is **not** the expected design-time
`HostAbortedException`. That abort is normal (the migrator uses the EF design-time
host pattern and always aborts after applying migrations) and is filtered out of
the alert query — so a fire is a *real* migration error, not the benign abort.

## First 5 minutes

1. Portal → Container Apps Jobs → `caj-vrbook-migrator-<env>` → **Execution history**; open the latest failed execution.
2. Log Analytics — pull the failing run's logs:
   ```kql
   ContainerAppConsoleLogs_CL
   | where TimeGenerated > ago(1h)
   | where Log_s startswith '{"@t"'
   | extend e = parse_json(Log_s)
   | where tostring(e.SourceContext) startswith "VrBook.Migrator"
        or tostring(e.SourceContext) startswith "Microsoft.EntityFrameworkCore"
   | where tostring(e['@l']) in ("Error", "Fatal")
   | where tostring(e['@mt']) !contains "HostAborted"
   | project TimeGenerated, level = tostring(e['@l']), msg = tostring(e['@m']), ex = tostring(e.ExceptionDetail)
   | order by TimeGenerated desc
   ```
3. Confirm the **exact `@mt`/exception shape** during the first drill — if the
   real failure log differs from the filter above, tune the alert query (this
   query is the compensating control until a live migrator failure is observed).

## Likely causes & fixes

- **Migration references a schema/table not yet created** (cross-schema ordering) — Identity migrations run first; guard `catalog.*`/`booking.*` references with `IF EXISTS (...)`. See [`reference_cross_schema_migration_trap`].
- **Connection string points at the wrong database** (`Database=postgres` instead of `Database=vrbook`) — the whole run fails atomically. Verify `postgres-cs` in Key Vault sets `Database=vrbook`.
- **A KV-referenced secret was not seeded before the deploy** — the job can't start. Seed the secret, then re-run the job.
- **Genuine data/constraint conflict** (e.g. a backfill that violates a FK) — fix the migration/backfill, re-deploy.

## Remediation

1. Fix the root cause (migration, connection string, or secret).
2. Re-run the migrator job:
   ```bash
   az containerapp job start -n caj-vrbook-migrator-<env> -g rg-vrbook-<env>
   ```
3. Confirm success: the run ends with the design-time abort and **no** non-benign error; the API comes up healthy.

## Escalation

- If the failure leaves the DB half-migrated and the API is down, this is a **release blocker** — hold the deploy, page the owner, and consider restoring from the last good backup if a destructive migration partially applied (backup/restore is untested — see gap G25).
