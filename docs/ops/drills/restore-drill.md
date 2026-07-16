# Drill — Postgres backup / restore / DR (RPO ≤1h, RTO ≤4h)

> **VRB-304.** Prove the Postgres backups are *restorable* end-to-end, not just
> configured — a data-loss incident must be recoverable within **RPO ≤1h** and
> **RTO ≤4h**, measured, not hoped. Closes **G25**.
>
> **Execution status: DEFERRED — procedure authoritative, not yet run.**
> Running this is a **write** operation (create a restored server, repoint an
> app revision, tear it down) and needs **owner Azure-write** access
> (Contributor on the RG, or a scoped Postgres restore/create/delete role) —
> a bigger grant than the read-only Reader SP. This doc is ready to execute the
> moment access lands; timings go in the **Runs** table and VRB-304 flips DONE
> once RTO/RPO are measured within target. **Never overwrite the live server —
> always restore to a NEW throwaway server.**

## Backup posture (as-built)

| Env | Retention | Geo-redundant | Notes |
|-----|-----------|---------------|-------|
| dev | 7 days | Disabled | local/ephemeral |
| staging | 7 days | Disabled | single-region; drill target until prod exists |
| prod | 35 days | **Enabled** (VRB-304 Bicep) | paired-region geo-restore for DR |

`main.bicep`: `backupRetentionDays = isProd ? 35 : 7`; `geoRedundantBackup =
isProd ? 'Enabled' : 'Disabled'` (VRB-304). Postgres Flexible Server keeps
continuous (WAL) backups enabling **point-in-time restore (PITR)** to any second
within the retention window — that is what makes RPO ≤1h achievable (worst-case
data loss = time since the last log flush, seconds-to-minutes).

---

## Drill A — PITR restore (measures RTO + RPO)

Run against **staging** (`psql-vrbook-staging-v2` in `rg-vrbook-staging`) until
prod exists; re-run once against a **restored copy** of prod pre-launch (never
against prod itself).

1. **Start the stopwatch** ("decide to restore") and pick a restore point ≤1h
   old (validates RPO — the further back you must go, the larger the RPO):
   ```bash
   RESTORE_TIME=$(date -u -d '-10 minutes' +%Y-%m-%dT%H:%M:%SZ)   # a recent point
   ```
2. **Trigger PITR to a NEW server** (never overwrite the source):
   ```bash
   az postgres flexible-server restore \
     --resource-group rg-vrbook-staging \
     --name psql-vrbook-staging-restore-drill \
     --source-server psql-vrbook-staging-v2 \
     --restore-time "$RESTORE_TIME"
   ```
3. **Repoint a throwaway app revision** at the restored server — update the KV
   `postgres-cs` value (keep `Database=vrbook`!) for a scratch revision, or set
   the connection string as a revision env override, and restart:
   ```bash
   # scratch revision only — do NOT repoint the live revision's KV secret
   az containerapp revision copy -n ca-vrbook-api-staging -g rg-vrbook-staging \
     --set-env-vars "ConnectionStrings__Postgres=Host=psql-vrbook-staging-restore-drill.postgres.database.azure.com;Database=vrbook;Username=vrbook_admin;Password=<from-KV>;Ssl Mode=Require"
   ```
4. **Smoke the restored DB** — the public + an authed path:
   ```bash
   curl -fsS "https://<scratch-revision-fqdn>/api/v1/properties?limit=1" >/dev/null && echo "public OK"
   ```
   and verify row counts match expectations (see Verification below).
5. **Stop the stopwatch** the moment the app serves on the restored DB — that
   wall-clock is the **measured RTO**. The gap between `RESTORE_TIME` and the
   incident point is the **measured RPO**.
6. **Tear down** the drill server + scratch revision:
   ```bash
   az containerapp revision deactivate -n ca-vrbook-api-staging -g rg-vrbook-staging --revision <scratch>
   az postgres flexible-server delete --yes -g rg-vrbook-staging -n psql-vrbook-staging-restore-drill
   ```

**Pass:** app serves on the restored DB with correct data; **measured RTO ≤ 4h**
and **measured RPO ≤ 1h**; both recorded in the Runs table.

### Verification queries (restored DB)
```sql
SELECT
  (SELECT count(*) FROM identity.users)     AS users,
  (SELECT count(*) FROM catalog.properties) AS properties,
  (SELECT count(*) FROM booking.bookings)   AS bookings,
  (SELECT max(created_at) FROM booking.bookings) AS newest_booking;  -- confirms restore point
```
`newest_booking` at/after the incident timestamp confirms the RPO window.

---

## Drill B — Geo-restore (region-loss DR) — **documented, not drilled**

For a full region loss, restore from the **geo-redundant** backup into the
**paired region** (prod only — `geoRedundantBackup=Enabled`):
```bash
az postgres flexible-server geo-restore \
  --resource-group rg-vrbook-prod \
  --name psql-vrbook-prod-geo \
  --source-server psql-vrbook-prod \
  --location <paired-region>
```
Then repoint DNS/app to the paired-region stack. **Not fully drilled** (needs
the prod geo backup to exist = VRB-301) — called out here per the story;
geo-RTO will exceed single-region PITR RTO and should be measured at prod DR day.

---

## Runs

| Date | Env | Drill | Measured RTO | Measured RPO | Result | Notes |
|------|-----|-------|--------------|--------------|--------|-------|
| _pending_ | staging | A · PITR | — | — | — | deferred: awaiting owner Azure-write |
| _n/a_ | prod | B · geo-restore | — | — | documented-not-drilled | needs prod geo backup (VRB-301) |
