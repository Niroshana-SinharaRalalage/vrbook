# Slice OPS.INFRA.1 — Rebuild staging Postgres as public-access server (LankaConnect parity)

- **Status:** DRAFT for owner approval before execution.
- **Date:** 2026-07-05.
- **Owner-approved intent:** rebuild staging Postgres with `publicNetworkAccess: Enabled` + IP-firewalled access for owner's DBeaver session. Trade-off explicitly accepted: internet-reachable + allowlisted, matching LankaConnect posture. Prod stays private (separate decision when prod stands up).
- **Prior analysis:** a scoping agent proved 2026-07-05 that public-vs-private is fixed at server-create time for Postgres Flexible Server — no toggle, no in-place migration. Rebuild is the only path.
- **Executes BEFORE OPS.M.12** so M.12's REFUSE-AT-PROVISIONING branch can be verified by direct `user_identities` inspection during the smoke walk.

---

## §1 What ships

Single-slice, staging-only rebuild. Steps land in one commit that touches:

1. **`infra/modules/postgres-flexible.bicep`** — public-access variant.
   - Parameter `publicNetworkAccess string` (default `'Disabled'` for prod safety).
   - Parameter `firewallAllowedIpRanges array` (default `[]`).
   - When `publicNetworkAccess == 'Enabled'`:
     - `network.publicNetworkAccess: 'Enabled'`.
     - `network.delegatedSubnetResourceId: null` (Bicep expresses this by omitting the property inside a conditional block — see §2 for the technique).
     - `network.privateDnsZoneArmResourceId: null` (same).
   - Firewall rule child resources deployed per IP range:
     - Owner's home/office IPs (fetched at plan time, checked into a Key Vault secret so the Bicep param comes through `getSecret()`, NOT hard-coded in-file).
     - Azure services IP range (`0.0.0.0` → `0.0.0.0`) for CD-managed migrator + container-app access. This is Azure's convention for "allow all Azure resources"; verified against Azure docs. Locked to Bicep-driven so we never open 0.0.0.0/0 to the internet.
   - **Prod safety guarantee:** because `publicNetworkAccess` defaults to `'Disabled'` at the parameter level, `main.bicep` for prod NEVER opts in. Staging-only opt-in via an explicit parameter override in `main.bicep`'s `env == 'staging'` branch.
2. **`infra/main.bicep`** — flip the staging invocation of `postgres-flexible` to pass `publicNetworkAccess: 'Enabled'` + `firewallAllowedIpRanges: [...]`.
3. **New Key Vault secret** `postgres-owner-ip-ranges` — a small JSON array of `[{startIp, endIp}, ...]` fetched via `getSecret()` and expanded to firewall rules.

Server NAME stays `psql-vrbook-staging` — but because the existing server can't flip its network mode, we delete the old server FIRST, then let Bicep create the new one. Same name, same DNS FQDN, so Key Vault `postgres-cs` connection string only needs the `Host=` part re-verified (should be unchanged — Postgres Flexible Server FQDN pattern is `<name>.postgres.database.azure.com`).

## §2 Execution sequence

The Bicep + delete + reapply dance:

1. **Owner-side prep** — provide the current public IP(s) to allowlist. I read from a Key Vault secret; if the owner rotates ISP or adds a new location, they update the secret + re-deploy Bicep (a one-liner).
2. **Snapshot the current schema + seed data** — dump `psql-vrbook-staging` → `.sql` file → checked into `scripts/staging-seed/2026-07-05-preinfra1-snapshot.sql`. Belt-and-braces so if the reseed misses anything, the owner has a rollback source. Runs from an Azure Cloud Shell inside the VNet (the only way to reach the current private server).
3. **Delete the old server** — `az postgres flexible-server delete -n psql-vrbook-staging -g rg-vrbook-staging --yes`. Cascade-deletes the databases + firewall rules. Backups are retained per policy (14 days) — recovery path if the rebuild fails.
4. **Deploy the updated Bicep** — `az deployment group create -g rg-vrbook-staging -f infra/main.bicep -p env=staging`. Creates the new server with `publicNetworkAccess: 'Enabled'` + firewall rules.
5. **Verify Key Vault `postgres-cs` still reflects the new FQDN.** Should — Bicep resolves it via `pg.outputs.fqdn` and pipes into the same `postgres-cs` secret. If the resolution changed (e.g. Bicep now emits a different FQDN pattern), update `postgres-cs` explicitly.
6. **Run the migrator against the fresh server** — `az containerapp job start -n caj-vrbook-migrator-staging -g rg-vrbook-staging`. Applies all EF migrations from scratch on an empty DB. This is where M.13's `user_identities` table etc. get created.
7. **Reseed** — either (a) run the checked-in seed migration if there is one, or (b) apply the snapshot from step 2 if the operator wants historical data preserved. Recommend (a) — a clean fresh DB is easier for M.12's smoke walk.
8. **Owner-side DBeaver verify** — `psql -h psql-vrbook-staging.postgres.database.azure.com -U vrbookadmin -d vrbook` from the owner's laptop → confirms `publicNetworkAccess` reachable + firewall lets the owner's IP through.
9. **CD end-to-end smoke** — trigger a no-op CD run (bump a comment in `web/src/app/robots.txt` say, push) → verify the deploy pipeline still resolves `postgres-cs` + migrator + container-app health. If the CD pipeline reads from a stale connection-string cache, refresh.

## §3 Rollback

If step 6 fails (migrator crashes on empty DB):
- The old server is deleted (step 3). Restore via `az postgres flexible-server restore` from the 14-day backup retention. Restore creates a new server; rename or repoint Key Vault `postgres-cs` to it.
- Cost: 1-2h of downtime + a name mismatch until Bicep is redeployed.

If step 4 fails (Bicep deploy error):
- Old server already deleted. Restore from backup as above.
- Post-mortem the Bicep error before retry.

## §4 What does NOT change

- Prod remains VNet-injected + `publicNetworkAccess: 'Disabled'`. `main.bicep` for prod passes the default parameter.
- SSL enforcement (`require_secure_transport = on`) remains.
- Entra AD auth remains enabled alongside password auth.
- `postgres-cs` Key Vault secret naming unchanged.
- Application code, RLS policies, seed migration content — no change.

## §5 What DOES change (small)

- Trigger for future infra-privacy discussion: once we stand up prod, we consciously decide whether prod matches staging's public model (probably NO — prod has real customer PII + higher assurance) or stays VNet-only. Documented in a follow-up ADR at prod-cutover time; not this slice.

## §6 Session budget + timeline

Single session, ~2 hours end-to-end:
- Bicep edit + snapshot + delete: 30 min.
- Deploy + migrator + verify: 45 min.
- Owner DBeaver check + CD smoke: 30 min.
- Buffer: 15 min.

## §7 Approval

Awaiting owner sign-off. On approval:
- Update this file's Status header to APPROVED.
- Execute §2 sequence in one commit + one Azure deploy.
- Update `docs/MASTER_PLAN.md` with an INFRA.1 row after ship.
- Then proceed to OPS.M.12.1.
