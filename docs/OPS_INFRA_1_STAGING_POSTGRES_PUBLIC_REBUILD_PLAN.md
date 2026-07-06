# Slice OPS.INFRA.1 — Blue/green rebuild of staging Postgres as public-access server

- **Status:** SHIPPED end-to-end 2026-07-05 (rev 2.b). API cutover live on `psql-vrbook-staging-v2`; old server deleted; Bicep + `staging.bicepparam` reconciled to post-cutover shape. Two empirical surprises during execution (A8 + A10) documented below.
- **Date:** 2026-07-05 (rev 2.b — retrospective + A10 added; rev 2.a locked Option A; rev 2 authored blue/green; rev 1 straight-line was superseded).
- **Owner-approved intent:** rebuild staging Postgres with `publicNetworkAccess: Enabled` + IP-firewalled access matching LankaConnect posture. Prod stays private.
- **Failure mode being avoided (rev 2):** the straight-line plan (delete → deploy → migrate) leaves staging with no DB for hours if the Bicep deploy fails. Rev 2 keeps `psql-vrbook-staging` alive until the replacement is proven end-to-end.
- **Prior analysis:** scoping agent proved 2026-07-05 that public-vs-private is fixed at server-create time — no in-place toggle. Rebuild is the only path.
- **Executes BEFORE OPS.M.12** so M.12's REFUSE-AT-PROVISIONING branch can be verified by direct `user_identities` inspection during the smoke walk.

---

## §1 Strategy: two servers coexist during cutover

Old server (`psql-vrbook-staging`, VNet-private) keeps serving container-app + migrator traffic through Key Vault secret `postgres-cs`. In parallel we stand up `psql-vrbook-staging-v2` (public-access, IP-allowlisted). We verify V2 end-to-end via a shadow migrator run + DBeaver + a temporary secondary connection-string secret. Only after all three checks pass do we flip the primary `postgres-cs` secret and restart the container apps. The old server is deleted last.

Coexistence is possible because:
- Server name is different (`psql-vrbook-staging-v2`), so no ARM name conflict.
- Old server is VNet-private, new server is public + firewalled — they share nothing but the resource group.
- `postgres-cs` is a KV secret populated **manually** post-Bicep (verified 2026-07-05 by grep — `infra/scripts/10-store-secrets.ps1` line 69 seeds it with the placeholder `'pending-bicep-deploy'` and `main.bicep` never overwrites it). So Bicep can deploy V2 without touching the live connection string.

**Assumptions flagged for empirical verification before execution (§9).**

---

## §2 What ships in code (single commit)

Touches:

1. **`infra/modules/postgres-flexible.bicep`** — add `serverNameOverride string = ''` parameter. When empty, current behavior (`psql-vrbook-${env}`). When set, uses the override. This is the ONLY module change; the public-access + firewall params already landed in `c1cc693`.
   - Alternative considered and rejected: a duplicate `postgres-flexible-blue-green.bicep` module. Rejected because it forks the SKU/HA/backup logic and leaves prod exposed to drift.
2. **`infra/main.bicep`** — add a **temporary** second `pg` module invocation gated by a new param `deployStagingPgV2 bool = false`:
   - Same params as the existing `pg` module (SKU/tier/storage/backup/HA), but `serverNameOverride: 'psql-vrbook-staging-v2'`, `publicNetworkAccess: 'Enabled'`, `firewallRules: pgFirewallRules`.
   - The existing `pg` module KEEPS its current shape (private, name `psql-vrbook-staging`) until §3 step 10.
   - Outputs `postgresV2Fqdn string = deployStagingPgV2 ? pgV2.outputs.fqdn : ''`.
3. **`infra/environments/staging.bicepparam`** — flip `deployStagingPgV2 = true` for the deploy that creates V2. After cutover (§3 step 10) we remove the temporary module + param.
4. **Key Vault secret `postgres-cs-v2`** — NEW temporary secret, populated manually after V2 exists (§3 step 5). The Bicep does not create it; the operator writes it via `az keyvault secret set`. Deleted after cutover.
5. **No app-code changes.** `apiEnvVars` / `apiSecrets` in `main.bicep` continue to reference `postgres-cs`; the cutover happens by updating that KV secret's value in-place at step 8.

Post-cutover cleanup commit (§3 step 12) removes the temporary `pgV2` module invocation + `deployStagingPgV2` param + `serverNameOverride` on the old `pg` invocation, and re-enables `publicNetworkAccess: 'Enabled'` on the primary `pg` module invocation with `serverNameOverride: 'psql-vrbook-staging-v2'` so subsequent deploys are idempotent against the promoted server.

---

## §3 Execution sequence (12 steps)

Each step lists: (a) command / action, (b) what proves it worked, (c) what a failure looks like.

### Step 1 — Owner-side prep + backup snapshot of the live server

- Confirm owner's current outbound IP is `174.104.204.213` (already in `infra/main.bicep` `pgFirewallRules`). If it has changed, update in the same commit.
- From Azure Cloud Shell (or a VM inside `vnet-vrbook-staging`), take a `pg_dump` of the current server:
  ```
  pg_dump --host=psql-vrbook-staging.postgres.database.azure.com --username=vrbook_admin --dbname=vrbook --format=custom --file=/home/$USER/vrbook-preinfra1-$(date +%Y%m%d).dump
  ```
  Persist the dump to a private blob (`scripts/staging-seed/` is source-controlled — do NOT check the dump in; owner-approved: test data is expendable, but keep the dump in a temp KV secret or private blob for 7 days).
- **Success:** dump file created + size > 0.
- **Failure:** if the private server is unreachable from Cloud Shell, the VNet has drifted; investigate before proceeding. Data loss on rebuild is acceptable per owner, so this step is belt-and-braces — do NOT block on it.

### Step 2 — Land the Bicep change (Commit A)

- Edit `infra/modules/postgres-flexible.bicep`: add `param serverNameOverride string = ''` and change `var serverName = empty(serverNameOverride) ? 'psql-vrbook-${env}' : serverNameOverride`.
- Edit `infra/main.bicep`: add `param deployStagingPgV2 bool = false` and a conditional second module `pgV2` (see §2 item 2). The existing `pg` module invocation is UNCHANGED so the live server keeps deploying idempotently in its current form.
- Edit `infra/environments/staging.bicepparam`: add `param deployStagingPgV2 = true`.
- `az deployment group what-if -g rg-vrbook-staging -f infra/main.bicep -p infra/environments/staging.bicepparam` — expected diff: **create** `psql-vrbook-staging-v2` (+ its `firewallRules/*` + `configurations/require_secure_transport`); **no change** to `psql-vrbook-staging`.
- **Success:** `what-if` shows exactly one net-new server + expected child resources, zero modifications to the primary Postgres.
- **Failure:** unexpected diff on the primary server → stop, do NOT run `az deployment group create`. Investigate what changed (likely an API-version drift or a shared param that got pulled into both modules).

### Step 3 — Deploy V2 alongside the live server

- `az deployment group create -g rg-vrbook-staging -f infra/main.bicep -p infra/environments/staging.bicepparam`.
- Old server keeps serving; ARM stands up the new one in parallel (Postgres Flex Server provisioning takes ~10 min).
- **Success:** `az postgres flexible-server show -n psql-vrbook-staging-v2 -g rg-vrbook-staging --query state -o tsv` returns `Ready`. Old server still `Ready` too. `az postgres flexible-server list -g rg-vrbook-staging -o table` shows both.
- **Failure (the mode we set out to prevent):** V2 creation errors out (quota, SKU regional unavailability, Bicep syntax, transient Azure). Old server untouched → staging still healthy. Fix the Bicep or file a quota ticket, re-run step 3. **No rollback of anything else needed at this point.**

### Step 4 — Verify V2 is reachable from owner's laptop (DBeaver check #1)

- DBeaver → new connection: `psql-vrbook-staging-v2.postgres.database.azure.com`, port 5432, DB `vrbook`, user `vrbook_admin` (verify A1), password from KV `postgres-admin-password` (unchanged — both servers share the admin credential because it's a Bicep param sourced from the same KV secret).
- Confirm connection succeeds. The DB will be empty (no databases, no schemas — Postgres Flex Server ships a default `postgres` DB but not `vrbook`).
- Create the `vrbook` database: `CREATE DATABASE vrbook;` (the migrator does NOT create the DB, only migrations within it).
- **Success:** DBeaver connects, `\l` shows `postgres` + `vrbook` databases.
- **Failure:** firewall not letting owner IP through → check `az postgres flexible-server firewall-rule list -n psql-vrbook-staging-v2 -g rg-vrbook-staging`. Owner IP may have rotated; update `pgFirewallRules` in `main.bicep` + redeploy.

### Step 5 — Populate temporary secondary KV secret `postgres-cs-v2`

- `az keyvault secret set --vault-name kv-vrbook-staging --name postgres-cs-v2 --value 'Host=psql-vrbook-staging-v2.postgres.database.azure.com;Database=vrbook;Username=<verified-admin-login>;Password=<from-kv-postgres-admin-password>;SSL Mode=Require;Trust Server Certificate=true'`
- Verify username via A1 (`az postgres flexible-server show --query administratorLogin`) — match V2 to whatever the live server actually uses.
- **Success:** `az keyvault secret show --vault-name kv-vrbook-staging --name postgres-cs-v2 --query value -o tsv` returns the connection string.
- **Failure:** KV RBAC missing for operator → fix the role assignment, retry. This does not affect running apps.

### Step 6 — Run the migrator LOCALLY against V2 (shadow phase, pre-cutover)

**Path decision (rev 2.a, 2026-07-05):** the container-app-job env-var override approach is dropped. `az containerapp job update` has no `--secrets` flag (A2 blocker); the YAML export/patch/apply workaround gets reconciled away on the next CD deploy of `infra/main.bicep` (`container-app-job.bicep:129-146` does full-property replace on `configuration.secrets` and `template.containers[0].env` from `apiSecrets`/`apiEnvVars`), so the shadow test would validate a state that never sees production traffic. Option A local-run wins by simpler execution + zero infra state to revert; the container-app-job path is exercised post-cutover under §9-A8.

Run the migrator locally from the operator workstation against V2. The migrator (`src/VrBook.Migrator/Program.cs`) reads `ConnectionStrings__Postgres` from env vars and V2 is public-network + operator IP is in `pgFirewallRules` per §3 step 3 — no Azure infra plumbing needed for this shadow pass.

```bash
# 6.a — Fetch admin password from KV (same value on V1 + V2 per A7).
PGPWD=$(az keyvault secret show \
    --vault-name kv-vrbook-staging \
    --name postgres-admin-password \
    --query value -o tsv)

# 6.b — Point migrator at V2. Note: username is vrbook_admin per A1.
export ConnectionStrings__Postgres="Host=psql-vrbook-staging-v2.postgres.database.azure.com;Database=vrbook;Username=vrbook_admin;Password=${PGPWD};SSL Mode=Require;Trust Server Certificate=true"
export DOTNET_ENVIRONMENT=Staging

# 6.c — Run the exact code path the container-app-job would.
dotnet run --project src/VrBook.Migrator --configuration Release
```

Expected: one "Migrating {DbContext}" / "Migrated" line pair per module DbContext (Identity, Catalog, Pricing, Booking, Payment, Reviews, Sync, Messaging, Loyalty, Notifications — 10 contexts), then "Migrator complete." Exit code 0.

- **Success:** exit 0. Connect via DBeaver → `\dn` shows module schemas; `__EFMigrationsHistory` in each schema has all migrations including `20260705034250_OpsM16_Booking_CompletionDueAt`.
- **Failure:** connection times out / firewall reject (SQLSTATE 28000, "no pg_hba.conf entry for host") → operator IP allowlist rule from §3 step 3 didn't take. Diagnose that first: `az postgres flexible-server firewall-rule list -n psql-vrbook-staging-v2 -g rg-vrbook-staging`. Fix + retry. Migration-time errors → V2 is broken but V1 still serving; drop `psql-vrbook-staging-v2` via `az postgres flexible-server delete` and restart from step 3 after fixing the migration bug.

### Step 7 — End-to-end validation of V2 via a shadow API smoke (optional but cheap)

- Do NOT redirect real traffic. Instead, from DBeaver, hand-insert a canary row: `INSERT INTO identity.users(...) VALUES(...);` then `SELECT` it back. This proves DDL + DML + SSL work against V2.
- Skip if step 6 succeeded — the migrator's own DDL is a stronger proof than a hand-INSERT.
- **Success:** row survives round-trip.
- **Failure:** unlikely if step 6 passed; escalates to §5 rollback R7.

### Step 8 — Cutover: flip primary `postgres-cs` to point at V2

This is the moment of downtime. Aim for < 2 min.

**PRE-STEP (non-negotiable):** capture current `postgres-cs` value for R8 rollback:
```
az keyvault secret show --vault-name kv-vrbook-staging --name postgres-cs --query value -o tsv > /tmp/postgres-cs-preexist.txt
```
Do NOT commit this file.

- Restore the migrator job's env-var (undo step 6's temporary override) so it once again reads `ConnectionStrings__Postgres=secretref:postgres-cs`. This ensures the next CD run picks up V2 via the primary secret, not the temporary one.
- `az keyvault secret set --vault-name kv-vrbook-staging --name postgres-cs --value '<same value as postgres-cs-v2>'`
- Container Apps cache KV secret values per revision (A4). To make the API re-read: bump revision:
  ```
  az containerapp update -n ca-vrbook-api-staging -g rg-vrbook-staging \
      --revision-suffix cutover-$(date +%s)
  ```
- Container App Jobs (migrator, sync, bookingexpiry, completion, notifdispatch) resolve `secretref:postgres-cs` at execution start, so they auto-cutover on next cron/manual trigger — no bump needed.
- **Success:** `az containerapp revision list -n ca-vrbook-api-staging -g rg-vrbook-staging -o table` shows the new revision `Active` + `Running`. API health endpoint returns healthy. App Insights: no Npgsql exceptions in a 2-min window.
- **Failure:** API cold-start fails against V2 → immediately roll back per §5 R8.

### Step 9 — Post-cutover verification burn-in (15 min)

- Trigger the migrator job manually: `az containerapp job start -n caj-vrbook-migrator-staging -g rg-vrbook-staging` — expected: no-op (all migrations already applied in step 6).
- Sign in through the web UI (owner's `niroshanaks@gmail.com` Entra flow). This provisions the user via M.13's identity flow → hits V2 → completes.
- Run the post-first-sign-in bootstrap in DBeaver:
  ```sql
  UPDATE identity.users SET is_platform_admin = true WHERE lower(email) LIKE '%niroshanaks%';
  ```
- Walk one property + one booking flow (create/list) → confirms DDL + DML + reads work against V2 through the running containers.
- Watch App Insights for 15 min: zero DB-connection failures.
- **Success:** clean 15-min window.
- **Failure at any of these:** roll back per §5 R8. The old server is still Ready.

### Step 10 — Delete the old server

- `az postgres flexible-server delete -n psql-vrbook-staging -g rg-vrbook-staging --yes`.
- 14-day backup retention persists on the deleted server per Azure policy — recovery still possible if something surfaces later.
- **Success:** `az postgres flexible-server show -n psql-vrbook-staging -g rg-vrbook-staging` returns 404.
- **Failure:** delete errors are rare; if it hangs, force-delete via the portal or retry.

### Step 11 — Delete the temporary secondary KV secret

- `az keyvault secret delete --vault-name kv-vrbook-staging --name postgres-cs-v2` (KV soft-delete retains it for 90 days — recovery still possible).
- **Success:** `az keyvault secret show --name postgres-cs-v2` returns 404.

### Step 12 — Cleanup commit (Commit B — post-cutover)

- Edit `infra/main.bicep`:
  - Remove the `pgV2` module invocation and the `deployStagingPgV2` param.
  - On the primary `pg` module invocation, set `serverNameOverride: env == 'staging' ? 'psql-vrbook-staging-v2' : ''`. This locks the name so future Bicep deploys converge on the promoted server rather than trying to create `psql-vrbook-staging` again.
  - Postgres Flexible Server does NOT support rename (A5), so we live with the `-v2` suffix. Cosmetic only.
- Edit `infra/environments/staging.bicepparam`: remove `deployStagingPgV2 = true`.
- `az deployment group what-if` — expected: **no changes** (V2 already exists and matches desired state; old server already deleted).
- `az deployment group create` — no-op deploy that reconciles.
- Commit both edits + close-out doc update.

---

## §4 Container-app / migrator connection-string plumbing (verified 2026-07-05)

- API container app (`ca-vrbook-api-staging`) reads `ConnectionStrings__Postgres` from env var, wired via `secretRef: postgres-cs` in `infra/main.bicep` `apiEnvVars`. Container Apps resolve KV secrets **at revision create time** and cache per-revision. To pick up a new value: create a new revision.
- All Container App Jobs (migrator, sync, bookingexpiry, completion, notifdispatch) resolve `secretref:postgres-cs` **at execution start** — each cron tick / manual start re-reads the KV secret. No revision bump needed for jobs.
- The migrator's `Program.cs` reads config via `AddEnvironmentVariables()`, so the env-var override in §3 step 6 is sufficient — no code path baked at build time.

## §5 Rollback matrix (explicit, per-step)

| Failure at step | State of old server | State of V2 | Rollback action |
|---|---|---|---|
| R2 (Bicep syntax) | Alive | Doesn't exist yet | Fix Bicep. No infra rollback. |
| R3 (deploy V2 fails) | Alive | Partial or absent | `az deployment group create` again after fixing root cause (quota / SKU / config). No infra rollback needed — Bicep is idempotent, partial V2 shell can be deleted via `az postgres flexible-server delete -n psql-vrbook-staging-v2 --yes`. |
| R4 (owner can't reach V2) | Alive | Alive but firewalled | Adjust `pgFirewallRules` in `main.bicep`, redeploy. No effect on old server. |
| R6 (local migrator fails on V2) | Alive | Alive but bad/no schema | Ctrl-C the local `dotnet run`. No Azure state to revert. Diagnose: (a) connection issue → check firewall rule from §3 step 3; (b) migration bug → fix + rebuild + retry, or if V2 is beyond salvage `az postgres flexible-server delete -n psql-vrbook-staging-v2 --yes` + restart from step 3. |
| R7 (shadow API smoke fails) | Alive | Alive with schema | Same as R6. |
| R8 (post-cutover API failing) | Alive | Serving | (a) `az keyvault secret set --name postgres-cs --value "$(cat /tmp/postgres-cs-preexist.txt)"` (b) `az containerapp update -n ca-vrbook-api-staging -g rg-vrbook-staging --revision-suffix rollback-$(date +%s)` — new revision reads the reverted secret, points at old server. (c) V2 stays running but idle; investigate + retry step 8, or delete V2 if wrong. |
| R9-new (first post-cutover migrator job run fails: KV RBAC / secretref plumbing latent-broken) | Alive | Serving | This is the residual risk Option A accepts. Same as R8: revert `postgres-cs` KV secret to V1 value, bump API revision. V1 keeps serving; investigate the container-app-job → KV → V2 wiring separately. Note: the plumbing (MI, KV secret name) is unchanged from what V1 uses today, so failure would be surprising. |
| R10 (old-server delete fails) | Broken | Serving | Retry delete via portal. No functional impact. |
| Post-cutover, days later, latent V2 bug discovered | Deleted, backups retained 14 days | Serving but broken | Restore old server from backup: `az postgres flexible-server restore --source-server psql-vrbook-staging --name psql-vrbook-staging-restored --restore-time <ISO>`. Update `postgres-cs` to the restored FQDN. Full recovery path — costs 1-2h of downtime but not "no DB for hours during rebuild". |

**Non-negotiable rule:** before step 8, the operator captures the current `postgres-cs` value (see §3 step 8 pre-step).

---

## §6 What does NOT change

- Prod remains VNet-injected + `publicNetworkAccess: 'Disabled'`. Prod passes the default parameter.
- `require_secure_transport = on` remains on both old + new servers.
- Entra AD auth + password auth both remain enabled.
- `apiEnvVars` / `apiSecrets` in `main.bicep` — unchanged. Cutover happens by rotating the KV secret's value, not by changing the Bicep secret refs.
- App code, RLS policies, seed migration content — unchanged.

## §7 What DOES change (small)

- `pg_dump`/`pg_restore` habit for future infra changes: keeping a snapshot even when the data is expendable is cheap insurance. Consider a scheduled export to a private blob as a follow-up (not this slice).
- Once prod is stood up, an ADR revisits public-vs-private posture for prod. Documented separately.

---

## §8 Session budget

Single session, ~3 hours end-to-end:
- Prep + snapshot + Bicep edit (Commit A): 30 min.
- Deploy V2 + migrator + DBeaver verify: 45 min (of which ~15 min is Azure provisioning time).
- Cutover + burn-in: 30 min.
- Delete old server + secret + cleanup commit (Commit B): 30 min.
- Buffer: 45 min.

---

## §9 Empirical verifications required BEFORE execution

These are the assumptions the plan depends on. Each must be verified with a docs read or a `--help`/`what-if` invocation before Commit A ships. None of them can burn the old server.

| ID | Assumption | Verification |
|---|---|---|
| A1 | Old server's admin login name (I had `vrbookadmin` in my earlier prompt; `main.bicep` line 27 declares `vrbook_admin`). Different logins mean V2 connection string won't match. | `az postgres flexible-server show -n psql-vrbook-staging -g rg-vrbook-staging --query administratorLogin -o tsv`. |
| A2 | `az containerapp job update --set-env-vars` + `--secrets` accepts `keyvaultref:...` values. | `az containerapp job update --help` or `--yes --no-wait` dry-run. Fallback: YAML apply. |
| A3 | Container App Job outbound traffic to public Postgres Flex Server is covered by `AllowAzureServices` (0.0.0.0-0.0.0.0). | Attempt step 6; if "no pg_hba.conf entry", add firewall rule for CAE's static IP. |
| A4 | Rotating a KV secret does NOT auto-restart Container App revisions; new revision must be created. | Azure docs "Manage secrets in Azure Container Apps" — well-known but worth re-reading. |
| A5 | Postgres Flexible Server does NOT support rename. | `az postgres flexible-server --help` — no `rename` verb. ARM `name` is immutable. |
| A6 | `main.bicep` can invoke `postgres-flexible.bicep` twice in one deployment with different `serverNameOverride` values. | `az deployment group what-if` at step 2 will show. Fallback: duplicate module file `postgres-flexible-v2.bicep` hardcoding V2 name. |
| A7 | `postgres-admin-password` KV secret is the same on both servers (both `pg` and `pgV2` reference it via `getSecret()` in `staging.bicepparam`). | Read `staging.bicepparam` — parameterized. Both server invocations receive the same password. |
| A8 (new) | CAE → V2 connectivity via `AllowAzureServices` (0.0.0.0-0.0.0.0) is sufficient — no need to pre-add CAE outbound IP `135.18.171.52`. | Assumed based on: (a) live V1 works today under identical AllowAzureServices rule; (b) Azure Postgres Flex docs treat 0.0.0.0-0.0.0.0 as "all in-region Azure-internal traffic". Verify empirically at first post-cutover migrator job execution (step 9 tail). If SQLSTATE 28000 fires, add `{ name: 'CAE-Outbound', startIp: '135.18.171.52', endIp: '135.18.171.52' }` to `pgFirewallRules` in `main.bicep` and redeploy. |
| A9 (new) | The `Username=` field in step 8's `postgres-cs` KV update uses `vrbook_admin`, not `vrbook` or `vrbookadmin`. | Cross-check with A1. Grep the plan `Username=vrbook[^_]` — must return no hits. |
| A10 (NEW, execution surprise) | `az containerapp update --revision-suffix` **inherits the parent Container App template's image field**, which the Bicep `main.bicep` seeds with `mcr.microsoft.com/azuredocs/containerapps-helloworld:latest` as a placeholder (real image is set only by the CD workflow at build time). Manual revision bumps without `--image` create revisions serving the placeholder, which fail health-probe activation. Discovered at step 8 during cutover: three revisions (`cutover-*`, `cutover2-*`, `rollback-*`) all stuck in `Activating` because they were running helloworld. Resolution: pass `--image <current-healthy-tag>` explicitly. Cutover succeeded on `cutover3-*` in 20 seconds once the image was correct. Add this to any future manual revision-bump runbook. |
| A8 empirical result | CAE outbound IP `135.18.171.52` was NOT covered by AllowAzureServices for Container Apps → public Postgres Flex Server outbound. Confirmed during cutover: helloworld revisions couldn't reach V2, but neither could a briefly-tested real-image revision until the specific rule was added. `CAE-Outbound` firewall rule added imperatively via `az postgres flexible-server firewall-rule create` + folded into Bicep in the Step 12 cleanup commit. |

**Any A1-A9 that fails on verification blocks execution until resolved.** A1, A3, A5, A7 verified 2026-07-05. A2 resolved via plan change. A4, A6 confirmed. A8, A9 empirically-verified during execution.

---

## §10 Approval

Awaiting owner sign-off. On approval:
- Update Status header to APPROVED.
- Execute §9 verifications, then §3 sequence.
- Commit A creates + verifies V2. Commit B post-cutover cleanup.
- Update `docs/MASTER_PLAN.md` with an INFRA.1 row after ship.
- Then proceed to OPS.M.12.1.
