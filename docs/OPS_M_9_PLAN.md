# OPS.M.9 — RLS Policies + `IRlsBypassDbContextFactory<TContext>` (Plan, rev 1)

**Status**: Proposed — awaiting user review.
**Author**: Plan agent (architect) consult, 2026-06-28.
**MASTER_PLAN reference**: `docs/MASTER_PLAN.md` §1 row OPS.M.9 + §2 row 10 ("RLS policies + `IRlsBypassDbContextFactory<TContext>` + bypass connection factory", 1.5-day estimate).
**MULTI_TENANCY reference**: `docs/MULTI_TENANCY_OPS_PLAN.md` §6 ("defense-in-depth — application enforcement first, RLS as belt-and-braces") + §7 ("RLS bypass for platform-admin reads").
**PHASE_3 reference**: `docs/PHASE_3_RECONNAISSANCE.md` lines 39-46 — the cheap-now/expensive-later decision is **binding granularity** (per-statement vs per-connection); per-statement is the door that Phase 4 OTA needs.

**Predecessors**:
- Slice OPS.M.0 ✅ (Entra External ID + App Roles).
- Slice OPS.M.1 ✅ (`Tenant` aggregate + `tenant_memberships`).
- Slice OPS.M.2 ✅ (`ICurrentUser.TenantId` + DB-wins claim enrichment per ADR-0014).
- Slice OPS.M.3 ✅ (every tenant-scoped table now carries `tenant_id NOT NULL` with a FK to `identity.tenants(id)`; verified in §3 inventory).
- Slice OPS.M.4 ✅ (`TenantAuthorizationBehavior` — app-layer write gate; verified `TenantAuthorizationBehavior.cs:41-76`).
- Slice OPS.M.5 ✅ (`HandleStripeWebhookHandler` resolves tenant from `stripe_account_id` BEFORE the tenant id is known — verified `HandleStripeWebhookCommand.cs:55-94`; this is the canonical M.9 bypass call site).
- Slice OPS.M.6 ✅ (`BackgroundCommandTenantScopeBehavior` asserts non-empty `TenantId` on `IBackgroundCommand`; the Sync worker reads `sync.channel_feeds` cross-tenant on bootstrap — verified `Workers.Sync/Program.cs:74-80`; second canonical bypass call site).
- Slice OPS.M.7 ✅ (`MeTenantDto` + `OnboardingProgressDto`; the M.7 GET path reads only the caller's own tenant and does NOT need the bypass).
- Slice OPS.M.8 ✅ (`ICurrentUser.IsPlatformAdmin` + `[Authorize(Roles="PlatformAdmin")]` controller gate + the three cross-tenant platform write endpoints; verified `TenantsPlatformController.cs` + `PlatformTenantQueries.cs`). M.8 introduced the **app-layer** PlatformAdmin bypass; M.9 introduces the **DB-layer** bypass that closes the second hole.

**Sequence**: After Slice OPS.M.8; before Slice OPS.M.10 (which adds the cross-tenant isolation test pack that asserts BOTH layers). Phase 3 (Slice 8 hotel rooms + Slice 9 multi-unit cart) does NOT touch this slot. Phase 4 (Slice 10 OTA package bundling) is the consumer of the per-statement binding granularity decision (§4 D1).

**Estimate**: **1.5 days, one engineer** — TDD-first, see §5. The slot is small in code volume (~10 migrations of ~10 lines each + one interceptor + one factory contract + one DI-wiring change per module) but wide in surface area — the migrations touch every module's schema.

This plan is the contract. Slice OPS.M.9 ships **(i) one `ENABLE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation` migration per tenant-scoped table across all 9 modules**, **(ii) a `TenantGucCommandInterceptor : DbCommandInterceptor` that fires `SET LOCAL app.tenant_id = '<guid>'` + `SET LOCAL app.is_platform_admin = '<bool>'` before every command on every request-scoped DbContext**, **(iii) the `IRlsBypassDbContextFactory<TContext>` contract in `VrBook.Contracts/Interfaces/` + one impl class per module + DI registration**, **(iv) wiring the Stripe webhook handler + the Sync worker bootstrap + the M.8 `ListPlatformTenantsHandler`/`GetPlatformTenantHandler` to use the bypass factory at exactly the cross-tenant call sites enumerated in §7**, **(v) arch tests pinning the bypass-factory registration (every DbContext with `tenant_id` columns must have a registered bypass-factory) and the call-site discipline (the bypass factory may only be injected into the explicit allow-list)**, **(vi) Postgres-fixture integration tests asserting every RLS policy denies cross-tenant SELECT + UPDATE + DELETE when `app.tenant_id` is set to the wrong value AND allows when set to the right value AND permits all when `app.is_platform_admin = 'true'`**, **(vii) a runbook documenting how to diagnose "RLS rejected my query" failure modes in production**.

The cross-tenant isolation **test pack** (the holistic two-tenant integration sweep that exercises every public endpoint with Owner-of-A trying to reach tenant-B's rows) is **explicitly OUT** of M.9 — that's Slice OPS.M.10. M.9 ships the *mechanism*; M.10 ships the *proof*.

Per-row encryption (Phase 2), auth-aware materialized views (Phase 2), per-tenant DB roles (Phase 2 — the Phase 1.5 ship uses one shared `vrbook` role + GUC-driven policies), and Phase 4 OTA cross-tenant itinerary reads (Slice 10) are also out of M.9. M.9 picks the binding granularity (per-statement) that *makes* Phase 4 possible without a rewrite.

---

## 1. Scope summary

### 1.1 What this slice ships

| # | Deliverable | Touches |
|---|---|---|
| 1 | **RLS policy migrations** — one EF migration per module that (a) `ALTER TABLE … ENABLE ROW LEVEL SECURITY;` and (b) `ALTER TABLE … FORCE ROW LEVEL SECURITY;` (so the table-owning role is NOT exempt) and (c) `CREATE POLICY <name> ON <table> USING (tenant_id = current_setting('app.tenant_id', true)::uuid OR current_setting('app.is_platform_admin', true) = 'true') WITH CHECK (…);` for every tenant-scoped table in the inventory (§3). Atomic-deploy wave 2 per §2. | 9 migration files, one per module (Identity, Catalog, Booking, Payment, Reviews, Pricing, Messaging, Sync, Notifications), naming convention `OpsM9a_<Module>_RlsPolicies`. |
| 2 | **`TenantGucCommandInterceptor`** — a `DbCommandInterceptor` that fires the `SET LOCAL app.tenant_id = …` + `SET LOCAL app.is_platform_admin = …` GUCs at the start of every command on every request-scoped DbContext. The interceptor reads `ICurrentUser.TenantId` + `ICurrentUser.IsPlatformAdmin` per-command (per-statement binding — D1). Lives in `VrBook.Infrastructure.Persistence`. Registered globally on every module's `AddDbContext` via the existing `UseOutbox(sp)` extension pattern. | `src/VrBook.Infrastructure/Persistence/TenantGucCommandInterceptor.cs` (new) + `src/VrBook.Infrastructure/Persistence/DbContextOptionsBuilderExtensions.cs` (extend or new). |
| 3 | **`IRlsBypassDbContextFactory<TContext>` contract** — generic interface in `VrBook.Contracts/Interfaces/IRlsBypassDbContextFactory.cs`. Returns a fresh, short-lived `TContext` whose connection has `app.is_platform_admin = 'true'` set per-statement (i.e. the same per-statement interceptor mechanism, but stamped with the platform-admin bypass instead of the caller's tenant id). The factory is the ONLY supported way to perform a cross-tenant read. | `src/VrBook.Contracts/Interfaces/IRlsBypassDbContextFactory.cs` (new contract). |
| 4 | **Per-module bypass-factory impls** — one `RlsBypass<Module>DbContextFactory.cs` per DbContext that wraps `IDbContextFactory<TContext>` (registered via `AddDbContextFactory<TContext>`) and stamps a `BypassMode = Enabled` flag onto the returned context. The interceptor reads the flag (via a `DbContext.Items` equivalent, or an `AsyncLocal<bool>` keyed by the factory) and emits `SET LOCAL app.is_platform_admin = 'true'` instead of `SET LOCAL app.tenant_id = …` for that context's commands. | 9 new files, one per module: `IdentityModule/Infrastructure/Persistence/RlsBypassIdentityDbContextFactory.cs` etc. |
| 5 | **DI wiring per module** — every module that today calls `services.AddDbContext<XxxDbContext>(…).UseOutbox(sp)` adds `.AddDbContextFactory<XxxDbContext>(…)` alongside (for the bypass factory's underlying pooled factory) AND registers `IRlsBypassDbContextFactory<XxxDbContext>` → `RlsBypassXxxDbContextFactory`. The interceptor is registered once at the host level (via a singleton + the `UseOutbox(sp)` pattern extended to add `.AddInterceptors(sp.GetRequiredService<TenantGucCommandInterceptor>())`). | 9 module files (`IdentityModule.cs`, `CatalogModule.cs`, `BookingModule.cs`, `PaymentModule.cs`, `ReviewsModule.cs`, `PricingModule.cs`, `MessagingModule.cs`, `SyncModule.cs`, `NotificationsModule.cs`). |
| 6 | **Wire the existing cross-tenant call sites to the bypass factory** — three call sites today: (a) `HandleStripeWebhookHandler.tenantStripe.GetByStripeAccountAsync` (verified `HandleStripeWebhookCommand.cs:83`), (b) the Sync worker's `feeds.ListDueForPollAsync` (verified `Workers.Sync/Program.cs:80`), (c) the M.8 `ListPlatformTenantsHandler` + `GetPlatformTenantHandler` (verified `PlatformTenantQueries.cs:35, :83`). Each switches from the request-scoped DbContext to a bypass-factory call. The handlers' existing behavior + audit guarantees do not change. | `TenantStripeContextLookup.cs` (extend to accept the factory), `PlatformTenantStatsLookup.cs` (same), `Workers.Sync/Program.cs` (small bootstrap edit), `PlatformTenantQueries.cs` (handler edits). |
| 7 | **Arch tests** — three new test classes: (a) `RlsBypassFactoryRegistrationTests` reflects on every module's DI registration and asserts every DbContext that owns a tenant-scoped table has both a registered DbContext AND a registered `IRlsBypassDbContextFactory<>`; (b) `RlsBypassCallSiteAllowlistTests` reflects on every constructor-injected `IRlsBypassDbContextFactory<>` injection and asserts the injecting class is in an explicit allow-list documented in `§7` (architecture rule: bypass is for explicitly enumerated cross-tenant reads, not a backdoor); (c) `RlsPolicyShapeTests` reflects on the migration files and asserts every tenant-scoped table per the §3 inventory has both `ENABLE RLS` and a `CREATE POLICY` line referencing `app.tenant_id` + `app.is_platform_admin`. | 3 files under `tests/VrBook.Architecture.Tests/`. |
| 8 | **Integration tests — Postgres-fixture facts** — one fact class per module asserting: (a) RLS is enabled on every tenant-scoped table; (b) cross-tenant SELECT returns zero rows; (c) cross-tenant UPDATE affects zero rows; (d) cross-tenant DELETE affects zero rows; (e) same-tenant SELECT returns expected rows; (f) `app.is_platform_admin = 'true'` lets all rows through; (g) when no GUC is set, queries don't error (the `, true` second arg to `current_setting()`) but return zero rows (the policy filter is false). | 9 files under `tests/VrBook.Api.IntegrationTests/Rls/` (one per module), driven by a shared `RlsFixture` extending the existing `TenantIdRolloutFixture` pattern (verified `tests/VrBook.Api.IntegrationTests/Identity/TenantIdRolloutFixture.cs`). |
| 9 | **Operator runbook** `docs/runbooks/rls-diagnose.md` — documents (a) the three failure modes (RLS-rejected SELECT returning zero rows when data exists, RLS-rejected UPDATE affecting zero rows, `permission denied` when a migrator-role session forgets to disable RLS), (b) how to inspect `pg_policies` to confirm policy presence, (c) how to read the structured log lines emitted by the interceptor at `Debug` level, (d) the bypass-factory escape hatch for production ops queries. | New file `docs/runbooks/rls-diagnose.md`. |
| 10 | **`AnonymousCurrentUser` consistency check** — `AnonymousCurrentUser` (verified `src/VrBook.Infrastructure/Common/AnonymousCurrentUser.cs:9-21`) returns `TenantId = null` + `IsPlatformAdmin = false`. The interceptor under those values emits `SET LOCAL app.tenant_id = ''` (empty); per D2, `current_setting('app.tenant_id', true)::uuid` would throw if the GUC is non-empty-but-invalid. We handle the empty case explicitly (D7) so the migrator path + the worker's pre-tenant-resolved code path both work. | Documented in D2 + D7; no `AnonymousCurrentUser` code change. |
| 11 | **`UserProvisioningMiddleware` integration check** — the middleware (verified `UserProvisioningMiddleware.cs:27-112`) stamps tenant id BEFORE any handler runs. The interceptor runs on every DbContext command, including the `db.Set<TenantMembership>` + `db.Users` reads inside the middleware (verified `UserProvisioningMiddleware.cs:68-76`). Those reads MUST execute as bypass — the middleware is reading the caller's own user row before the tenant claim is materialized; an RLS policy on `identity.users` (which doesn't carry `tenant_id` — confirmed §3) is moot, but the `tenant_memberships` read needs careful treatment. | Documented in D11; the `tenant_memberships` table is NOT RLS-protected in M.9 (the bootstrap read is the load-bearing reason). |
| 12 | **CI gate** — every CI run executes the M.9 integration fact pack against a fresh Postgres testcontainer. Failing facts block the merge. | CI workflow update; the test class `Category=Integration` already runs in CI per the OPS.M.5 / OPS.M.6 precedent. |

### 1.2 What's explicitly OUT of OPS.M.9

| Item | Owner slice | Why deferred |
|---|---|---|
| Cross-tenant isolation **test pack** (the holistic two-tenant integration sweep against every public endpoint) | Slice OPS.M.10 | M.10 owns the proof; M.9 owns the mechanism. M.10 will exercise every endpoint (Owner-of-A vs tenant-B; PlatformAdmin-vs-both; PlatformAdmin-via-bypass; PlatformAdmin-without-bypass-403). M.9's integration tests prove the RLS policies enforce correctly *at the DB level*; M.10 proves the end-to-end auth chain holds at the API. The two are layered. |
| Per-tenant DB roles | Phase 2 | Phase 1.5 ships one shared `vrbook` app role + one shared `vrbook_migrator` migrator role. Per-tenant roles (a separate DB role per tenant) is a stronger isolation property but multiplies operational overhead; Phase 2 hardening can swap in if/when the threat model demands. M.9's GUC-driven policies are role-agnostic. |
| Per-row encryption | Phase 2 | Encrypting `email`, `phone`, `payment_intents.metadata`, etc. is an additive surface that does NOT need to land with RLS. PII encryption is a separate compliance lever. |
| Auth-aware materialized views | Phase 2 | Reporting + analytics surfaces (Slice 7) may want materialized views; RLS interaction with mat-views is non-trivial (mat-views inherit RLS from base tables only when refreshed by an unprivileged role). Phase 2 owns the choice. |
| Phase 4 OTA cross-tenant itinerary reads | Slice 10 (Phase 4) | The architect's PHASE_3_RECONNAISSANCE verdict is that M.9 picks **per-statement** binding so a single transaction can hop between supplier-tenant ids on the read path. M.9 ships the mechanism; Slice 10 is the consumer. |
| Audit-log read RLS exemption | Phase 2 | `identity.audit_log.tenant_id` is nullable (per §3 — super-admin actions carry null tenant) but a PlatformAdmin reading the audit log cross-tenant needs the bypass. M.9 ships the bypass mechanism but does NOT light up a "show me every PlatformAdmin action against tenant X" endpoint (that's the M.8 §1.2 carve-out — Open Question O2 in M.8 plan). When the audit-log read endpoint ships, it'll use the bypass factory; no M.9 work required. |
| `IDbContextFactory<TContext>` direct usage by other code | Phase 2 / discretionary | M.9 registers `AddDbContextFactory<TContext>` so the bypass impl has something to wrap, but does NOT promote `IDbContextFactory<>` injection as a general pattern. Existing scoped-DbContext injections continue. The bypass factory is the only blessed factory-style consumer. |
| Slice 4 (Notifications) refactor for RLS | Slice 4 | The `notifications.notification_log` table has `tenant_id NULLABLE` (verified §3 — guest-facing entries carry no tenant). The RLS policy on it is shaped per D6 (allow when `tenant_id IS NULL` OR matches GUC OR bypass). Slice 4's handlers do NOT need bypass-factory injections; the nullable case + the per-statement GUC binding covers them. |
| Phase 2 multi-org concept | Phase 2 | A future "PlatformAdmin scoped to Org X" would constrain the bypass policy to `current_setting('app.platform_admin_org_id', true) = tenant.org_id`. The Phase 1.5 ship uses platform-wide bypass; the policy SQL is forward-compatible (additive `OR`). |

### 1.3 Decision lock summary

| # | Decision | Locked verdict |
|---|---|---|
| D1 | Binding granularity for `app.tenant_id` | **Per-statement `SET LOCAL app.tenant_id = …`** — the same transaction can switch tenant binding on the next command. Locked by MASTER_PLAN.md §2 row 10 + PHASE_3_RECONNAISSANCE.md lines 39-46 (architect verdict). The Phase 4 OTA itinerary read path requires intra-transaction tenant hopping. |
| D2 | Where the bypass session variable is read | **`current_setting('app.is_platform_admin', true)`** — the second argument (`missing_ok = true`) makes Postgres return empty string (not throw) when the GUC is unset. Critical for the bootstrap path (migrator, worker pre-tenant-resolution, anonymous `/health` requests). |
| D3 | How the GUCs get set | **`DbCommandInterceptor.ReaderExecutingAsync` + `NonQueryExecutingAsync` + `ScalarExecutingAsync`** — prepend a `SET LOCAL app.tenant_id = '<guid>'; SET LOCAL app.is_platform_admin = '<bool>'; ` to the command text, OR (preferred) issue the SET LOCAL as a separate command immediately before the main command on the same connection inside the same transaction. We pick the **separate-command-on-same-transaction** shape (D3a) because it composes cleanly with EF's parameterization and avoids SQL-injection risk. |
| D4 | `IRlsBypassDbContextFactory<TContext>` contract shape | **`Task<TContext> CreateForBypassAsync(string reason, CancellationToken ct = default)`** — returns a fresh, owned `TContext` instance with the bypass flag stamped via `AsyncLocal<bool>` for the interceptor to read. Caller MUST `await using` the returned context. The `reason` parameter is captured into a structured log line every time the factory is invoked. |
| D5 | Background worker bypass | **Sync worker uses the bypass factory for the bootstrap `feeds.ListDueForPollAsync` call only; per-feed processing uses the normal request-scoped DbContext (which the per-statement interceptor stamps with `feed.TenantId`)**. The worker switches tenant binding per-feed because the bootstrap call is cross-tenant by nature. |
| D6 | Stripe webhook handler bypass | **`HandleStripeWebhookHandler` uses the bypass factory for the `TenantStripeContextLookup.GetByStripeAccountAsync` call only** — the lookup resolves the tenant id from the Stripe account id BEFORE the tenant is known. After the tenant is resolved, the `WebhookEvent` row write goes through the normal DbContext (with the resolved tenant id now stamped). |
| D7 | Where the per-request `SET LOCAL app.tenant_id` fires | **Inside the `TenantGucCommandInterceptor` at command-execution time** (per-statement, D1). The interceptor reads `ICurrentUser.TenantId` from the request-scoped `ICurrentUser`; if null (anonymous, unauthenticated, or worker pre-resolution), the interceptor emits `SET LOCAL app.tenant_id = ''` and the policy filter denies everything for tenant-scoped tables. The empty-string case + `current_setting('app.tenant_id', true)::uuid` would throw, so the policy is shaped to handle empty-string explicitly (see D2 + D9 policy shape). |
| D8 | Outbox-write path | **Outbox writes inherit the request's `app.tenant_id`** — the `OutboxMessage` row carries the same tenant id as the parent SaveChanges (or null for cross-tenant writes like the M.8 platform endpoints, which go through the bypass factory's DbContext and write with `is_platform_admin = 'true'`). No special-casing; the existing `DomainEventOutboxInterceptor` runs in the same transaction as the aggregate write and inherits the GUC. |
| D9 | Migration policy naming convention | **`rls_<schema>_<table>_tenant_isolation`** for the policy name; migration file naming `OpsM9a_<Module>_RlsPolicies.cs`. Documented in §6.4 so future tables follow the same pattern. |
| D10 | Read-side-only tables (no RLS) | **`outbox.outbox_messages` (per-module), `identity.users`, `identity.tenants`, `identity.tenant_memberships`** — these tables are explicitly carved out (§3 + D11). The outbox tables don't carry `tenant_id`; users/tenants/memberships need cross-tenant reads in the bootstrap path. The carve-out is enumerated by name in §3 + arch-tested in §7. |
| D11 | `tenant_memberships` carve-out justification | **Bootstrap path constraint** — `UserProvisioningMiddleware` reads `tenant_memberships` to materialize the `app_tenant_id` claim. Until the claim is materialized, the GUC `app.tenant_id` is empty. An RLS policy on `tenant_memberships` would block its own bootstrap. The carve-out is enforced by `tenant_memberships.tenant_id` having an implicit FK + an explicit `WHERE user_id = @oid` filter at every read site. M.10's test pack verifies a tenant A user cannot read tenant B's membership rows via any public endpoint (app-layer enforcement). |
| D12 | `webhook_events.tenant_id` is nullable — policy shape | **The policy on `payment.webhook_events` accepts `tenant_id IS NULL` OR matches GUC OR bypass.** The orphan-event case (Stripe event arrived before tenant is resolved — verified `HandleStripeWebhookCommand.cs:89-93`) lands with `tenant_id = null`; the orphan row must remain readable by the operator (via the bypass factory) for forensics. Same shape applies to `audit_log` + `notification_log`. |
| D13 | Migration ordering across modules | **All 9 migration files ship in one tag**. The interceptor and the migrations must atomically deploy together; otherwise the policy is active without the GUC source. §2 enforces. |

### 1.4 What OPS.M.3 / M.4 / M.5 / M.6 / M.7 / M.8 left for M.9 to clean up

1. **The OPS.M.3 invariant**: every tenant-scoped table has `tenant_id NOT NULL` with a FK to `identity.tenants(id)`. M.9 leans on this — the policy `USING (tenant_id = current_setting(...))` would throw on rows with `tenant_id IS NULL` if the column allowed nulls. Three tables in the inventory legitimately allow nulls (`audit_log`, `webhook_events`, `notification_log`) — their policies carry the `OR tenant_id IS NULL` clause (D12).
2. **The OPS.M.4 invariant**: every `ITenantScoped` command is gated by `TenantAuthorizationBehavior`. M.9 does not regress; the behavior is the *app-layer* enforcement, the RLS policy is the *DB-layer* enforcement. Both fire on every write.
3. **The OPS.M.5 cross-tenant lookup**: `TenantStripeContextLookup.GetByStripeAccountAsync` (verified `TenantStripeContextLookup.cs:28-40`) is the M.9 bypass call site #1. M.5 shipped this as a normal `IdentityDbContext` read; M.9 lights it up to use the bypass factory.
4. **The OPS.M.6 worker bootstrap**: `Workers.Sync/Program.cs:74-80` calls `feeds.ListDueForPollAsync(now)` without a tenant filter (it's enumerating across every tenant's feeds). M.6 shipped this with the `AnonymousCurrentUser` registration (verified `Program.cs:45`) — under M.9's interceptor, that anonymous registration would set `app.tenant_id = ''` and the call would return zero rows. M.9 switches the bootstrap to the bypass factory.
5. **The OPS.M.8 platform endpoints**: `ListPlatformTenantsHandler` + `GetPlatformTenantHandler` + `PlatformTenantStatsLookup` (verified `PlatformTenantQueries.cs:35, :83`; `PlatformTenantStatsLookup.cs:21-40`) read across tenants. M.8 shipped these with the controller-level `[Authorize(Roles="PlatformAdmin")]` + handler-level `currentUser.IsPlatformAdmin` check; M.9 adds the bypass-factory layer underneath so the DB also permits the cross-tenant read. **Without M.9, the M.8 endpoints would 200-but-return-zero-rows for the PlatformAdmin** — exactly the failure mode the runbook (§9) describes.
6. **The OPS.M.7 GET `/api/v1/me/tenant` path**: reads only the caller's own tenant (verified `GetMyTenantQuery.cs:27-31`); no bypass needed. The per-statement GUC binding handles it naturally.

---

## 2. Atomic-deploy constraints

Steps 1→9 in §5 sequence into **two waves** (the policy migrations and the GUC-setting interceptor MUST land atomically — if the policy lands without the interceptor, every query returns zero rows; if the interceptor lands without the policy, nothing breaks but the bypass factory is dead code).

### Wave 1 — Interceptor + factory infrastructure (Steps 1 + 2 + 3 + 4)

Ship in one tag:

1. `TenantGucCommandInterceptor` + `DbContextOptionsBuilderExtensions.UseRlsTenantGuc(sp)`.
2. `IRlsBypassDbContextFactory<TContext>` contract.
3. Per-module impl + DI wiring.
4. Wave 1 does NOT enable any policies. It exercises the interceptor against an unprotected schema. Behavioral effect: every query has two extra `SET LOCAL` statements; latency overhead is the per-command transaction-state set, negligible (<1ms on warm connections).

**Acceptance**: integration test asserts the interceptor fires `SET LOCAL app.tenant_id = '<guid>'` before every command on a request-scoped DbContext (assert via Postgres log capture: `log_statement = 'all'` in the testcontainer's `postgresql.conf`).

### Wave 2 — Policy migrations + call-site rewiring (Steps 5 + 6 + 7 + 8 + 9 + 10)

Ship in one tag:

1. All 9 RLS-policy migrations (one per module).
2. The three call-site rewires (Stripe webhook handler, Sync worker, M.8 platform endpoints) to use the bypass factory.
3. The runbook.
4. The arch + integration tests.

**Why bundle Wave 2**: the policy migration is what activates RLS enforcement at the DB level. If it lands without the call-site rewires, the M.8 platform endpoints + the Stripe webhook handler + the Sync worker bootstrap would start returning zero rows (they're cross-tenant reads through the now-RLS-protected tables). The wave is a single coordinated change.

**Acceptance**: a DevAuth Admin-persona request to `GET /api/v1/admin/platform/tenants` returns the full list (multi-tenant). A DevAuth Owner-persona request to the same endpoint returns 403 (the M.8 controller filter rejects). A Stripe webhook arriving with a known connected-account id correctly resolves to the right tenant (the bypass factory call succeeds). The Sync worker's nightly run enumerates due feeds across every tenant.

### Forward-replay constraint

M.9 introduces NO new outbox events. The migrations are schema-only (RLS policies; no column changes; no data changes). Replay safety: trivial — the policies are additive, and any in-flight command that hits the new policy either succeeds (matching tenant) or returns zero rows (cross-tenant — same shape as a `WHERE tenant_id = …` filter, the EF layer doesn't see a difference).

### Per-environment deploy script

Each wave is one tag → one `azd deploy` per environment. Wave 1 is app-tier code only; Wave 2 runs the DB migrations via the existing `VrBook.Migrator` path AND ships the app code that uses the new call-site rewires.

**Migrator role caveat**: the `vrbook_migrator` DB role MUST have `BYPASSRLS` granted at the role level so migrations (which `ALTER TABLE`, `INSERT INTO` for backfills, etc.) are not gated by the policies they're installing. The migrator role is shipped via `infra/sql/setup-roles.sql` (verify in §3 inventory); M.9 adds a `GRANT BYPASSRLS` to that role if not already present. This is a one-time grant + an idempotent SQL line in the migrator bootstrap. Documented in §6.5.

---

## 3. Tenant-scoped table inventory

The master list of every table that ships RLS in M.9. Inventory derived from the OPS.M.3 migration sweep (verified by sub-agent reconnaissance; every cited file path is grounded).

### 3.1 Tables that ship RLS (M.9 Wave 2)

| # | Module | Schema | Table | `tenant_id` column | Nullability | Policy name | Bypass clause |
|---|---|---|---|---|---|---|---|
| 1 | Catalog | `catalog` | `properties` | `tenant_id` | NOT NULL | `rls_catalog_properties_tenant_isolation` | `OR current_setting('app.is_platform_admin', true) = 'true'` |
| 2 | Catalog | `catalog` | `property_images` | `tenant_id` | NOT NULL | `rls_catalog_property_images_tenant_isolation` | same |
| 3 | Booking | `booking` | `bookings` | `tenant_id` | NOT NULL | `rls_booking_bookings_tenant_isolation` | same |
| 4 | Booking | `booking` | `booking_holds` | `tenant_id` | NOT NULL | `rls_booking_booking_holds_tenant_isolation` | same |
| 5 | Booking | `booking` | `availability_blocks` | `tenant_id` | NOT NULL | `rls_booking_availability_blocks_tenant_isolation` | same |
| 6 | Payment | `payment` | `payment_intents` | `tenant_id` | NOT NULL | `rls_payment_payment_intents_tenant_isolation` | same |
| 7 | Payment | `payment` | `refunds` | `tenant_id` | NOT NULL | `rls_payment_refunds_tenant_isolation` | same |
| 8 | Payment | `payment` | `webhook_events` | `tenant_id` | **NULLABLE** | `rls_payment_webhook_events_tenant_isolation` | `OR tenant_id IS NULL OR current_setting('app.is_platform_admin', true) = 'true'` |
| 9 | Reviews | `reviews` | `reviews` | `tenant_id` | NOT NULL | `rls_reviews_reviews_tenant_isolation` | standard |
| 10 | Pricing | `pricing` | `pricing_plans` | `tenant_id` | NOT NULL | `rls_pricing_pricing_plans_tenant_isolation` | standard |
| 11 | Pricing | `pricing` | `pricing_rules` | `tenant_id` | NOT NULL | `rls_pricing_pricing_rules_tenant_isolation` | standard |
| 12 | Messaging | `messaging` | `threads` | `tenant_id` | NOT NULL | `rls_messaging_threads_tenant_isolation` | standard |
| 13 | Messaging | `messaging` | `messages` | `tenant_id` | NOT NULL | `rls_messaging_messages_tenant_isolation` | standard |
| 14 | Notifications | `notifications` | `notification_log` | `tenant_id` | **NULLABLE** | `rls_notifications_notification_log_tenant_isolation` | `OR tenant_id IS NULL OR current_setting('app.is_platform_admin', true) = 'true'` |
| 15 | Sync | `sync` | `channel_feeds` | `tenant_id` | NOT NULL | `rls_sync_channel_feeds_tenant_isolation` | standard |
| 16 | Sync | `sync` | `external_reservations` | `tenant_id` | NOT NULL | `rls_sync_external_reservations_tenant_isolation` | standard |
| 17 | Sync | `sync` | `sync_conflicts` | `tenant_id` | NOT NULL | `rls_sync_sync_conflicts_tenant_isolation` | standard |
| 18 | Sync | `sync` | `sync_runs` | `tenant_id` | NOT NULL | `rls_sync_sync_runs_tenant_isolation` | standard |
| 19 | Identity | `identity` | `audit_log` | `tenant_id` | **NULLABLE** | `rls_identity_audit_log_tenant_isolation` | `OR tenant_id IS NULL OR current_setting('app.is_platform_admin', true) = 'true'` |

**Total: 19 tables across 9 modules ship RLS in M.9.**

### 3.2 Tables that do NOT ship RLS (carve-outs documented per D10 + D11)

| # | Module | Schema | Table | Reason for carve-out |
|---|---|---|---|---|
| 1 | Identity | `identity` | `users` | No `tenant_id` column — a user is platform-level, can be a member of N tenants. M.10 test pack verifies the app-layer prevents cross-tenant user enumeration. |
| 2 | Identity | `identity` | `tenants` | No `tenant_id` column — the table IS the tenant. M.10 test pack verifies that the M.8 cross-tenant read path (`ListPlatformTenantsHandler`) only the PlatformAdmin reaches; non-PlatformAdmin readers are 403'd before the DB sees the query. Lookup-by-stripe_account_id (the webhook bypass path) goes through the bypass factory. |
| 3 | Identity | `identity` | `tenant_memberships` | Bootstrap path constraint (D11). `UserProvisioningMiddleware` reads this table BEFORE the tenant claim is materialized. M.10 test pack verifies app-layer prevents cross-tenant membership reads. |
| 4 | Catalog | `catalog` | `outbox_messages` | Per-module outbox; system-level event log; no `tenant_id`. The outbox-relay worker reads cross-module; RLS would block it. |
| 5 | Booking | `booking` | `outbox_messages` | same |
| 6 | Payment | `payment` | `outbox_messages` | same |
| 7 | Reviews | `reviews` | `outbox_messages` | same |
| 8 | Pricing | `pricing` | `outbox_messages` | same |
| 9 | Messaging | `messaging` | `outbox_messages` | same |
| 10 | Notifications | `notifications` | `outbox_messages` | same |
| 11 | Sync | `sync` | `outbox_messages` | same |
| 12 | Identity | `identity` | `outbox_messages` | same |
| 13 | Catalog | `catalog` | `house_rules` | (verify in §6 — the `HouseRule` entity is a value-object child of `Property`; if it has its own table without `tenant_id`, RLS via the parent. If it inherits the parent's row via a FK, that's the discoverable shape.) |
| 14 | Catalog | `catalog` | `amenities` | Catalog/reference data — shared across tenants (an "amenity" like "Wi-Fi" is a platform vocabulary, not per-tenant). No `tenant_id`. |
| 15 | Pricing | `pricing` | `fees` | Catalog/reference data — shared fees vocabulary. |
| 16 | Booking | `booking` | `line_items` | Child of `bookings`; tenant scope inherited via FK. RLS at the parent (`bookings`) protects the child indirectly; cross-tenant SELECT on `line_items` is mooted by the FK + the M.10 test verification. (If a future M.10 finding shows a direct-read site on `line_items`, add the policy in a follow-up; M.9 ships the inheritance shape.) |
| 17 | Booking | `booking` | `guests` | Child of `bookings`; same inheritance argument. |
| 18 | Loyalty | `loyalty` | `accounts` | Per OPS.M.3, Loyalty is NOT in the multi-tenant rollout (loyalty accounts are platform-wide guest-level). RLS would be wrong here. M.10 test pack verifies no cross-tenant leak via the Loyalty surface. |

### 3.3 Inventory verification — how this was derived

Every entry in §3.1 + §3.2 was derived from:

- OPS.M.3a migration files: `Migrations/*_OpsM3a_<Module>_TenantIdColumn.cs` — adds the column NULL with FK.
- OPS.M.3c migration files: `Migrations/*_OpsM3c_<Module>_TenantIdNotNull.cs` — flips to NOT NULL.
- EF configuration files: `*Configuration.cs` — confirms the property + the `IsRequired()` shape.
- DbContext class definitions: `*DbContext.cs` — confirms the `DbSet<T>` set.

For ambiguous cases (child tables without their own `tenant_id` — `line_items`, `guests`, `house_rules`, `messages` if it's a child of `threads`), §3.2 documents the inheritance shape; the policy is on the parent.

**Open verification question (flagged for review)**: are `booking.line_items` and `booking.guests` first-class tables or owned-types serialized into JSONB on `bookings`? Sub-agent inventory shows `LineItems` + `Guests` as DbSets on `BookingDbContext` (verified `BookingDbContext.cs` Q3). If they're owned-types embedded as columns, no separate table exists; if they're child tables, the inheritance argument applies and an M.9 follow-up could add their own policy. **Architect action**: confirm during Step 6 by reading `BookingLineItemConfiguration.cs` (or equivalent). If they're child tables with their own `tenant_id` columns (denormalized for query performance), they MOVE TO §3.1 and ship RLS. If they're owned-types, they stay in §3.2.

### 3.4 Migration template

Each module's migration file uses this template:

```csharp
// OpsM9a_<Module>_RlsPolicies.cs

using Microsoft.EntityFrameworkCore.Migrations;

public partial class OpsM9a_<Module>_RlsPolicies : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // Enable RLS + force (so the table owner is not exempt).
        mb.Sql("ALTER TABLE <schema>.<table> ENABLE ROW LEVEL SECURITY;");
        mb.Sql("ALTER TABLE <schema>.<table> FORCE ROW LEVEL SECURITY;");

        // The policy: matches GUC tenant id OR PlatformAdmin bypass.
        mb.Sql(@"
            CREATE POLICY rls_<schema>_<table>_tenant_isolation ON <schema>.<table>
                USING (
                    tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                    OR current_setting('app.is_platform_admin', true) = 'true'
                )
                WITH CHECK (
                    tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                    OR current_setting('app.is_platform_admin', true) = 'true'
                );
        ");

        // Repeat for every tenant-scoped table in this module.
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.Sql("DROP POLICY IF EXISTS rls_<schema>_<table>_tenant_isolation ON <schema>.<table>;");
        mb.Sql("ALTER TABLE <schema>.<table> DISABLE ROW LEVEL SECURITY;");
        // Down does NOT undo FORCE — once disabled the FORCE flag is moot.
    }
}
```

For nullable-`tenant_id` tables (D12), the policy is:

```sql
CREATE POLICY rls_<schema>_<table>_tenant_isolation ON <schema>.<table>
    USING (
        tenant_id IS NULL
        OR tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        OR current_setting('app.is_platform_admin', true) = 'true'
    )
    WITH CHECK (
        tenant_id IS NULL
        OR tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        OR current_setting('app.is_platform_admin', true) = 'true'
    );
```

The `NULLIF(current_setting('app.tenant_id', true), '')::uuid` wrapping is the load-bearing detail. Without `NULLIF`, an empty-string GUC (the anonymous-bootstrap path) casts to a uuid throw; with `NULLIF`, it casts to `NULL` and the comparison is `tenant_id = NULL` which is always false — exactly the "deny everything" semantic we want for unauthenticated reads.

---

## 4. Design decisions

Every D-row ends with **"Decision: X"** per the OPS_M_8_PLAN format. Locked decisions are final unless an open question in Appendix B is promoted.

### 4.0 D-row index

| # | Topic | Verdict |
|---|---|---|
| D1 | Binding granularity (per-statement vs per-connection) | Per-statement (locked by MASTER_PLAN + PHASE_3_RECONNAISSANCE) |
| D2 | `current_setting()` shape | `, true` (missing-OK) + `NULLIF(..., '')::uuid` |
| D3 | How GUC gets set | `DbCommandInterceptor` + sibling `set_config(...)` command |
| D4 | Bypass factory contract | `CreateForBypassAsync(reason, ct)` returning owned `TContext`; AsyncLocal scope |
| D5 | Worker bypass | Bootstrap = bypass factory; per-feed = normal DbContext + `BackgroundTenantScope` |
| D6 | Stripe webhook bypass | Whole handler body under bypass (two factories: Payment + Identity) |
| D7 | Where the GUC fires | Inside the interceptor at command execution |
| D8 | Outbox-write path | Inherits the request's tenant id; no special-casing |
| D9 | Migration naming | `rls_<schema>_<table>_tenant_isolation`; one migration per module |
| D10 | Read-side-only carve-outs | `outbox_messages`, `users`, `tenants`, `tenant_memberships`, reference tables |
| D11 | `tenant_memberships` carve-out | Bootstrap-path constraint; app-layer enforcement only |
| D12 | Nullable-`tenant_id` tables | Three-branch policy: NULL OR GUC match OR bypass |
| D13 | Migration ordering | All 9 in one tag; no cross-module ordering constraint |

### 4.1 D1 — Binding granularity: per-statement `SET LOCAL app.tenant_id`

**Locked by**: MASTER_PLAN.md §2 row 10 ("choose per-statement `SET LOCAL app.tenant_id` over per-connection so the same factory can serve Phase 4 OTA cross-tenant itinerary reads") + PHASE_3_RECONNAISSANCE.md lines 39-46 (architect verdict). This decision is the load-bearing pre-shape that Phase 4 needs.

**Alternatives considered**:

- **(A) Per-connection binding** — bind `app.tenant_id` once at `Open()` time on each connection from the pool. Cheaper write; one GUC per connection; the connection pool serves a stream of same-tenant requests fast. **Rejected** because (i) Phase 4 OTA package bundling requires a single transaction to switch tenant binding (an itinerary spans stay/flight/car suppliers across N tenants; one read per leg with a different tenant id), and (ii) the connection pool's identity is opaque to the EF layer — a per-connection `SET` would leak across requests if Npgsql ever reused the connection without a reset (the `SET` without `LOCAL` is session-scoped; `SET LOCAL` is transaction-scoped; mixing them is fragile).
- **(B) Per-statement binding via `DbCommandInterceptor`** — emit `SET LOCAL app.tenant_id = '<guid>'` before every command. Marginally slower (two extra statements per query). Trivial to switch tenant binding inside a single transaction. **Picked.** Phase 4 forward-compat is the dominant constraint; the perf overhead is <1ms on warm connections.
- **(C) Per-statement binding via raw-SQL prepend** — concatenate the SET LOCAL into the same command string. **Rejected** because of SQL-injection risk (the tenant id is user-derived; even though it's a GUID after middleware enrichment, prepending raw-SQL is the wrong pattern) and EF parameterization confusion (the prepended command isn't an EF-prepared command, breaking the parameter mapping).

**Per-statement semantics — what fires when**:

- A simple SELECT through the DbContext: interceptor fires SET LOCAL → command runs → SET LOCAL goes out of scope at the next transaction boundary (which is the same statement, since no explicit transaction = autocommit).
- A SaveChangesAsync with N entity changes: EF wraps the writes in a transaction; the interceptor fires SET LOCAL once at the start of the transaction (actually, before each command — but the transaction makes the SET LOCAL durable for the whole batch).
- An explicit `IUnitOfWork.BeginTransactionAsync` (verified `BaseDbContext.cs:80-84`): the transaction is open; every command inside it gets the SET LOCAL prefix; the SET LOCAL takes effect for the rest of the transaction. **Subtle**: if the same transaction switches tenant binding mid-flight (Phase 4 OTA), the second SET LOCAL overrides the first for subsequent commands in the same transaction. This is exactly the property Phase 4 needs.

**Decision: per-statement `SET LOCAL app.tenant_id = '<guid>'` via a `DbCommandInterceptor` that prepends the SET LOCAL as a separate command on the same connection. Locked by MASTER_PLAN + PHASE_3_RECONNAISSANCE; Phase 4 forward-compat is the load-bearing constraint.**

### 4.2 D2 — `current_setting(name, true)` — the missing-OK second argument

PostgreSQL's `current_setting(setting_name, missing_ok)` signature: when the setting is unset, the second argument controls behavior. With `missing_ok = true`, the function returns empty string (not throws). Without it (the single-arg form), an unset GUC raises `unrecognized configuration parameter "app.tenant_id"` and the policy SQL itself blows up.

**Why this is load-bearing**:

- Migrator role: the migrator runs `dotnet ef database update` via `VrBook.Migrator`; the migration code runs BEFORE the interceptor exists (the migrator wires a slim service set without `ICurrentUser` HTTP awareness). The migrator's connection has no GUC set. With `current_setting` single-arg, every migration query would crash on the policy SQL.
- Worker bootstrap: the Sync worker registers `AnonymousCurrentUser` (verified `Workers.Sync/Program.cs:45`); the interceptor would set `app.tenant_id = ''`. Without missing-OK, the policy SQL's `current_setting` would not throw (we ARE setting it, just to empty string) — but then `''::uuid` would throw. Hence the `NULLIF(current_setting(...), '')::uuid` wrapping in §3.4.
- Health-check endpoint: anonymous `GET /health` opens a DbContext (verified — the existing `IHealthCheck` reads the schema-version table). The connection has no GUC set; the policy SQL must not crash.

**Decision: every `current_setting()` call in M.9 policies passes `missing_ok = true` (the `, true` second arg). Combined with `NULLIF(..., '')::uuid` for the tenant-id cast, the policy gracefully handles unset + empty-string + valid cases.**

### 4.3 D3 — How the GUC gets set: `DbCommandInterceptor`, per-command pre-issue

Three implementation options:

- **(a) DbConnection wrapper** — subclass `NpgsqlConnection`, override `OpenAsync`, fire SET on open. **Rejected** because EF Core 8 doesn't make `NpgsqlConnection` easily wrappable without reaching into Npgsql internals (the `UseNpgsql(connectionString)` path constructs the connection from the pool; subclassing requires `UseNpgsql(NpgsqlDataSource)` and a custom data source. Plus the per-connection semantics conflict with D1's per-statement requirement).
- **(b) DbCommandInterceptor** — override `ReaderExecutingAsync` / `NonQueryExecutingAsync` / `ScalarExecutingAsync`; for each, issue `SET LOCAL` as a separate command on the same connection/transaction before the actual command. **Picked.** Per-statement semantics; composes with EF transactions; no Npgsql-internal coupling.
- **(c) SaveChangesAsync override** — set the GUC inside `BaseDbContext.SaveChangesAsync`. **Rejected** because it only covers writes; reads (the entire query surface) go through different paths.

**Implementation sketch**:

```csharp
// src/VrBook.Infrastructure/Persistence/TenantGucCommandInterceptor.cs

using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

internal sealed class TenantGucCommandInterceptor(
    ICurrentUser currentUser,
    ILogger<TenantGucCommandInterceptor> logger) : DbCommandInterceptor
{
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await StampTenantGucsAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await StampTenantGucsAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await StampTenantGucsAsync(command, cancellationToken);
        return result;
    }

    private async Task StampTenantGucsAsync(DbCommand command, CancellationToken ct)
    {
        // D4 — read the AsyncLocal bypass flag set by IRlsBypassDbContextFactory.
        var bypass = RlsBypassScope.IsActive;

        var tenantId = currentUser.TenantId;
        var tenantGuc = tenantId?.ToString("D") ?? string.Empty;
        var bypassGuc = bypass ? "true" : "false";

        // Issue SET LOCAL as a sibling command on the same connection +
        // transaction. The SET LOCAL inherits the ambient transaction;
        // if none exists yet, EF will autocommit and the SET LOCAL is moot
        // for the real command — so we MUST execute SET LOCAL together
        // with the real command in the same transaction. For an autocommit
        // path (no explicit BeginTransactionAsync), the SET LOCAL inside
        // the same statement batch works because Postgres treats the batch
        // as a single transaction.
        //
        // We use parameterized SQL even for the GUC values, even though
        // they're not user-supplied at this layer — for defense in depth.

        using var setCmd = command.Connection!.CreateCommand();
        setCmd.Transaction = command.Transaction;
        setCmd.CommandText =
            "SELECT set_config('app.tenant_id', @tenant_id, true), " +
            "       set_config('app.is_platform_admin', @bypass_flag, true);";

        var tenantParam = setCmd.CreateParameter();
        tenantParam.ParameterName = "@tenant_id";
        tenantParam.Value = (object?)tenantGuc ?? DBNull.Value;
        setCmd.Parameters.Add(tenantParam);

        var bypassParam = setCmd.CreateParameter();
        bypassParam.ParameterName = "@bypass_flag";
        bypassParam.Value = bypassGuc;
        setCmd.Parameters.Add(bypassParam);

        await setCmd.ExecuteNonQueryAsync(ct);

        logger.LogDebug(
            "RLS GUC stamped: tenant_id={TenantId}, is_platform_admin={Bypass}",
            tenantGuc, bypassGuc);
    }
}
```

**`set_config('name', 'value', is_local)` vs `SET LOCAL name = value`**: `set_config(..., true)` is the function form of `SET LOCAL`. Parameterized, safe, identical semantic. We pick the function form for SQL-injection defense in depth.

**Implementation alternative considered + rejected**: prepending `SET LOCAL` to `command.CommandText`. Rejected because (i) EF parameter binding gets confused, (ii) SQL injection risk if the GUC value were ever user-derived, (iii) the prepend doesn't compose with multi-statement commands well.

**Decision: a `DbCommandInterceptor` that pre-issues a parameterized `set_config(..., is_local=true)` for both `app.tenant_id` and `app.is_platform_admin` on the same connection + transaction as the intercepted command. AsyncLocal `RlsBypassScope` carries the bypass-active flag from the factory to the interceptor.**

### 4.4 D4 — `IRlsBypassDbContextFactory<TContext>` contract shape

```csharp
// src/VrBook.Contracts/Interfaces/IRlsBypassDbContextFactory.cs

namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Slice OPS.M.9 §4.4 (D4) — opens a fresh DbContext whose connection runs
/// with <c>app.is_platform_admin = 'true'</c> set per-statement, allowing
/// cross-tenant reads that the RLS policies otherwise deny.
///
/// <para>Lifecycle: caller MUST <c>await using</c> the returned context.
/// The bypass flag is scoped via AsyncLocal to the lifetime of the
/// returned context (not a static); concurrent bypass contexts on the
/// same logical thread are stacked (each `using` block pops one frame).</para>
///
/// <para>Allowed call sites are enumerated in <c>docs/OPS_M_9_PLAN.md</c>
/// §7; the <c>RlsBypassCallSiteAllowlistTests</c> arch test pins the
/// allow-list. Adding a new bypass call site is a deliberate design
/// review.</para>
/// </summary>
public interface IRlsBypassDbContextFactory<TContext> where TContext : DbContext
{
    /// <summary>
    /// Opens a fresh bypass-flagged DbContext. The <paramref name="reason"/>
    /// is captured into a structured log line (level <c>Information</c>)
    /// every invocation for after-the-fact audit. Treat it like a
    /// commit-message — short, action-oriented, identifies the caller.
    /// </summary>
    Task<TContext> CreateForBypassAsync(string reason, CancellationToken ct = default);
}
```

**`RlsBypassScope` AsyncLocal helper** (sketch):

```csharp
// src/VrBook.Infrastructure/Persistence/RlsBypassScope.cs

internal static class RlsBypassScope
{
    private static readonly AsyncLocal<int> _depth = new();
    public static bool IsActive => _depth.Value > 0;

    public static IDisposable Enter()
    {
        _depth.Value++;
        return new ExitScope();
    }

    private sealed class ExitScope : IDisposable
    {
        public void Dispose() => _depth.Value = Math.Max(0, _depth.Value - 1);
    }
}
```

**Per-module factory impl** (sketch for Identity; same shape per module):

```csharp
// src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/RlsBypassIdentityDbContextFactory.cs

internal sealed class RlsBypassIdentityDbContextFactory(
    IDbContextFactory<IdentityDbContext> inner,
    ILogger<RlsBypassIdentityDbContextFactory> logger)
    : IRlsBypassDbContextFactory<IdentityDbContext>
{
    public async Task<IdentityDbContext> CreateForBypassAsync(string reason, CancellationToken ct = default)
    {
        logger.LogInformation(
            "RLS bypass open for IdentityDbContext (reason={Reason}, actor=server)",
            reason);

        var scope = RlsBypassScope.Enter();
        var ctx = await inner.CreateDbContextAsync(ct);
        return new BypassScopedDbContext<IdentityDbContext>(ctx, scope);
    }
}

internal sealed class BypassScopedDbContext<TContext> : DbContext where TContext : DbContext
{
    // Wrapper that disposes both the inner DbContext and the AsyncLocal scope on dispose.
    // … implementation detail …
}
```

**Why AsyncLocal-stack instead of per-context flag**: the AsyncLocal-stack lets a caller open a bypass context, run multiple awaits, and the interceptor (which fires on every command on every DbContext on this logical async thread) sees the bypass flag. The alternative — a flag on the DbContext itself — would require the interceptor to read the DbContext metadata, which is awkward through the `CommandEventData` API. The AsyncLocal is the cleanest carry-channel.

**Caveat: the AsyncLocal bypass is logical-thread-scoped, not DbContext-scoped**. If a bypass-scoped block runs `await someOtherService.DoUnrelatedReadAsync()` and the unrelated service uses a *different* DbContext (a request-scoped one), the bypass flag will leak to that unrelated read. This is a sharp edge. The guard rail (§9 item 2) is "the bypass DbContext is short-lived: open → query → dispose. NEVER hold across an unrelated await." The arch test in §7 enforces by limiting bypass-factory injection sites.

**Decision: `IRlsBypassDbContextFactory<TContext>.CreateForBypassAsync(reason, ct)` returns an `await using`-safe DbContext that activates the bypass via AsyncLocal stack. The bypass flag flows through the interceptor on every command on the logical async thread until the returned context is disposed. Reason string is logged at Information level.**

### 4.5 D5 — Background worker bypass

The Sync worker (verified `Workers.Sync/Program.cs:74-80`) does two distinct things:

1. **Bootstrap**: `feeds.ListDueForPollAsync(now)` enumerates due channel feeds across every tenant. Cross-tenant by nature.
2. **Per-feed processing**: for each due feed, dispatch `RunSyncForFeedCommand(feed.Id, feed.TenantId)`; the command implements `IBackgroundCommand` + `ITenantScoped`; the M.6 `BackgroundCommandTenantScopeBehavior` asserts `feed.TenantId != Guid.Empty`; the per-statement interceptor stamps the GUC to `feed.TenantId` for all DbContext commands inside the handler.

**M.9 changes**:

- The bootstrap reads switches to the bypass factory: `await using var db = await bypassFactory.CreateForBypassAsync("sync-worker.list-due-feeds")`. The interceptor sees `RlsBypassScope.IsActive == true` and stamps `app.is_platform_admin = 'true'`.
- Per-feed processing is unchanged. The interceptor reads `ICurrentUser.TenantId` from the request-scoped `ICurrentUser` (which in the worker is `AnonymousCurrentUser` — returning `null`!). **This is a problem**: the M.6 worker's tenant scoping is achieved by stamping `tenant_id` into the LOG SCOPE via `Serilog.Context.LogContext.PushProperty("tenant_id", feed.TenantId)` (verified `Workers.Sync/Program.cs:96`) and into the COMMAND `TenantId` field — but `AnonymousCurrentUser` always returns `null` for `TenantId`, so the interceptor would stamp the GUC to `''` and the per-feed DbContext reads would be DB-denied.

**Fix**: the M.6 `BackgroundCommandTenantScopeBehavior` already does the right thing at the COMMAND layer — but the DbContext-layer interceptor needs to know the tenant id of the current background command. M.9 introduces a complementary mechanism: an `AsyncLocal<Guid?> CurrentBackgroundTenantId` that the `BackgroundCommandTenantScopeBehavior` sets at the start of each background command and clears at the end. The interceptor reads `CurrentBackgroundTenantId` when `ICurrentUser.TenantId == null && !RlsBypassScope.IsActive` and uses it as the tenant GUC. This is the "worker tenant inherits from the IBackgroundCommand's TenantId" property.

**Alternative considered and rejected**: register a `BackgroundCurrentUser` instead of `AnonymousCurrentUser` in the worker, with `TenantId` materialized from the current command. Rejected because `ICurrentUser` is a per-request abstraction; the worker handles many commands per process; switching the registration introduces lifetime complexity for `ICurrentUser` (would need scoped re-injection per command).

**Picked**: AsyncLocal `BackgroundTenantScope` analogous to `RlsBypassScope`. The M.6 behavior wraps each `Handle` call in a `using var scope = BackgroundTenantScope.Enter(feed.TenantId)`. The interceptor's fallback chain becomes:

1. If `RlsBypassScope.IsActive`, GUC = bypass flag = true.
2. Else if `ICurrentUser.TenantId` has a value, GUC = that.
3. Else if `BackgroundTenantScope.CurrentTenantId` has a value, GUC = that.
4. Else, GUC = empty string (the deny-all case for tenant-scoped tables; harmless for non-tenant-scoped reads).

**M.6 retro-fit cost**: ~30 lines in `BackgroundCommandTenantScopeBehavior.cs` to push/pop the scope. M.9 owns this change.

**Decision: the Sync worker uses the bypass factory for the bootstrap `ListDueForPollAsync` call; per-feed processing uses the normal DbContext but with the M.9-added `BackgroundTenantScope.Enter(feed.TenantId)` flowing through the interceptor. The interceptor reads the scope as a fallback after `ICurrentUser.TenantId`.**

### 4.6 D6 — Stripe webhook handler bypass

The handler (verified `HandleStripeWebhookCommand.cs:55-94`):

1. Verifies the Stripe signature.
2. Parses the JSON, extracts the `account` field (the connected account id).
3. Idempotency check on `(stripe_event_id, stripe_account_id)`.
4. Resolves tenant from account id via `tenantStripe.GetByStripeAccountAsync(stripeAccountId, ct)` (line 83) — **the bypass call**.
5. Stamps the tenant id on the `WebhookEvent` row.
6. Saves changes.

**M.9 changes**:

- Step 4 switches to the bypass factory: the `TenantStripeContextLookup.GetByStripeAccountAsync` is rewired to inject `IRlsBypassDbContextFactory<IdentityDbContext>` and use the bypass DbContext for the cross-tenant query.
- Steps 5 + 6 are unchanged. After the tenant is resolved, the `WebhookEvent` row write uses the request-scoped `PaymentDbContext` (which the per-statement interceptor stamps with the resolved tenant id via — wait, the webhook endpoint is anonymous from Stripe's perspective; there's no `ICurrentUser.TenantId` set).

**Subtle**: the webhook endpoint is `[AllowAnonymous]`-shaped (Stripe POSTs with no Authorization header; signature verification is the auth mechanism). After the handler resolves the tenant id, the `WebhookEvent` row write needs to land. Without a tenant GUC set, the RLS policy on `payment.webhook_events` allows the write because the policy permits `tenant_id IS NULL` (D12). But the M.9 plan is to stamp `tenant_id` on the row before save — so the row carries a non-null tenant id, and the policy's `WITH CHECK` clause must permit it.

**Resolution**: after the bypass-factory call resolves the tenant id, the handler stamps the tenant id onto the `WebhookEvent` row AND opens a second, short-lived bypass scope around `db.SaveChangesAsync(...)` — OR (cleaner) the entire `HandleStripeWebhookHandler` runs inside one bypass scope from start to finish. **Pick the simpler shape**: wrap the whole handler in a bypass scope. The handler is platform-level; it's the right semantic.

Concretely: `HandleStripeWebhookHandler` constructor injects `IRlsBypassDbContextFactory<PaymentDbContext>` AND `IRlsBypassDbContextFactory<IdentityDbContext>`; the handler opens both at the start and uses them through the handler's body. The Identity bypass is for the `TenantStripeContextLookup` read; the Payment bypass is for the `WebhookEvent` row write. Audit + log lines record the bypass with reason "stripe-webhook".

**Decision: `HandleStripeWebhookHandler` opens two bypass-scoped DbContexts at the start of its body (one Identity for tenant resolution, one Payment for the `WebhookEvent` write). The handler's body runs entirely under bypass; the resolved tenant id is stamped onto the row but the policy lets it through via the bypass flag. Reason string = "stripe-webhook".**

### 4.7 D7 — Where the per-request `SET LOCAL app.tenant_id` fires

Per D1 + D3: inside the `TenantGucCommandInterceptor` at command execution time. The interceptor reads `ICurrentUser.TenantId` from the request-scoped `ICurrentUser`; if null, the GUC is set to empty string.

**Critical detail**: the interceptor MUST run BEFORE the `DomainEventOutboxInterceptor` for writes, otherwise the outbox row insertion could fire under the wrong tenant context. EF Core 8 interceptor ordering is registration order; the registration order in `BaseDbContext`'s `UseOutbox(sp)` extension is currently outbox-only. **M.9 prepends the `TenantGucCommandInterceptor` to the interceptor list** so it fires first on every command.

**Race conditions considered**:

- Two concurrent requests on the same connection (Npgsql connection pool reuse): the per-statement `SET LOCAL` is transaction-scoped, so the second request gets its own SET LOCAL inside its own transaction (or autocommit single statement). No leak.
- Pipelined commands within one transaction: EF Core 8 doesn't pipeline commands within a transaction; each command awaits the previous. The interceptor fires per command. No leak.

**Decision: the `TenantGucCommandInterceptor` is registered first in every module's interceptor list (via `UseRlsTenantGuc(sp)` extension) and fires on every command on every request-scoped DbContext. Reads `ICurrentUser.TenantId` → falls back to `BackgroundTenantScope.CurrentTenantId` (D5) → falls back to empty string.**

### 4.8 D8 — Outbox-write path

The `DomainEventOutboxInterceptor` (verified `src/VrBook.Infrastructure/Outbox/DomainEventOutboxInterceptor.cs:31-92`) inserts `OutboxMessage` rows in the same transaction as the aggregate write. The outbox tables do NOT carry `tenant_id` and are NOT RLS-protected (§3.2 row 4-12).

**Does this leak tenant context?** No. The outbox table is a write-side surface; the outbox relay worker reads it cross-module to dispatch events; the events themselves carry their tenant id in the payload (per OPS.M.4 D6 — every event payload carries `Guid TenantId`). So a downstream consumer reads the event payload and re-stamps its own DbContext with that tenant id (via the M.6 background-tenant-scope mechanism, D5) before processing.

**Decision: outbox is RLS-exempt by design (no `tenant_id` column). Event payloads carry the tenant id; downstream consumers re-stamp via the background-tenant-scope.**

### 4.9 D9 — Migration policy naming convention

- Policy name: `rls_<schema>_<table>_tenant_isolation` — exact pattern. Inventoried in §3.1.
- Migration file name: `OpsM9a_<Module>_RlsPolicies.cs` — one file per module containing the policies for every tenant-scoped table in that module.
- Future tables: the same convention. Documented in `docs/runbooks/rls-diagnose.md` so the next slice's author knows to follow.

**Why not one policy per table per file?** Per-module bundling matches the OPS.M.3 precedent (one `OpsM3a_<Module>_TenantIdColumn` per module, even when multiple tables touched). Fewer migration files = simpler ordering.

**Decision: `rls_<schema>_<table>_tenant_isolation` policy name; one file per module; pattern documented in the runbook.**

### 4.10 D10 — Read-side-only tables (no RLS)

Inventoried in §3.2. Per D11 + D10:

- `identity.users` — platform-level, no tenant scope.
- `identity.tenants` — IS the tenant; root aggregate. Cross-tenant read via bypass factory (the M.8 platform endpoints + the webhook handler's lookup).
- `identity.tenant_memberships` — bootstrap-path read; cannot be RLS-protected without breaking the middleware.
- Per-module `outbox_messages` — system event log; no tenant column.
- Reference tables (`catalog.amenities`, `catalog.house_rules`, `pricing.fees`) — shared vocabulary; no tenant column.
- Child tables that inherit tenant scope from a parent (`booking.line_items`, `booking.guests`) — see §3.3 verification flag.

**Arch test (§7)**: enumerates the §3.1 inventory and asserts each table has an RLS policy migration; enumerates §3.2 and asserts those tables do NOT have a policy. The arch test is the canonical inventory check — if §3.1 / §3.2 drifts from reality, the test catches it.

**Decision: §3 inventory is the canonical list; the arch test in §7 pins it.**

### 4.11 D11 — `tenant_memberships` carve-out (deep dive)

The `UserProvisioningMiddleware` (verified `UserProvisioningMiddleware.cs:68-71`):

```csharp
var memberships = await db.Set<TenantMembership>()
    .Where(m => m.UserId == userId && m.DeletedAt == null)
    .Select(m => new { m.TenantId, m.Role, m.IsPrimary })
    .ToListAsync(ctx.RequestAborted);
```

This runs BEFORE the `app_tenant_id` claim is materialized. If `tenant_memberships` had an RLS policy `USING (tenant_id = current_setting('app.tenant_id', true)::uuid OR ...)`, the GUC `app.tenant_id` would be empty at this point (the middleware is the thing that sets it). The policy filter would be `tenant_id = NULL` which is always false; the query would return zero rows; the middleware would fail to enrich claims; the user would be permanently unauthenticated.

**Resolutions considered**:

- **(a) Run the middleware reads under the bypass scope** — wrap the middleware in `RlsBypassScope.Enter()`. Pro: the policy can exist on `tenant_memberships`. Con: every authenticated request now does a bypass; the bypass count metrics become useless. Plus, the bypass is supposed to be a deliberate per-call-site mechanism, not a request-wide flag.
- **(b) Exempt `tenant_memberships` from RLS** — no policy, no enforcement at DB level. Pro: middleware works unchanged. Con: a SQL-injection-or-bug could read another user's memberships. Mitigation: every code-side read of `tenant_memberships` filters by `WHERE user_id = @currentUser.UserId`; the app-layer is the only protection.
- **(c) Stamp the tenant GUC from the membership table at middleware time** — open a bypass-scoped read for the membership query specifically, then stamp the GUC. Pro: the policy can exist; bypass is scoped to the one query. Con: complex; requires the middleware to know about RLS internals; couples middleware to M.9 mechanics.

**Picked: (b) — `tenant_memberships` is exempt from RLS in M.9.** Justification:

1. The table contains `(user_id, tenant_id, role, is_primary)` rows. A cross-tenant leak would tell user X which tenants user Y is a member of — a privacy issue but not a data-confidentiality breach.
2. Every read site is gated app-layer by `WHERE user_id = @currentUser.UserId` (the middleware) or `WHERE tenant_id = @scopedTenantId` (admin endpoints reading their own tenant's memberships). The M.10 test pack verifies every read site.
3. The bootstrap-path constraint is real and load-bearing; complicating the middleware to solve a privacy concern that's already app-layer-mitigated is the wrong trade.
4. Phase 2 hardening can revisit if/when per-tenant DB roles ship (the membership table could be moved to a per-tenant schema then).

**Decision: `tenant_memberships` exempt from RLS in M.9; app-layer enforcement at every read site is the protection; M.10 test pack verifies. Future hardening can revisit in Phase 2.**

### 4.12 D12 — `webhook_events`, `audit_log`, `notification_log` — nullable-`tenant_id` policy shape

All three tables allow `tenant_id IS NULL`:

- `payment.webhook_events`: orphan Stripe events (account unknown at receive time).
- `identity.audit_log`: super-admin actions + anonymous-session events.
- `notifications.notification_log`: guest-facing notifications (the recipient is a guest, not a tenant).

**Policy shape per D12**: the policy accepts NULL OR matching GUC OR bypass:

```sql
USING (
    tenant_id IS NULL
    OR tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    OR current_setting('app.is_platform_admin', true) = 'true'
)
```

**Why allow `IS NULL` in the policy?** Without it, a non-bypass session would never read the orphan rows. The Stripe webhook handler's `seen` check (`AnyAsync(... StripeEventId == ...)` — verified `HandleStripeWebhookCommand.cs:64-67`) reads the table cross-tenant by idempotency key; if the row's `tenant_id` is null (orphan), a tenant-scoped session would not see it (the `IS NULL OR tenant_id = …` clause would not match without `IS NULL`). But the webhook handler runs under the bypass per D6, so this is academic. Still: defense in depth; a future code path that legitimately reads a null-tenant row from a tenant-scoped session shouldn't be a footgun.

**For audit_log**: a future audit-log read endpoint (M.8 §1.2 Open Question O2 — deferred) would page across both null-tenant (PlatformAdmin actions) and tenant-scoped (Owner actions) rows. The policy permits both shapes; the application enforces "Owner sees their own tenant's audit rows + null-tenant PlatformAdmin actions targeting their tenant".

**Decision: nullable-`tenant_id` tables (`webhook_events`, `audit_log`, `notification_log`) carry the three-branch policy `tenant_id IS NULL OR tenant_id = GUC OR bypass`. The `WITH CHECK` clause uses the same shape so inserts/updates with null tenant ids are permitted.**

### 4.13 D13 — Migration ordering across modules

All 9 migration files ship in one tag. The order within the tag doesn't matter — each migration is local to its own module's schema and `__ef_migrations_history` table. The migrator applies them in alphabetical-timestamp order (the existing OPS.M.3 pattern).

**Cross-module dependencies**: none. Each module's `OpsM9a_<Module>_RlsPolicies` migration touches only its own schema. No cross-schema FK additions; no cross-schema policy references.

**Decision: 9 migrations, one tag, no cross-module ordering constraint.**

---

## 5. Step-by-step TDD plan (Red → Green)

Every step is red-first. Red commit + green commit are tracked in the §11 ledger. Estimates assume one engineer; total is ~12 hours of effective work (1.5 days at 8h/day).

### 5.0 TDD discipline reminder (carried from prior plans)

Every step follows the cycle:
1. Write the failing test(s) — RED commit. CI MUST fail.
2. Write the minimum impl to make the test(s) pass — GREEN commit. CI MUST pass.
3. (Optional) Refactor — REFACTOR commit. CI MUST stay passing.

The §11 ledger tracks all three commits per step. The arch tests in Steps 5 + 12 + 11.5 are RED-on-add (assertion against a schema/code shape that doesn't exist yet) — the engineer adds the test first, sees it fail, then adds the policy / impl / allow-list.

### 5.0.1 Order rationale

Wave 1 (Steps 1-4) ships the mechanism BEFORE any policy is enabled. The interceptor + factory + DI wiring are exercised in isolation; if Wave 1 has a bug, no production data is at risk because the policies aren't activated yet.

Wave 2 (Steps 5-13) activates the policies AND the call-site rewires. The wave is atomic — if the policy migration applies but the call-site rewire is missing, the M.8 platform endpoints would return zero rows. Single-tag deploy is the discipline.

### 5.0.2 Pre-Wave-1 readiness check

Before Step 1 starts:
- Verify the test fixture base (`TenantIdRolloutFixture`) is working in CI. M.9 extends it.
- Verify the migrator role's current `BYPASSRLS` state in dev DB (`SELECT rolbypassrls FROM pg_roles WHERE rolname = 'vrbook_migrator';`). If absent, Step 6 will grant; if present, Step 6 is a no-op.
- Verify the §3 inventory by sub-agent recon (already done in this plan's preparation).
- Confirm no in-flight PR is adding a new tenant-scoped table (would need to merge first OR be added to M.9 scope).

### Step 1 — `TenantGucCommandInterceptor` skeleton + tests (M, ~2h) — Wave 1

**Tests (red first)** — `tests/VrBook.Api.IntegrationTests/Rls/TenantGucInterceptorTests.cs`:

- `Interceptor_emits_SET_LOCAL_app_tenant_id_before_every_query` — drive a SELECT through any module's DbContext; capture Postgres logs (testcontainer `log_statement = 'all'`); assert the `set_config('app.tenant_id', '<guid>', true)` call appears before the SELECT.
- `Interceptor_emits_SET_LOCAL_app_is_platform_admin_before_every_query` — same shape; assert both GUCs land in the same set_config batch.
- `Interceptor_uses_AsyncLocal_bypass_flag_when_RlsBypassScope_is_active` — open a `RlsBypassScope.Enter()`; run a query; assert `app.is_platform_admin = 'true'` in the log capture.
- `Interceptor_uses_empty_tenant_id_when_ICurrentUser_returns_null_and_no_BackgroundTenantScope` — register `AnonymousCurrentUser`; run a query; assert `app.tenant_id = ''` (empty string).
- `Interceptor_falls_back_to_BackgroundTenantScope_when_ICurrentUser_TenantId_is_null` — register `AnonymousCurrentUser`; open a `BackgroundTenantScope.Enter(guid)`; run a query; assert `app.tenant_id = <guid>`.

**Min implementation**:

1. `src/VrBook.Infrastructure/Persistence/TenantGucCommandInterceptor.cs` per §4.3 sketch.
2. `src/VrBook.Infrastructure/Persistence/RlsBypassScope.cs` per §4.4 sketch.
3. `src/VrBook.Infrastructure/Persistence/BackgroundTenantScope.cs` (symmetric AsyncLocal with `Enter(guid)` returning IDisposable).
4. `src/VrBook.Infrastructure/Persistence/DbContextOptionsBuilderExtensions.cs` — extend with `UseRlsTenantGuc(this DbContextOptionsBuilder b, IServiceProvider sp)` that registers the interceptor singleton + adds it to the options.

**Refactor**: none.

**§3 / §4 cross-reference**: §4.1 D1, §4.2 D2, §4.3 D3.

**Pitfalls**:
- The `set_config` command must run on the SAME `DbCommand.Transaction` as the intercepted command. If it runs on a fresh connection (e.g. by calling `command.Connection.OpenAsync()`), the GUC is set on a different physical connection and doesn't apply. Verified by the Step 1 test capturing connection process id.
- AsyncLocal `RlsBypassScope` must be re-entrant — nested bypass scopes (one factory call inside another's `using` block) should both work. The depth counter shape in §4.4 handles this.
- The `BackgroundTenantScope` AsyncLocal must NOT survive across `Task.Run` boundaries by default. AsyncLocal does survive (that's the contract); test that the worker's per-feed dispatch doesn't accidentally inherit a stale scope from a previous iteration. Mitigation: the M.6 behavior wraps `using var scope = ... .Enter(feed.TenantId)` at the start of EACH `Handle` call; the iteration boundary is the disposal point.

### Step 2 — `IRlsBypassDbContextFactory<TContext>` contract + per-module impls (S, ~1.5h) — Wave 1

**Tests (red first)** — `tests/VrBook.Architecture.Tests/RlsBypassFactoryRegistrationTests.cs`:

- `Every_module_with_a_tenant_scoped_DbContext_registers_an_IRlsBypassDbContextFactory_impl` — reflect on every module's `AddXxxModule` extension; for each `AddDbContext<TContext>` call, assert the same module's DI also has `services.AddScoped<IRlsBypassDbContextFactory<TContext>, RlsBypass<Module>DbContextFactory>()`.
- `Every_module_with_a_tenant_scoped_DbContext_registers_AddDbContextFactory<TContext>` — reflection check.
- `IRlsBypassDbContextFactory_contract_has_CreateForBypassAsync_with_reason_parameter` — reflect on the contract; assert the method signature.

**Min implementation**:

1. `src/VrBook.Contracts/Interfaces/IRlsBypassDbContextFactory.cs` per §4.4 sketch.
2. One impl per module: `src/Modules/VrBook.Modules.<Module>/Infrastructure/Persistence/RlsBypass<Module>DbContextFactory.cs`. 9 files.
3. Each module's `Add<Module>Module` extension adds `.AddDbContextFactory<TContext>(...)` AND `services.AddScoped<IRlsBypassDbContextFactory<TContext>, RlsBypass<Module>DbContextFactory>()`.

**Refactor**: extract a single generic base class `RlsBypassDbContextFactoryBase<TContext>` if the 9 impls duplicate (they do — only the type differs). Move to `VrBook.Infrastructure.Persistence`. Per-module sealed sub-classes are 5-line stubs.

**§3 / §4 cross-reference**: §4.4 D4.

**Pitfalls**:
- `IDbContextFactory<TContext>` requires `AddDbContextFactory<TContext>(...)` which creates a SECOND pool of DbContexts beside the existing `AddDbContext<TContext>(...)`. This is two configurations to keep in sync (both need the interceptor). The §6.5 template centralizes the options via a local `Action<DbContextOptionsBuilder>` to dedupe.
- `RlsBypassDbContextFactoryBase`'s `CreateForBypassAsync` must dispose the AsyncLocal scope if the inner `CreateDbContextAsync` throws (try-catch around scope acquisition). Else a failed factory call leaves a phantom bypass-active flag on the logical thread.
- The bypass wrapper's `DisposeAsync` must dispose the inner context FIRST then the scope. If the order is reversed, a final outbox flush at dispose time would run without the bypass and could fail RLS.

### Step 3 — `BackgroundCommandTenantScopeBehavior` wiring of `BackgroundTenantScope` (XS, ~30min) — Wave 1

**Tests (red first)** — `tests/VrBook.Modules.Sync.UnitTests/BackgroundCommandTenantScopeBehaviorTests.cs` (extend or new):

- `Behavior_opens_BackgroundTenantScope_with_command_TenantId_before_next_invocation`.
- `Behavior_closes_BackgroundTenantScope_after_next_completes`.
- `Behavior_closes_BackgroundTenantScope_even_on_exception_path`.

**Min implementation**: modify `src/Modules/VrBook.Modules.Sync/Application/Behaviors/BackgroundCommandTenantScopeBehavior.cs` to wrap the `next()` invocation in `using var scope = BackgroundTenantScope.Enter(scoped.TenantId)`.

**Refactor**: none.

**§3 / §4 cross-reference**: §4.5 D5.

**Pitfalls**:
- The scope must wrap the inner `next()` invocation, NOT the whole handler — if a future handler captures `BackgroundTenantScope.CurrentTenantId` and re-uses it after the scope is disposed, the captured value would still be valid (it's a snapshot) but new reads via the interceptor would not see the scope. Document the rule in the behavior's XML doc.
- The scope must clean up on exception — `try { scope = Enter() } finally { scope.Dispose() }` shape. If the behavior throws between `Enter` and the next-call, the scope leaks.

### Step 4 — Module-by-module DI wiring of the interceptor (S, ~1h) — Wave 1

**Tests (red first)** — `tests/VrBook.Api.IntegrationTests/Rls/InterceptorRegistrationTests.cs`:

- `IdentityDbContext_has_TenantGucCommandInterceptor_registered`.
- `CatalogDbContext_has_TenantGucCommandInterceptor_registered`.
- (And so on for every module — 9 facts via `[Theory]`.)

**Min implementation**: each module's `Add<Module>Module` extension chains `.UseRlsTenantGuc(sp)` onto the `UseNpgsql(...).UseOutbox(sp)` chain. ~1 line per module × 9 modules.

**Refactor**: extract a single chained extension `UseVrBookPersistence(sp)` that adds outbox + interceptor + future infrastructure in one call. Per-module call site becomes `.UseVrBookPersistence(sp)`.

**§3 / §4 cross-reference**: §4.7 D7.

**Pitfalls**:
- The interceptor MUST be registered BEFORE `UseOutbox(sp)` to ensure it fires first in the interceptor chain. The `UseRlsTenantGuc(sp)` extension's implementation calls `options.AddInterceptors(sp.GetRequiredService<TenantGucCommandInterceptor>())`; EF Core 8 preserves registration order for interceptors of the same interface type.
- Both `AddDbContext<>` and `AddDbContextFactory<>` registrations must wire the interceptor — otherwise bypass-factory contexts run WITHOUT the interceptor and the `set_config` never fires. Test asserts the interceptor is registered on both.
- The `VrBook.Migrator` module-registration variants (`AddXxxDbContextForMigrator`) MUST NOT wire the interceptor. The migrator role has `BYPASSRLS` and runs with no `ICurrentUser`; an interceptor would call `ICurrentUser.TenantId` which would `NullReferenceException`. Code review pin.

### Step 5 — RLS policy migrations, one per module (M, ~3h) — Wave 2

**Tests (red first)** — `tests/VrBook.Api.IntegrationTests/Rls/RlsPolicySchemaTests.cs`:

For each table in §3.1:

- `<Schema>_<Table>_has_row_level_security_enabled` — `SELECT relrowsecurity FROM pg_class WHERE oid = '<schema>.<table>'::regclass;`.
- `<Schema>_<Table>_has_row_level_security_forced` — `SELECT relforcerowsecurity ...`.
- `<Schema>_<Table>_has_tenant_isolation_policy` — `SELECT polname FROM pg_policy WHERE polrelid = '<schema>.<table>'::regclass AND polname = 'rls_<schema>_<table>_tenant_isolation';`.
- `<Schema>_<Table>_policy_qual_references_app_tenant_id_GUC` — `SELECT pg_get_expr(polqual, polrelid) ... ` and assert substring `app.tenant_id`.

19 tables × 4 facts = 76 facts. Use `[Theory]` with `InlineData` per table.

For each table in §3.2 (carve-outs):

- `<Schema>_<Table>_does_NOT_have_row_level_security_enabled` — assert `relrowsecurity = false`.

**Min implementation**: 9 migration files, one per module, per the §3.4 template. Each module's migration enables RLS on its tenant-scoped tables only.

**Refactor**: extract a `mb.EnableRlsTenantIsolation(schema, table, nullable: false)` helper if the same SQL block recurs (it does, 19 times). Move to `src/VrBook.Infrastructure/Persistence/RlsMigrationBuilderExtensions.cs`. Per-table call becomes one line.

**§3 / §4 cross-reference**: §3.1, §3.4, §4.9 D9, §4.12 D12, §4.13 D13.

**Pitfalls**:
- `FORCE ROW LEVEL SECURITY` is what makes the policies apply to the table owner (the `vrbook` app role owns the tables). Without `FORCE`, the table owner is exempt and the policies do nothing for app queries. This is a load-bearing line in the migration; the Step 5 schema test asserts `relforcerowsecurity = true`.
- The `WITH CHECK` clause is what blocks cross-tenant INSERTs/UPDATEs. Without it, the policy only blocks SELECTs; a malicious or buggy write can land cross-tenant rows. Every policy in §3.4 includes the `WITH CHECK` clause; the Step 5 test asserts.
- Policies on tables that are children of `properties` (e.g. `property_images`) need the same `tenant_id` GUC check — they DO carry their own `tenant_id` (per OPS.M.3 denormalization). Documented in §3.1.
- Postgres caches plan trees; an `ALTER TABLE … ENABLE RLS` invalidates plans on the table, causing a brief re-plan storm. For high-throughput tables (`bookings`, `payment_intents`), schedule the migration during low-traffic window. The Phase 1.5 staging traffic profile makes this academic; production deploy considerations documented in the runbook.

### Step 6 — Migrator role `BYPASSRLS` grant (XS, ~15min) — Wave 2

**Tests (red first)** — `tests/VrBook.Api.IntegrationTests/Rls/MigratorRoleBypassTests.cs`:

- `vrbook_migrator_role_has_BYPASSRLS_attribute` — `SELECT rolbypassrls FROM pg_roles WHERE rolname = 'vrbook_migrator';`.

**Min implementation**: add an idempotent `GRANT BYPASSRLS` SQL line to the migrator bootstrap (or to a new `OpsM9b_MigratorRoleBypassRls.cs` migration that runs at the Identity schema level). Choose the migration shape because it ships through the same migrator pipeline as the policies; the role grant is a deploy-once-per-environment action.

**Refactor**: none.

**§3 / §4 cross-reference**: §2 — "Migrator role caveat".

**Pitfalls**:
- `BYPASSRLS` is a role attribute, not a permission. It survives `ALTER ROLE … RENAME` but is dropped on `DROP ROLE`. Document in the infra runbook so future role recreation includes the grant.
- The `vrbook` (app) role MUST NOT have `BYPASSRLS` — that would defeat the whole RLS layer. The Step 6 test could be extended to assert `vrbook` role's `rolbypassrls = false`; recommended addition during implementation.

### Step 7 — Rewire `TenantStripeContextLookup.GetByStripeAccountAsync` to use the bypass factory (S, ~1h) — Wave 2

**Tests (red first)** — `tests/VrBook.Modules.Identity.UnitTests/Infrastructure/TenantStripeContextLookupBypassTests.cs`:

- `GetByStripeAccountAsync_opens_RlsBypassDbContextFactory_with_reason_stripe_account_lookup`.
- `GetByStripeAccountAsync_returns_null_when_account_unknown` (existing behavior preserved).
- `GetByStripeAccountAsync_returns_TenantStripeContext_when_account_found` (existing behavior preserved).
- `GetByStripeAccountAsync_logs_bypass_open_at_Information_level`.

Integration test — `tests/VrBook.Api.IntegrationTests/Payment/StripeWebhookCrossTenantResolutionTests.cs`:

- `Webhook_with_account_id_for_tenant_A_correctly_resolves_to_tenant_A_under_RLS` — seed tenant A + tenant B; POST a webhook with tenant A's account id; assert the row lands with tenant A's id; tenant A's owner can read it; tenant B's owner cannot.

**Min implementation**: `TenantStripeContextLookup` constructor injects `IRlsBypassDbContextFactory<IdentityDbContext>` instead of (or in addition to) `IdentityDbContext`; both `GetAsync` and `GetByStripeAccountAsync` open a bypass scope. The `GetAsync` path (which reads by tenant id) could in principle skip the bypass if the caller's tenant id matches, but the bypass-factory pattern is simpler — always-bypass for this lookup, accept the overhead.

**Refactor**: none.

**§3 / §4 cross-reference**: §4.6 D6.

**Pitfalls**:
- `TenantStripeContextLookup` is consumed by both the webhook handler (cross-tenant lookup) AND any tenant-scoped path that needs a tenant's Stripe context (same-tenant lookup). The bypass is over-broad for the same-tenant case — but the overhead is one extra log line + an AsyncLocal stack push/pop per call. Acceptable.
- The `TenantStripeContextLookup` impl is `internal sealed`; the bypass factory injection MUST go through constructor. The existing single-DbContext constructor changes shape; the M.5 tests that instantiate this class manually (if any) need to be updated to pass a mock bypass factory.

### Step 8 — Rewire `HandleStripeWebhookHandler` to use bypass for the whole body (S, ~1h) — Wave 2

**Tests (red first)** — `tests/VrBook.Modules.Payment.UnitTests/Application/HandleStripeWebhookBypassTests.cs`:

- `Handler_opens_PaymentDbContext_bypass_for_WebhookEvent_write`.
- `Handler_opens_IdentityDbContext_bypass_for_TenantStripeContext_read` (indirect via the lookup).
- `Handler_succeeds_when_account_resolves_to_tenant_A_and_writes_WebhookEvent_with_tenant_A_id`.
- `Handler_succeeds_when_account_unknown_and_writes_WebhookEvent_with_null_tenant_id` (orphan path).

**Min implementation**: rewire `HandleStripeWebhookHandler` constructor to inject `IRlsBypassDbContextFactory<PaymentDbContext>`; replace the `PaymentDbContext` field with a bypass-opened context inside `Handle`. Reason = `"stripe-webhook"`. The handler body's existing logic is unchanged.

**Refactor**: none.

**§3 / §4 cross-reference**: §4.6 D6.

**Pitfalls**:
- The bypass scope spans the whole `Handle` body. If a future exception path raises before the SaveChanges, the `WebhookEvent` row is not persisted; this is the same behavior as today and is acceptable (Stripe will retry).
- If the handler's downstream dispatch (`DispatchAsync` — verified `HandleStripeWebhookCommand.cs:100`) invokes another MediatR command that has its OWN tenant scoping (e.g. a `MarkPaymentSucceededCommand` that goes through `TenantAuthorizationBehavior`), the M.4 behavior would normally block because `currentUser.TenantId` is null in the webhook context. M.4's `IBackgroundCommand` short-circuit handles this — the downstream commands ARE shaped as `IBackgroundCommand` per OPS.M.6. Verify during Step 8 implementation; if a downstream command is NOT marked, fix it (or document the gap).

### Step 9 — Rewire Sync worker bootstrap to use bypass; per-feed processing unchanged (S, ~1h) — Wave 2

**Tests (red first)** — `tests/VrBook.Workers.Sync.IntegrationTests/SyncWorkerBootstrapBypassTests.cs`:

- `Worker_opens_SyncDbContext_bypass_for_ListDueForPollAsync` (assert via log capture: "RLS bypass open for SyncDbContext (reason=sync-worker.list-due-feeds)").
- `Worker_per_feed_DbContext_reads_run_under_BackgroundTenantScope_with_feed_TenantId` (verify via Postgres log capture: SET LOCAL `app.tenant_id` = feed's tenant id).
- `Worker_under_two_tenant_scenario_processes_feed_A_and_feed_B_independently_without_cross_leak`.

**Min implementation**: `Workers.Sync/Program.cs` — replace the `feeds.ListDueForPollAsync(now)` call with a bypass-factory-opened context; per-feed dispatch unchanged (the M.6 behavior + the M.9 Step 3 scope-wiring handles it).

**Refactor**: none.

**§3 / §4 cross-reference**: §4.5 D5.

**Pitfalls**:
- The `IChannelFeedRepository` constructor takes `SyncDbContext`. If the repository is registered as Scoped in DI (it almost certainly is — verify), then the bypass-scoped repository instantiation must be by hand (per §6.9). DI auto-injection would give the request-scoped DbContext, not the bypass one.
- The bypass scope must close BEFORE the per-feed dispatch loop. Otherwise the per-feed loop's commands run under bypass (cross-tenant) which is wrong — each feed's processing must be tenant-scoped to that feed's tenant. The `await using` block in §6.9 is correctly scoped to JUST the `ListDueForPollAsync` call.
- The feed list is materialized inside the bypass (`due = await feeds.ListDueForPollAsync(now)`); the post-bypass loop iterates over the in-memory list. Each `RunSyncForFeedCommand` opens its own DbContext via DI (request-scoped); the interceptor on that DbContext stamps the GUC = feed's tenant id (via `BackgroundTenantScope`). Verified by the Step 9 test.

### Step 10 — Rewire M.8 `ListPlatformTenantsHandler` + `GetPlatformTenantHandler` + `PlatformTenantStatsLookup` to use bypass (S, ~1h) — Wave 2

**Tests (red first)** — `tests/VrBook.Api.IntegrationTests/Identity/Platform/PlatformTenantHandlersBypassTests.cs`:

- `ListPlatformTenantsHandler_opens_IdentityDbContext_bypass_with_reason_admin_platform_list_tenants`.
- `ListPlatformTenantsHandler_returns_all_tenants_under_RLS` (two-tenant fixture; assert both return).
- `GetPlatformTenantHandler_opens_IdentityDbContext_bypass`.
- `PlatformTenantStatsLookup_opens_IdentityDbContext_bypass`.

**Min implementation**: three handler/lookup rewires; each switches from constructor-injected `IdentityDbContext` to `IRlsBypassDbContextFactory<IdentityDbContext>` + `await using` inside the method.

**Refactor**: none.

**§3 / §4 cross-reference**: §1.4 row 5, §7.

**Pitfalls**:
- `PlatformTenantStatsLookup` calls `IPropertyCountByTenant.GetCountAsync(tenantId)` (verified) — this is a Catalog-module read. The Catalog-side `PropertyCountByTenant` impl uses `CatalogDbContext` (request-scoped). For the M.8 platform endpoints, the request-scoped CatalogDbContext is stamped with the PlatformAdmin caller's OWN tenant id (or null if they're not an Owner), so the property-count query for tenant B would return zero. **This is the cross-module subtlety flagged in §7.2 row 7.** The fix during Step 10 implementation: `PlatformTenantStatsLookup` opens a bypass `CatalogDbContext` and constructs a fresh `PropertyCountByTenant` impl directly OR the `IPropertyCountByTenant` contract bumps to accept the bypass factory (cleaner long-term but more invasive). **Pick the simpler shape**: open the bypass `CatalogDbContext` in `PlatformTenantStatsLookup.GetAsync` and inline the count query (5 lines of SQL/LINQ). The `IPropertyCountByTenant` contract stays unchanged; the M.8 lookup just doesn't use it for the cross-tenant case.
- The `ListPlatformTenantsHandler` does N+1 property counts (verified M.8 plan §6.9 — accepted at operator-facing volume of ~25 rows per page). Each property count opens its own bypass `CatalogDbContext` — that's 25 bypass-factory calls per list request. Log volume implications: 25 Information-level bypass-open lines per list page hit. Acceptable for Phase 1.5; consider batching in Phase 2.

### Step 11 — Per-module RLS integration fact pack (M, ~2h) — Wave 2

**Tests (red first)** — `tests/VrBook.Api.IntegrationTests/Rls/RlsIsolationFactPack.cs`:

For each tenant-scoped table in §3.1, a `[Theory]` row asserting:

- `Same_tenant_SELECT_returns_seeded_rows` — set `app.tenant_id = <tenant_A>`; SELECT *; assert non-empty.
- `Cross_tenant_SELECT_returns_zero_rows` — set `app.tenant_id = <tenant_B>`; SELECT *; assert empty.
- `Cross_tenant_UPDATE_affects_zero_rows` — set `app.tenant_id = <tenant_B>`; UPDATE … WHERE id = <tenant_A_row_id>; assert `rowsAffected == 0`.
- `Cross_tenant_DELETE_affects_zero_rows` — same shape.
- `Bypass_app_is_platform_admin_true_allows_all_rows` — set `app.is_platform_admin = 'true'`; assert all rows visible.
- `Unset_GUCs_return_zero_rows_without_error` — clear both GUCs; SELECT; assert empty + no exception.

19 tables × 6 facts = 114 facts. Use `[Theory]` with `InlineData` per table.

**Min implementation**: a shared `RlsFixture` extending `TenantIdRolloutFixture` (verified `tests/VrBook.Api.IntegrationTests/Identity/TenantIdRolloutFixture.cs`) seeds two tenants + a representative row per table; the test class drives the policies under various GUC states.

**Refactor**: factor the per-table SQL into a small helper `SetGucAndQuery(schema, table, tenantGuc, bypassGuc) -> rowCount`.

**§3 / §4 cross-reference**: §3.1, §4.12 D12.

**Pitfalls**:
- The test fixture must NOT register the M.9 interceptor (the tests are deliberately exercising RAW Postgres GUC behavior to verify the policy SQL). The fixture connects via a separate Npgsql connection that bypasses the EF interceptor chain entirely; SET LOCAL is fired by the test itself.
- Seeding two tenants requires the migrator role (bypass) AND a follow-up SET to the seeded tenant's id before insert. The fixture's seed method opens a transaction, fires `SELECT set_config('app.is_platform_admin', 'true', true);`, INSERTs the seed rows, COMMITs. Idempotent.
- Some tests assert "zero rows returned" — this distinguishes from "exception raised". The empty-string-cast handling (`NULLIF(..., '')::uuid`) is the load-bearing detail; if the policy used a bare cast, the unset-GUC test would throw `invalid input syntax for type uuid: ""`. The Step 11 `Unset_GUCs_return_zero_rows_without_error` fact is the canary.

### Step 12 — Arch tests pinning bypass call-site allow-list (XS, ~30min) — Wave 2

**Tests (red first)** — `tests/VrBook.Architecture.Tests/RlsBypassCallSiteAllowlistTests.cs`:

- `Every_constructor_injection_of_IRlsBypassDbContextFactory_lives_in_an_allowed_class` — reflect on every type with a constructor parameter of `IRlsBypassDbContextFactory<>`; assert the class is in the explicit allow-list:
  - `TenantStripeContextLookup` (Identity module)
  - `PlatformTenantStatsLookup` (Identity module)
  - `ListPlatformTenantsHandler` (Identity module)
  - `GetPlatformTenantHandler` (Identity module)
  - `HandleStripeWebhookHandler` (Payment module)
  - `Workers.Sync/Program.cs` (Sync worker — flagged differently because it's a `Main` not a class; the arch test detects via assembly scan).

If a new injection appears, the test fails until the engineer adds the class to the allow-list AND the §7 inventory.

- `RlsBypassDbContextFactory_contract_has_AsyncDisposable_returned_TContext_shape` — reflection check.

**Min implementation**: hard-code the allow-list per the §7 inventory; the test source-references the inventory.

**Refactor**: none.

**§3 / §4 cross-reference**: §7, §8.

**Pitfalls**:
- Reflection-based discovery of constructor injections: walk every assembly, every public/internal type, every constructor, every parameter; check if parameter type starts with `IRlsBypassDbContextFactory<`. Use `Assembly.GetTypes()` defensively (some types throw on load).
- The allow-list comparison should be by FullName, not Name — `BookingDbContext` and `IdentityDbContext` differ by namespace; collisions are unlikely but the discipline matters.
- The Sync worker's `Program.cs` is top-level statements — it doesn't have a class to enumerate. The arch test scans the worker assembly's `<Program>$.<Main>$` synthetic type (or alternatively, scans for direct calls to `IRlsBypassDbContextFactory<>.CreateForBypassAsync` via Mono.Cecil / source-text grep). The simpler shape: maintain the allow-list as a hard-coded list in the test; the worker is one explicit entry.

### Step 11.5 — Negative integration tests for the carve-out tables (S, ~30min) — Wave 2

**Tests (red first)** — `tests/VrBook.Api.IntegrationTests/Rls/CarveOutTableSchemaTests.cs`:

For each table in §3.2:

- `<Schema>_<Table>_does_NOT_have_RLS_enabled` — assert `pg_class.relrowsecurity = false` for `outbox_messages` (per module), `users`, `tenants`, `tenant_memberships`, `amenities`, `house_rules`, `fees`, `loyalty.accounts`. ~13 facts via `[Theory]`.

**Why this fact matters**: a future PR that accidentally enables RLS on `tenant_memberships` (e.g. an over-eager hardening PR) would break the bootstrap path. The carve-out test is the canary.

**Min implementation**: extension to `RlsPolicySchemaTests`.

**§3 / §4 cross-reference**: §3.2, §4.10 D10, §4.11 D11.

### Step 13 — Runbook `rls-diagnose.md` (S, ~1h) — Wave 2

**Tests (red first)**: none — runbook is documentation.

**Content**:

1. **Symptom: SELECT returns zero rows when data exists.**
   - Diagnostic: `SHOW app.tenant_id;` — is it set?
   - Diagnostic: `SHOW app.is_platform_admin;` — is bypass active?
   - Diagnostic: `SELECT * FROM pg_policies WHERE tablename = '<table>';` — is the policy in place?
   - Fix: confirm the request reached the interceptor; check the Serilog `Debug` log for "RLS GUC stamped" line.
2. **Symptom: UPDATE / DELETE affects zero rows.**
   - Likely cause: WITH CHECK clause rejected the new tenant_id (a cross-tenant write attempt).
3. **Symptom: `permission denied for table X` on a migrator-run query.**
   - Likely cause: migrator role lost `BYPASSRLS`. Fix: `ALTER ROLE vrbook_migrator BYPASSRLS;`.
4. **Symptom: a Stripe webhook arrived but the resolved tenant looks wrong.**
   - Check: `TenantStripeContextLookup` log line; the bypass should be active.
5. **Operator escape hatch**: how to run an emergency cross-tenant query in production (via the bypass factory, or via a `psql` session with `SET LOCAL app.is_platform_admin = 'true';`).
6. **Adding a new tenant-scoped table**: how to add the RLS policy + update the §3.1 inventory + update the arch test inventory.
7. **Adding a new bypass call site**: the deliberate review process — must update §7 + the arch test allow-list, AND justify in a PR comment.

**Min implementation**: `docs/runbooks/rls-diagnose.md` per the above outline.

**§3 / §4 cross-reference**: §9.

---

## 6. `IRlsBypassDbContextFactory` contract — full code

Per §4.4 D4. Reproduced here in full for the implementor's reference.

### 6.1 The contract

```csharp
// src/VrBook.Contracts/Interfaces/IRlsBypassDbContextFactory.cs

using Microsoft.EntityFrameworkCore;

namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Slice OPS.M.9 §4.4 (D4) — opens a fresh DbContext whose connection runs
/// with <c>app.is_platform_admin = 'true'</c> set per-statement, allowing
/// cross-tenant reads that the RLS policies otherwise deny.
///
/// <para>Lifecycle: caller MUST <c>await using</c> the returned context.
/// The bypass flag is scoped via AsyncLocal to the lifetime of the
/// returned context (not a static); concurrent bypass contexts on the
/// same logical thread are stacked (each <c>using</c> block pops one
/// frame).</para>
///
/// <para>Allowed call sites are enumerated in <c>docs/OPS_M_9_PLAN.md</c>
/// §7; the <c>RlsBypassCallSiteAllowlistTests</c> arch test pins the
/// allow-list. Adding a new bypass call site is a deliberate design
/// review.</para>
/// </summary>
public interface IRlsBypassDbContextFactory<TContext> where TContext : DbContext
{
    /// <summary>
    /// Opens a fresh bypass-flagged DbContext. The <paramref name="reason"/>
    /// is captured into a structured log line (level <c>Information</c>)
    /// every invocation for after-the-fact audit. Treat it like a
    /// commit-message — short, action-oriented, identifies the caller.
    /// </summary>
    Task<TContext> CreateForBypassAsync(string reason, CancellationToken ct = default);
}
```

### 6.2 The per-module impl base

```csharp
// src/VrBook.Infrastructure/Persistence/RlsBypassDbContextFactoryBase.cs

internal abstract class RlsBypassDbContextFactoryBase<TContext>(
    IDbContextFactory<TContext> inner,
    ILogger logger) : IRlsBypassDbContextFactory<TContext> where TContext : DbContext
{
    public async Task<TContext> CreateForBypassAsync(string reason, CancellationToken ct = default)
    {
        logger.LogInformation(
            "RLS bypass open for {ContextType} (reason={Reason})",
            typeof(TContext).Name, reason);

        var scope = RlsBypassScope.Enter();
        try
        {
            var ctx = await inner.CreateDbContextAsync(ct);
            return new BypassScopedDbContextWrapper<TContext>(ctx, scope);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
```

### 6.3 Per-module sealed sub-class (one per module)

```csharp
// src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/RlsBypassIdentityDbContextFactory.cs

internal sealed class RlsBypassIdentityDbContextFactory(
    IDbContextFactory<IdentityDbContext> inner,
    ILogger<RlsBypassIdentityDbContextFactory> logger)
    : RlsBypassDbContextFactoryBase<IdentityDbContext>(inner, logger);
```

5 lines per module. Same pattern for `CatalogDbContext`, `BookingDbContext`, `PaymentDbContext`, `ReviewsDbContext`, `PricingDbContext`, `MessagingDbContext`, `SyncDbContext`, `NotificationsDbContext`.

### 6.4 The `BypassScopedDbContextWrapper`

```csharp
// src/VrBook.Infrastructure/Persistence/BypassScopedDbContextWrapper.cs

internal sealed class BypassScopedDbContextWrapper<TContext>(
    TContext inner, IDisposable bypassScope) : TContext
    where TContext : DbContext
{
    // Override DisposeAsync to drop the bypass scope after the inner DbContext disposes.
    public override async ValueTask DisposeAsync()
    {
        try
        {
            await inner.DisposeAsync();
        }
        finally
        {
            bypassScope.Dispose();
        }
    }
}
```

**Implementation note**: subclassing DbContext is fragile (no easy ctor-pass-through). A cleaner shape is to NOT subclass — return the inner DbContext directly + register a `using` on the caller side that also disposes the scope. The factory returns a tuple-like wrapper `(TContext context, IAsyncDisposable scope)` and the caller `await using` both. Pick whichever feels less brittle in the implementor's editor. The §11 forward-link flags this as a small spike at implementation time.

### 6.5 The DI registration template (per module)

```csharp
// In Add<Module>Module(this IServiceCollection services, IConfiguration cfg):

services.AddDbContext<TContext>((sp, opts) =>
    opts.UseNpgsql(...).UseOutbox(sp).UseRlsTenantGuc(sp));     // M.9 addition

services.AddDbContextFactory<TContext>((sp, opts) =>             // M.9 addition
    opts.UseNpgsql(...).UseOutbox(sp).UseRlsTenantGuc(sp));

services.AddScoped<IRlsBypassDbContextFactory<TContext>,
    RlsBypass<Module>DbContextFactory>();                         // M.9 addition
```

### 6.6 The bypass-aware `Migrator` configuration

The migrator (`src/VrBook.Migrator/Program.cs`) does NOT wire the interceptor — migrations run as the `vrbook_migrator` role which has `BYPASSRLS` granted (per Step 6). The migrator does not need bypass-factory wiring either; raw migrations bypass RLS via the role.

### 6.7 Per-module migration body — full template

Each of the 9 modules ships an `OpsM9a_<Module>_RlsPolicies` migration. Here is the **Booking** module's migration shown in full (the other 8 follow the same shape with their own table lists):

```csharp
// src/Modules/VrBook.Modules.Booking/Infrastructure/Persistence/Migrations/
//   20260628<HHmmss>_OpsM9a_Booking_RlsPolicies.cs

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VrBook.Modules.Booking.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class OpsM9a_Booking_RlsPolicies : Migration
{
    private const string TenantGuc = "current_setting('app.tenant_id', true)";
    private const string BypassGuc = "current_setting('app.is_platform_admin', true)";
    private const string TenantCast = "NULLIF(" + TenantGuc + ", '')::uuid";

    /// <inheritdoc />
    protected override void Up(MigrationBuilder mb)
    {
        // booking.bookings — NOT NULL tenant_id (OPS.M.3c).
        EnableRls(mb, "booking", "bookings", nullable: false);

        // booking.booking_holds — NOT NULL.
        EnableRls(mb, "booking", "booking_holds", nullable: false);

        // booking.availability_blocks — NOT NULL.
        EnableRls(mb, "booking", "availability_blocks", nullable: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder mb)
    {
        DisableRls(mb, "booking", "availability_blocks");
        DisableRls(mb, "booking", "booking_holds");
        DisableRls(mb, "booking", "bookings");
    }

    private static void EnableRls(MigrationBuilder mb, string schema, string table, bool nullable)
    {
        mb.Sql($"ALTER TABLE {schema}.{table} ENABLE ROW LEVEL SECURITY;");
        mb.Sql($"ALTER TABLE {schema}.{table} FORCE ROW LEVEL SECURITY;");

        var nullClause = nullable ? "tenant_id IS NULL OR " : "";
        var policySql = $@"
            CREATE POLICY rls_{schema}_{table}_tenant_isolation ON {schema}.{table}
                USING (
                    {nullClause}tenant_id = {TenantCast}
                    OR {BypassGuc} = 'true'
                )
                WITH CHECK (
                    {nullClause}tenant_id = {TenantCast}
                    OR {BypassGuc} = 'true'
                );
        ";
        mb.Sql(policySql);
    }

    private static void DisableRls(MigrationBuilder mb, string schema, string table)
    {
        mb.Sql($"DROP POLICY IF EXISTS rls_{schema}_{table}_tenant_isolation ON {schema}.{table};");
        mb.Sql($"ALTER TABLE {schema}.{table} DISABLE ROW LEVEL SECURITY;");
    }
}
```

The helper methods `EnableRls` / `DisableRls` are local to each module's migration file (not shared) — duplication is fine for migrations because they're tagged + immutable post-ship. Sharing across modules would couple migrations to a runtime helper which is fragile during replay.

### 6.8 Per-module table list for migrations

For the implementor's reference (driving the 9 migration files):

| Module | Migration file | Tables (NOT NULL) | Tables (NULLABLE) |
|---|---|---|---|
| Identity | `OpsM9a_Identity_RlsPolicies` | — | `audit_log` |
| Catalog | `OpsM9a_Catalog_RlsPolicies` | `properties`, `property_images` | — |
| Booking | `OpsM9a_Booking_RlsPolicies` | `bookings`, `booking_holds`, `availability_blocks` | — |
| Payment | `OpsM9a_Payment_RlsPolicies` | `payment_intents`, `refunds` | `webhook_events` |
| Reviews | `OpsM9a_Reviews_RlsPolicies` | `reviews` | — |
| Pricing | `OpsM9a_Pricing_RlsPolicies` | `pricing_plans`, `pricing_rules` | — |
| Messaging | `OpsM9a_Messaging_RlsPolicies` | `threads`, `messages` | — |
| Sync | `OpsM9a_Sync_RlsPolicies` | `channel_feeds`, `external_reservations`, `sync_conflicts`, `sync_runs` | — |
| Notifications | `OpsM9a_Notifications_RlsPolicies` | — | `notification_log` |

**Verification**: this table is the ground truth. If a future PR adds a new tenant-scoped table, the engineer adds (i) the policy migration for that module, (ii) the row to this table, (iii) the row to §3.1, (iv) the row to the `RlsPolicySchemaTests` `InlineData`. The arch test (Step 5) catches drift.

### 6.9 The Sync worker `Program.cs` edit — full diff

The bootstrap section transforms from (verified current state, `Workers.Sync/Program.cs:74-80`):

```csharp
using var scope = host.Services.CreateScope();
var feeds = scope.ServiceProvider.GetRequiredService<IChannelFeedRepository>();
var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
var now = clock.UtcNow;

var due = await feeds.ListDueForPollAsync(now);
```

to (M.9-edited):

```csharp
using var scope = host.Services.CreateScope();
var bypassFactory = scope.ServiceProvider
    .GetRequiredService<IRlsBypassDbContextFactory<SyncDbContext>>();
var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
var now = clock.UtcNow;

// OPS.M.9 §4.5 (D5) — the bootstrap read is cross-tenant (every tenant's
// due feeds). Open a bypass-scoped SyncDbContext for this one query, then
// dispose. Per-feed dispatch below uses the normal request-scoped DbContext
// chain; the M.6 BackgroundCommandTenantScopeBehavior stamps
// BackgroundTenantScope from the command's TenantId; the M.9 interceptor
// reads the scope as a fallback to ICurrentUser.TenantId.
List<ChannelFeed> due;
await using (var bypassDb = await bypassFactory.CreateForBypassAsync(
    "sync-worker.list-due-feeds", cts.Token))
{
    var feeds = new ChannelFeedRepository(bypassDb);
    due = await feeds.ListDueForPollAsync(now);
}
```

The `IChannelFeedRepository` instantiation inside the bypass scope is the cleanest shape — the repository's `ListDueForPollAsync` runs against the bypass DbContext directly. The alternative (inject the repository normally and have it open the bypass internally) couples the repository to the bypass factory; the worker's shape is the right place for the bypass call.

**Open implementation question**: `IChannelFeedRepository`'s constructor injects a `SyncDbContext`; constructing a fresh instance with the bypass context is straightforward but the worker is now constructing a repository directly (instead of getting it from DI). This is fine — workers do this kind of thing for one-shot batch jobs. Documented in §11 forward-link as a potential refactor (consider a `RepositoryFactory` pattern if more bypass-using workers ship).

---

## 7. Hot-path validation

For each existing handler that does a cross-tenant read today, walk through whether it needs the bypass or not. This is the explicit allow-list pinned by the arch test in Step 12.

### 7.1 Sites that need the bypass (the allow-list)

| # | Call site | File | Reason | Reason string for the log line |
|---|---|---|---|---|
| 1 | `TenantStripeContextLookup.GetByStripeAccountAsync` | `src/Modules/VrBook.Modules.Identity/Infrastructure/TenantStripeContextLookup.cs:28-40` | Resolves tenant from Stripe account id before tenant is known. | `"stripe-account-lookup"` |
| 2 | `TenantStripeContextLookup.GetAsync` | same file, `:15-26` | Operator/PlatformAdmin querying any tenant's Stripe context. | `"platform-tenant-stripe-context"` |
| 3 | `HandleStripeWebhookHandler.Handle` (whole body) | `src/Modules/VrBook.Modules.Payment/Application/Commands/HandleStripeWebhookCommand.cs:40-110` | The handler is platform-level; tenant id is resolved mid-flight; the row write needs to land regardless of GUC. | `"stripe-webhook"` |
| 4 | `Workers.Sync/Program.cs` bootstrap `feeds.ListDueForPollAsync` | `src/Workers/VrBook.Workers.Sync/Program.cs:74-80` | Cross-tenant enumeration on worker startup. | `"sync-worker.list-due-feeds"` |
| 5 | `ListPlatformTenantsHandler` | `src/Modules/VrBook.Modules.Identity/Application/Tenants/Queries/PlatformTenantQueries.cs:35` | M.8 platform-admin tenant list. | `"admin.platform.list-tenants"` |
| 6 | `GetPlatformTenantHandler` | same file, `:83` | M.8 platform-admin tenant detail. | `"admin.platform.get-tenant"` |
| 7 | `PlatformTenantStatsLookup.GetAsync` | `src/Modules/VrBook.Modules.Identity/Infrastructure/PlatformTenantStatsLookup.cs:21-40` | M.8 platform-admin stats. | `"admin.platform.tenant-stats"` |

**Total bypass call sites: 7.** These are the ONLY classes that may inject `IRlsBypassDbContextFactory<>` per the arch test.

### 7.2 Sites that look cross-tenant but actually aren't (false positives)

These were inventoried by the sub-agent reconnaissance (§8 of the reconnaissance report); each was verified to have a tenant-scoping `WHERE` clause that means RLS will silently agree.

| # | Call site | File | Why not bypass |
|---|---|---|---|
| 1 | `UserEmailLookup` | `src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/UserEmailLookup.cs:10` | Reads `users` table; `users` has no `tenant_id` (§3.2). RLS not relevant. |
| 2 | `RealLoyaltyDiscountResolver` | `src/Modules/VrBook.Modules.Loyalty/Infrastructure/RealLoyaltyDiscountResolver.cs:31` | Reads `loyalty.accounts`; Loyalty schema is platform-wide per OPS.M.3 (no `tenant_id`). RLS not applied (§3.2 row 18). |
| 3 | `GetMyLoyaltyQuery` | `src/Modules/VrBook.Modules.Loyalty/Application/Accounts/Queries/GetMyLoyaltyQuery.cs:24` | Same as #2. |
| 4 | `ConfirmedBookingLookup` | `src/Modules/VrBook.Modules.Booking/Infrastructure/Persistence/ConfirmedBookingLookup.cs:28` | Reads `bookings`; the caller is always within their own tenant scope; the request-scoped DbContext's GUC stamps the right tenant id; RLS silently agrees. |
| 5 | `ThreadQueries` | `src/Modules/VrBook.Modules.Messaging/Application/Threads/Queries/ThreadQueries.cs` | Same as #4 for messaging. |
| 6 | `GetSourceReportQuery` (Reports) | `src/Modules/VrBook.Modules.Reports/Application/Source/Queries/GetSourceReportQuery.cs:49, :62` | Tenant-scoped; RLS silently agrees. |
| 7 | `IPropertyCountByTenant` (Catalog) | `src/Modules/VrBook.Modules.Catalog/Infrastructure/PropertyCountByTenant.cs` | **Mixed** — when called from `PlatformTenantStatsLookup` it's cross-tenant; the M.9 wire-up routes the call through the bypass-opened `IdentityDbContext`, but `PropertyCountByTenant` itself uses `CatalogDbContext`. The call site needs to be re-examined during Step 10 implementation — either `PlatformTenantStatsLookup` opens both a bypass IdentityDbContext AND a bypass CatalogDbContext, OR the `IPropertyCountByTenant` contract bumps to accept the bypass factory. **Architect note for implementor**: pick the cleaner shape during Step 10. Probable answer: `PlatformTenantStatsLookup` opens a bypass `CatalogDbContext` via the bypass factory when answering the property-count question; the cross-module `IPropertyCountByTenant` lookup gets a fresh impl that uses the bypass. |

### 7.3 Read-side surface map

This is the canonical list. If a future PR adds a new bypass injection that isn't in §7.1, the Step 12 arch test fails. The PR must update §7.1 + the test's allow-list AND justify in the PR description.

### 7.4 Endpoint-by-endpoint M.9 impact map

For every public API endpoint that exists today, what changes after M.9 ships?

| Endpoint | Method | Current state | After M.9 |
|---|---|---|---|
| `/api/v1/me` | GET | Reads `users` (no `tenant_id`); request-scoped DbContext. | Unchanged — `users` is not RLS-protected (§3.2). |
| `/api/v1/me/tenant` | GET | Reads `tenants` by `currentUser.TenantId`; request-scoped DbContext. | Interceptor stamps `app.tenant_id = currentUser.TenantId`; the tenant read is silently scoped by RLS (same-tenant agreement). |
| `/api/v1/properties` (Owner-scoped) | GET | Reads `properties` filtered by tenant in handler. | Interceptor stamps GUC; RLS silently agrees with the handler's filter (double-protection). |
| `/api/v1/properties/{id}` | GET | Same as above. | Same; if the property belongs to a different tenant, RLS returns zero rows + handler 404s. |
| `/api/v1/bookings` (place booking) | POST | Writes `bookings`, `booking_holds`, `payment_intents` in one transaction. | Interceptor stamps GUC; every INSERT runs with the same `app.tenant_id`; RLS `WITH CHECK` permits. |
| `/api/v1/admin/tenants/{tenantId}/stripe/onboard` | POST | M.5 Stripe onboarding; reads `tenants` for the caller's own tenant; writes `tenants.stripe_account_id`. | Unchanged — `tenants` is not RLS-protected (§3.2). |
| `/api/v1/admin/tenants/{tenantId}/stripe/account-link` | POST | Same. | Same. |
| `/api/v1/admin/tenants/{tenantId}/stripe/login-link` | POST | Same. | Same. |
| `/api/v1/admin/platform/tenants` | GET (PlatformAdmin) | M.8 list — reads `tenants` cross-tenant. | **Rewired (Step 10)**: `ListPlatformTenantsHandler` opens bypass `IdentityDbContext`. Returns all tenants under bypass. |
| `/api/v1/admin/platform/tenants/{tenantId}` | GET (PlatformAdmin) | M.8 detail — reads `tenants` + calls `IPlatformTenantStatsLookup`. | **Rewired (Step 10)**: handler + lookup both use bypass; cross-tenant reads succeed. |
| `/api/v1/admin/platform/tenants/{tenantId}/suspend` | POST (PlatformAdmin) | M.8 — calls `Tenant.Suspend(reason, actorId)` on a `Tenant` in any tenant. | Unchanged — `tenants` is not RLS-protected; the write goes through. |
| `/api/v1/admin/platform/tenants/{tenantId}/reactivate` | POST (PlatformAdmin) | M.8 — calls `Tenant.Reactivate()`. | Same. |
| `/api/v1/admin/platform/tenants/{tenantId}/platform-fee` | PUT (PlatformAdmin) | M.8 — calls `SetTenantPlatformFeeBpsCommand` (ITenantScoped; M.4 bypass fires). | Same — the write hits `tenants` which is not RLS-protected. |
| `/api/v1/webhooks/stripe` | POST (anonymous; Stripe-signed) | M.5 — `HandleStripeWebhookHandler`; reads `tenants` by `stripe_account_id`; writes `webhook_events` + dispatches downstream. | **Rewired (Step 8)**: handler opens bypass `PaymentDbContext` + bypass `IdentityDbContext` (via `TenantStripeContextLookup`); the whole body runs under bypass; `webhook_events` row write permitted by bypass policy. |
| `/api/v1/sync/feeds` (owner-scoped) | GET / POST / PUT | M.6 — owner manages their own feeds; reads/writes `channel_feeds`. | Interceptor stamps GUC; RLS silently agrees. |
| `/api/v1/sync/conflicts/{id}/resolve` | POST | M.6 — owner resolves a conflict; writes `sync_conflicts`. | Interceptor stamps GUC; RLS silently agrees. |
| `/api/v1/notifications/...` | various | Slice 4 (not shipped yet) — `notification_log` writes. | When Slice 4 ships, interceptor stamps GUC; RLS on `notification_log` allows null-tenant guest-facing writes + same-tenant writes. |
| `/health` | GET (anonymous) | Reads schema-version. | Interceptor stamps `app.tenant_id = ''`; the schema-version table is not RLS-protected; read succeeds. |
| **Sync worker** (cron `*/5 * * * *`) | — | M.6 — enumerates due feeds cross-tenant; dispatches per-feed commands. | **Rewired (Step 9)**: bootstrap uses bypass; per-feed uses `BackgroundTenantScope` fallback to stamp GUC = feed's tenant id. |

**The net effect**: 100% of the existing API surface continues to work post-M.9. The three rewires (Steps 8, 9, 10) are the only behavioral changes; every other endpoint gets RLS-as-belt-and-braces silently. Cross-tenant **leaks** (a future bug that lets a Tenant A request read a Tenant B row) become **RLS-rejected at the DB layer** — the bug surfaces as "zero rows returned" instead of "cross-tenant data leaked".

### 7.5 GUC value cheat-sheet — what does the connection see?

For an implementor debugging a "why did this query return zero rows" failure, here's the cheat-sheet for what `SHOW app.tenant_id;` + `SHOW app.is_platform_admin;` would return in each scenario:

| Scenario | `app.tenant_id` | `app.is_platform_admin` | What the policy permits |
|---|---|---|---|
| Authenticated Owner of tenant A reading their own properties | `<tenant-A-guid>` | `false` | Rows where `tenant_id = <tenant-A-guid>` |
| Authenticated Owner of tenant A reading tenant B's properties (a bug) | `<tenant-A-guid>` | `false` | Zero rows (policy filter false) |
| Authenticated PlatformAdmin reading tenant B's properties via the M.8 endpoints (rewired through the bypass factory) | _(irrelevant — the bypass DbContext is in scope)_ | `true` | All rows |
| Anonymous request to `/health` | `''` (empty) | `false` | Zero rows for any RLS-protected table; non-protected tables (schema_version) work |
| DevAuth Owner persona seeded into tenant A | `<tenant-A-guid>` (from `DevAuthPersonas`) | `false` | Same as Owner case |
| DevAuth Admin persona (post-OPS.M.8 seed) | `<seeded-tenant-guid>` | `true` | All rows (bypass active) — note: bypass active because DB column `is_platform_admin = true`; the middleware stamps the flag and the interceptor reads `currentUser.IsPlatformAdmin` |
| Stripe webhook arrives | `''` (empty before bypass scope opens) → bypass active during handler body | `false` → `true` | During the handler body: all rows permitted; outside it (signature verify): no DB reads |
| Sync worker bootstrap | bypass active | `true` | All feeds visible |
| Sync worker per-feed `RunSyncForFeedCommand` | `<feed.tenant-guid>` (from `BackgroundTenantScope`) | `false` | Only this feed's tenant's rows |
| Migrator role (running `dotnet ef database update`) | _(no interceptor)_ — `''` | `''` | All rows (role-level `BYPASSRLS`) |

**Critical hot-path nuance**: an authenticated PlatformAdmin who is ALSO an Owner of some tenant defaults to their Owner tenant in `currentUser.TenantId` (the primary membership). A non-bypass M.8 endpoint (e.g. the SetPlatformFee write) would stamp GUC = their Owner tenant. The RLS policy for `tenants` is not RLS-protected (§3.2), so the write goes through. If a future PlatformAdmin endpoint touched an RLS-protected table cross-tenant WITHOUT the bypass factory, the write would fail with `new row violates row-level security policy`. **Mitigation**: M.10 test pack verifies; M.9 §9 guard rail item 11 captures the convention.

---

## 8. Cross-tenant safety review

The new threat surface introduced by M.9 is "a handler that should NOT bypass but accidentally does". Three failure modes:

### 8.1 Failure mode A — accidental bypass-factory injection in a tenant-scoped handler

A future PR adds `IRlsBypassDbContextFactory<BookingDbContext>` to `PlaceBookingHandler` (intent: "convenience for cross-property read"). This would let a guest in tenant A book a property in tenant B — a serious cross-tenant leak.

**Mitigation**: the Step 12 arch test (`RlsBypassCallSiteAllowlistTests`) pins the §7.1 inventory. The PR fails CI; the engineer must explicitly add the class to the allow-list, which is a flagged design-review event in code review.

### 8.2 Failure mode B — bypass scope leaks across an unrelated await

A handler does:
```csharp
await using var bypass = await factory.CreateForBypassAsync("...");
var ctx = await bypass; // bypass active
var data = await someOtherService.DoUnrelatedReadAsync(); // bypass STILL active
```

`DoUnrelatedReadAsync` uses a different request-scoped DbContext but the AsyncLocal `RlsBypassScope` is still in scope — the unrelated read runs under bypass, potentially leaking cross-tenant data.

**Mitigation**: §9 guard rail item 2 — "the bypass DbContext is short-lived: open → query → dispose. NEVER hold across an unrelated await." Enforcement via code review; the arch test cannot easily detect this dynamic shape. The M.10 test pack will exercise the AsyncLocal leak risk in its sweep (positive: a bypass-using handler that calls into a tenant-scoped service; assert the tenant-scoped service still respects its own tenant scope despite the AsyncLocal bypass).

### 8.3 Failure mode C — middleware run under bypass

A future middleware runs `await db.SomeReadAsync()` under an outer bypass that the request established. The middleware's read could leak. **Mitigation**: the middleware order is fixed (verified `Program.cs`); `UserProvisioningMiddleware` runs early; nothing under it opens a bypass. M.10 verifies.

### 8.4 Architecture invariant

**Bypass usage is concentrated in the seven enumerated call sites in §7.1, enforced by the Step 12 arch test.** New bypass call sites are a deliberate review event; the test fails until both §7.1 AND the test's allow-list are updated. The PR review process documents this in the runbook.

### 8.5 Defense-in-depth: app-layer + RLS

After M.9, every cross-tenant write is gated by both:
1. The OPS.M.4 `TenantAuthorizationBehavior` — app-layer rejects writes whose `TenantId` doesn't match `ICurrentUser.TenantId` (with the M.8 PlatformAdmin bypass).
2. The M.9 RLS policy `WITH CHECK` clause — DB-layer rejects writes whose `tenant_id` doesn't match the GUC.

Both must agree. A bug that bypasses M.4 will be caught by M.9's DB rejection (with a more obscure error message — `new row violates row-level security policy`). A bug that bypasses M.9 (e.g. the migrator role doing a backfill) is caught by M.4 if the path went through MediatR.

### 8.6 What M.10's test pack will assert

M.10 inherits the M.9 mechanism and adds the holistic proof. The M.10 sweep:

1. **Two-tenant fixture** — seed tenant A + tenant B with realistic data: properties, bookings, payments, reviews, pricing rules, sync feeds, messaging threads, notifications. Both fully populated.
2. **Per-endpoint negative case** — for every public endpoint, assert Owner-of-A trying to reach a Tenant-B resource returns 403 (from `TenantAuthorizationBehavior`) OR 404 (from "not found in my tenant"). Never 200-with-cross-tenant-data.
3. **Per-endpoint positive case** — Owner-of-A reaches their own tenant's data successfully.
4. **PlatformAdmin sweep** — PlatformAdmin reaches both A and B successfully via the M.8 platform endpoints.
5. **PlatformAdmin without bypass** — a hypothetical PlatformAdmin endpoint that forgot to use the bypass factory: assert the M.9 RLS rejects the read (zero rows or 404). This is the test that PROVES the RLS layer is the last line of defense.
6. **Audit trail completeness** — every cross-tenant attempt (success or rejection) lands in `audit_log` with the right `actor_role` + `target_tenant_id`.
7. **AsyncLocal leak test** — a bypass-using handler calls into a tenant-scoped service; assert the tenant-scoped service still respects its own tenant scope (the bypass flag leaked, but the tenant-scoped service's GUC stamp wins).

M.9 does NOT ship these tests. M.10 does. The hand-off contract is the M.9 mechanism + the §3.1 inventory + the §7.1 bypass call-site allow-list.

---

## 9. Implementation guard rails (best practices)

Every M.9 PR must satisfy these. Arch tests enforce items marked **[arch]**; code review enforces the rest.

1. **Every new DbContext must register its `IRlsBypassDbContextFactory<>` impl** + `AddDbContextFactory<>` + the per-statement interceptor via `UseRlsTenantGuc(sp)`. **[arch — Step 2]**

2. **The bypass DbContext is short-lived: open → query → dispose. NEVER hold across an unrelated await for UI render, network call, or unrelated service call.** The AsyncLocal `RlsBypassScope` is logical-thread-scoped; a long-held bypass leaks the flag to every DbContext on the thread. Code review pin; M.10 test pack exercises the leak risk. **[code review + M.10 verification]**

3. **Structured logging: every bypass open MUST log `bypass_reason` + `caller_handler` + the caller's `actor_user_id`** (where applicable; the worker has no actor). The base class `RlsBypassDbContextFactoryBase` emits the log line at `Information` level. **[base class enforced]**

4. **The bypass factory's `reason` parameter is required** — the contract requires it. Empty-string reasons fail validation (FluentValidation on the factory if needed; simpler: throw `ArgumentException` in the base class if `string.IsNullOrWhiteSpace(reason)`). **[base class]**

5. **No `OR true` or `OR 1=1` shortcuts in policies** — the policy text uses the canonical shape from §3.4. The Step 5 schema test asserts the policy text matches the template. **[arch — Step 5]**

6. **No policy DISABLE in subsequent migrations without architect review** — a future PR that adds `DISABLE ROW LEVEL SECURITY` to any table is a flagged review event. There's no arch test for this (the policy could be dropped legitimately if a table is being removed) — code review pin.

7. **Migrator role keeps `BYPASSRLS`** — the migrator role's `rolbypassrls` attribute must remain `true`. Step 6 asserts; a future grant revoke would break migrations. **[arch — Step 6]**

8. **The interceptor MUST be first in the interceptor list** — verified by the Step 1 test (assert `set_config` lines appear before the outbox-related lines in the log capture). If a future interceptor is added that needs to run earlier (unlikely — the GUC is a precondition for every other DB-layer interaction), it must be carefully ordered. **[Step 1 integration]**

9. **`current_setting()` calls always use `, true`** — every policy in the codebase uses the missing-OK variant. Step 5 arch test pin. **[arch — Step 5]**

10. **`NULLIF(current_setting(...), '')::uuid` is the canonical tenant-id cast** — handles empty-string GUC without throwing. Step 5 arch test pin (regex over policy text). **[arch — Step 5]**

11. **No PlatformAdmin endpoint should bypass M.4 + M.9 simultaneously without a logged reason** — the M.8 PlatformAdmin bypass at the app layer + the M.9 bypass at the DB layer is the strongest privilege grant in the system. Every PlatformAdmin endpoint's handler emits a structured log line for the M.8 bypass; M.9 adds a second log line for the bypass-factory open. Two log lines per cross-tenant write = the audit trail. **[code review + handler convention]**

12. **The `tenant_memberships` carve-out (D11) is permanent** — Phase 2 can revisit, but for Phase 1.5, the table is RLS-exempt. A future PR adding a policy to `tenant_memberships` is a flagged review event (it would break `UserProvisioningMiddleware`). **[code review]**

### Arch tests summary

- `TenantGucInterceptorTests` (Step 1) — 5 facts.
- `RlsBypassFactoryRegistrationTests` (Step 2) — 3 facts.
- `BackgroundCommandTenantScopeBehaviorTests` (Step 3) — 3 facts.
- `InterceptorRegistrationTests` (Step 4) — 9 facts (one per module).
- `RlsPolicySchemaTests` (Step 5) — 19 tables × 4 facts = 76 facts; plus the §3.2 carve-out checks (~13 facts) = ~89 facts.
- `MigratorRoleBypassTests` (Step 6) — 1 fact.
- `TenantStripeContextLookupBypassTests` (Step 7) — 4 facts.
- `StripeWebhookCrossTenantResolutionTests` (Step 7) — 1 integration scenario.
- `HandleStripeWebhookBypassTests` (Step 8) — 4 facts.
- `SyncWorkerBootstrapBypassTests` (Step 9) — 3 facts.
- `PlatformTenantHandlersBypassTests` (Step 10) — 4 facts.
- `RlsIsolationFactPack` (Step 11) — 19 tables × 6 facts = 114 facts.
- `CarveOutTableSchemaTests` (Step 11.5) — 13 facts.
- `RlsBypassCallSiteAllowlistTests` (Step 12) — 2 facts.

**Total: ~263 facts across ~14 test classes/files.** Larger than M.8 (~120) — RLS is structural; the surface is wide; every table needs a positive + negative + bypass case.

### 9.13 Operator concerns — what does production look like after M.9?

**Day-1 operator experience**:

- Every API request emits a `Debug`-level "RLS GUC stamped" log line per command (high volume; default log level filters it out — only visible at `Debug`).
- Every bypass-factory open emits an `Information`-level line. Volume estimate: the M.8 platform list endpoint hits ~25 bypass opens per page view × ~5 page views per operator per day × 3 operators = ~375 lines per day. Sync worker bootstrap: 1 bypass open per worker run × every 5 minutes = ~288 lines per day. Stripe webhooks: 1 bypass open per webhook event × variable volume (low in Phase 1.5 — single-digit per day; high in steady state). **Net**: hundreds to low thousands of bypass-open Information lines per day at Phase 1.5 maturity. Acceptable for grep-style audit; if it becomes noise, downgrade to `Debug` in a follow-up.
- No new metrics dashboards. The bypass count could be a future metric (Phase 2 observability hardening).

**Day-1 deploy hazard**: Wave 2 is the deploy that activates RLS. If the wave deploys to staging and the Sync worker hasn't yet picked up the rewire (a deploy ordering issue between API and worker — possible if they're separate Container Apps), the worker would attempt `feeds.ListDueForPollAsync` without bypass and return zero rows. The runbook documents the rollback procedure: revert the policy migrations (the Down() side of `OpsM9a_<Module>_RlsPolicies` is symmetric).

**Day-7 operator drill**: the runbook (Step 13) includes a "simulate a Tenant-A-reads-Tenant-B request" drill — manually craft a request with a forged `app_tenant_id` claim, hit the API, observe the M.4 rejection (app-layer first). Then forge ONLY a database-level connection (psql with the right credentials, `SET LOCAL app.tenant_id = '<tenant-A-guid>'`), attempt a SELECT on Tenant-B's bookings — observe zero rows. This is the operator's confidence-building exercise.

**Phase 2 hardening points**:

- Per-tenant DB roles (revisit §1.2 row "Per-tenant DB roles").
- `BYPASSRLS` audit (the migrator role's grant could be tracked in a separate audit log; a rogue admin granting `BYPASSRLS` to an app role is the worst-case escalation).
- Materialized views for reporting (a separate auth boundary).

---

## 10. Operational considerations

### 10.1 Connection-pool implications

Npgsql's default connection pool reuses connections across requests. `SET LOCAL` is transaction-scoped (auto-reset at transaction end), so the GUC does NOT leak across requests on the same physical connection. **However**: if the M.9 interceptor were to use `SET` (without LOCAL), the GUC would leak to the next request's first transaction — a serious cross-tenant data hazard.

The interceptor implementation (§4.3 / §6) uses `set_config('app.tenant_id', value, is_local=true)` — the third argument forces transaction-scope. The Step 1 integration test asserts this (the Postgres log shows `is_local=t` in the set_config call).

**Pool sizing**: M.9 does not change the pool size. Each request takes one connection from the pool, opens a transaction (autocommit or explicit), the interceptor stamps the GUC, the command runs, the transaction closes, the GUC resets, the connection returns to the pool. Same shape as before; the only addition is the per-command `set_config` overhead (<1ms).

### 10.2 Long-running transactions

If a handler holds a transaction open for an extended period (e.g. a multi-aggregate write), the GUC is set ONCE for that transaction and the policy uses the same tenant id for every command inside it. This is the desired property for Phase 1.5 — every command in one transaction belongs to one tenant.

**Phase 4 OTA forward-compat**: an itinerary write spans N supplier tenants. The implementation will open a transaction, stamp `app.tenant_id = supplier_A`, write the supplier-A leg, then re-stamp `app.tenant_id = supplier_B` mid-transaction, write the supplier-B leg, commit. The per-statement binding (D1) supports this natively because each command's interceptor fires and re-stamps. **M.9 does not implement Phase 4 logic — only enables it.**

### 10.3 Read replicas

If the platform ever adds Postgres read replicas (Phase 2 hardening / cost optimization), the RLS policies replicate automatically (they're catalog-stored, replicated as table metadata). The GUC-setting interceptor would need to fire on the replica connection too — straightforward if the read replica is wired via the same Npgsql connection string with `Target Session Attributes=read-only`. No M.9 work required; the replica connections inherit the interceptor.

### 10.4 Backup + restore — RLS interaction

`pg_dump` runs as the database owner by default; the owner has `BYPASSRLS` so dumps include every row. Restoration: the policies are dropped + recreated by the dump's DDL section; the data section's INSERTs run as the owner (bypass active) so all rows land. **No M.9 work required for backup/restore mechanics.**

**Caveat for SaaS-tenant data export** (Phase 2 ops): if/when the platform offers "export my tenant's data" as a self-serve feature, the export job needs to either (i) open the bypass factory (problematic — the export is per-tenant, not cross-tenant; bypass would over-grant), or (ii) run under the requesting Owner's tenant scope (preferred — the RLS policy then naturally scopes the dump). Documented as a deferred Phase 2 consideration.

### 10.5 Test-environment vs production

In test (testcontainer Postgres), the migrations create the policies; the interceptor runs; the bypass factory is the only escape hatch. Identical to production behavior.

**Local dev (LocalDB / docker-compose)**: same. The OPS.M.3 fixture pattern (`TenantIdRolloutFixture`) is reused for M.9's integration tests (Step 11). Developers running locally see the same RLS enforcement; debugging is via the runbook (Step 13).

**Migrator role in test**: the test-environment's `vrbook_migrator` role must also have `BYPASSRLS` granted. The Step 6 fact verifies; the fixture's role-setup SQL is updated.

---

## 11. Reserved — no removed sections

This rev does not promote any deferred decision, so no rev-summary block is needed. All decisions in §4 are locked at first authoring (with open questions O1-O7 in §Appendix B carved out as next-slice candidates).

---

## 12. Forward links + post-M.9 roadmap

### 12.1 Slice OPS.M.10 — Cross-tenant isolation test pack (next slot, 2 days)

M.10 owns the **holistic proof**. The M.9 mechanism (policies + bypass factory) gives M.10 the tools; M.10's sweep exercises every endpoint with two-tenant fixtures and asserts no cross-tenant leak surfaces. The hand-off contract:

- §3.1 inventory + §7.1 bypass call-site allow-list are stable; M.10 reads them as the canonical surface.
- M.10 adds the holistic positive + negative test pack per §8.6.
- M.10 also adds tests for the AsyncLocal-leak failure mode (§8.2) — a handler opens bypass, calls into a tenant-scoped service, the service should still respect its own GUC.

### 12.2 Slice 4 — Notifications that actually send (3 days, after OPS.M.10)

Slice 4 ships notification handlers that write to `notifications.notification_log`. The table is RLS-protected with the nullable-`tenant_id` policy shape (§3.1 row 14, §4.12 D12). Writes inherit the request's tenant id (D8); guest-facing notifications (no tenant) write with `tenant_id = null` which the policy permits. No M.9 code change required when Slice 4 ships.

### 12.3 Slice 5 — Reviews + Loyalty (2 days)

Reviews are tenant-scoped (`reviews.tenant_id NOT NULL` per §3.1 row 9). Loyalty is platform-wide (no `tenant_id` per §3.2 row 18). Slice 5's handlers inherit M.9's protection automatically.

**Phase-3-aware tweak**: Slice 5 ships `Review.PropertyId` as part of the composite key per `PHASE_3_RECONNAISSANCE.md` line 59. No M.9 interaction; the composite key doesn't change the RLS shape.

### 12.4 Slice OPS.6 — Stripe key rotation (launch hardening)

When rotating Stripe API keys, the M.9 mechanism is unaffected — the webhook handler's bypass-factory call doesn't depend on the API key. The runbook for key rotation doesn't need M.9 updates.

### 12.5 Phase 4 — Slice 10 OTA package bundling

The consumer of D1 (per-statement binding). When Phase 4 ships, the itinerary write path stamps `app.tenant_id` per leg within a single transaction. The mechanism is already in place; no migration changes needed. The new Phase 4 code adds bypass call sites (the itinerary read path is cross-tenant by design); the §7.1 allow-list will expand.

### 12.6 Phase 2 hardening — what M.9 enables

- **Per-tenant DB roles**: an additional `OR has_role(current_user, 'tenant_<id>', 'MEMBER')` clause on each policy. Additive.
- **Per-row encryption**: orthogonal; ship as a separate migration that ALTERs columns to `bytea` + adds `pgcrypto` wrapping at the EF value-converter layer. M.9 RLS continues to apply.
- **Read replicas**: GUC-setting interceptor naturally applies on replica connections.
- **Audit-log read endpoint** (M.8 §1.2 O2): uses the M.9 bypass factory.
- **Tenant data export** (per §10.4): runs under the requester's tenant scope; RLS naturally provides the dump.

---

## 13. Close-out — TBD (filled in post-ship)

### Per-step commit ledger

| Step | Wave | Module(s) | Commit | Files touched |
|---|---|---|---|---|
| 1 | 1 | Infrastructure | _pending_ | |
| 2 | 1 | Contracts + 9 modules | _pending_ | |
| 3 | 1 | Sync | _pending_ | |
| 4 | 1 | 9 modules | _pending_ | |
| 5 | 2 | 9 modules (migrations) | _pending_ | |
| 6 | 2 | Migrator | _pending_ | |
| 7 | 2 | Identity | _pending_ | |
| 8 | 2 | Payment | _pending_ | |
| 9 | 2 | Sync worker | _pending_ | |
| 10 | 2 | Identity | _pending_ | |
| 11 | 2 | Integration tests | _pending_ | |
| 12 | 2 | Architecture tests | _pending_ | |
| 13 | 2 | Docs | _pending_ | |

### Deviations from this plan

_(To be filled in post-ship.)_

### Forward links

- **Slice OPS.M.10 — Cross-tenant isolation test pack**: the holistic two-tenant integration sweep. Exercises every public endpoint with Owner-of-A trying to reach tenant-B's data; assert 403 / empty-results. M.10 leans on M.9's mechanism — the RLS policies + the bypass factory — to provide the DB-level enforcement that the app-level assertions verify. M.9 + M.10 together = "the multi-tenant isolation guarantee".
- **Slice 10 (Phase 4 OTA package bundling)**: the consumer of M.9's per-statement binding decision (D1). An itinerary spans N supplier tenants; the read path switches `app.tenant_id` mid-transaction per leg. No M.9 code change required; the per-statement interceptor naturally supports the pattern.
- **Phase 2 — Per-tenant DB roles**: stronger isolation by giving each tenant its own DB role with `SET ROLE` per-request. RLS becomes redundant for tenant scoping but remains for PlatformAdmin bypass + audit. M.9's GUC-driven policy is forward-compatible (an additional `OR has_role(current_user, 'tenant_<id>_admin', 'MEMBER')` clause could be added).
- **Phase 2 — Per-row encryption**: encrypting `email`, `phone`, `payment_intents.metadata`, etc. Orthogonal to RLS; both can layer.
- **Phase 2 — Auth-aware materialized views**: reporting surfaces. RLS interaction with mat-views is non-trivial; Phase 2 chooses the materialization strategy.
- **Slice OPS.M.8.1 (Tenant Suspended enforcement)**: per OPS.M.8 §3.9 D9 / Open Question O3. Block new bookings on Suspended tenants. M.9 does NOT change this; the M.8.1 handlers add a `BusinessRuleViolationException("tenant.suspended", …)` check. The RLS policies do not enforce status; that's app-layer.
- **Slice 4 (Notifications) — RLS verification**: when Slice 4 ships the ACS pipeline, every notification write is per-tenant; the RLS policy on `notifications.notification_log` permits the write because the GUC is set. The nullable-`tenant_id` path (guest-facing notifications) is the §3.1 D12 case.

---

## Appendix A — Verified codebase claims

Every concrete file/class name in §3-§5 is grounded in one of these. Sub-agent reconnaissance (the two-thread `Explore` agents on 2026-06-28) was the verification source. If any line drifts, the plan's *contract claim* is the contract — adjust the file path, not the contract.

| Claim | Source |
|---|---|
| 19 tenant-scoped tables across 9 modules carry `tenant_id NOT NULL` post-OPS.M.3 | Sub-agent inventory Q1 + Q2, cross-referenced with `Migrations/*OpsM3c*` files |
| `payment.webhook_events.tenant_id`, `identity.audit_log.tenant_id`, `notifications.notification_log.tenant_id` are nullable by design | `PaymentIntentConfiguration.cs:73`, `AuditLogEntryConfiguration.cs:22`, `NotificationsDbContext.cs:35` |
| Per-module `outbox_messages` tables carry NO `tenant_id` column | OPS.M.3 §3.2 carve-out (system events); sub-agent Q4 |
| `identity.users`, `identity.tenants`, `identity.tenant_memberships` carry no per-row `tenant_id` (the schema's primary keys are user / tenant level; `tenant_memberships` has a `tenant_id` column that IS the FK to its parent tenant — not a row-level scope) | sub-agent Q2 |
| Every DbContext is `sealed` and inherits from `BaseDbContext` (`VrBook.Infrastructure.Persistence.BaseDbContext`); ctor `(DbContextOptions options, ICurrentUser currentUser, IDateTimeProvider clock)` | `BaseDbContext.cs:18-21` + sub-agent Q3 |
| `BaseDbContext` implements `IUnitOfWork`; `SaveChangesAsync` calls `ApplyAudit()` then `base.SaveChangesAsync` | `BaseDbContext.cs:21, :73-77` |
| `BaseDbContext.BeginTransactionAsync` opens an EF transaction and returns an `IAsyncDisposable` that commits on dispose | `BaseDbContext.cs:80-84, :117-124` |
| `TenantAuthorizationBehavior` short-circuits for `IBackgroundCommand` BEFORE the PlatformAdmin check | `TenantAuthorizationBehavior.cs:49-52` |
| `TenantAuthorizationBehavior.IsPlatformAdmin(user)` returns `user.IsPlatformAdmin` (lit-up per OPS.M.8) | `TenantAuthorizationBehavior.cs:87-91` |
| `UserProvisioningMiddleware` reads `tenant_memberships` AND `users.is_platform_admin` after `ProvisionUserCommand` returns | `UserProvisioningMiddleware.cs:68-76` |
| `UserProvisioningMiddleware` stamps `HttpContext.Items[HttpCurrentUser.PlatformAdminItemKey]` + role claims | `UserProvisioningMiddleware.cs:78-100` |
| `HttpCurrentUser.IsPlatformAdmin` reads `HttpContext.Items[PlatformAdminItemKey]`, falls back to role claim | `HttpCurrentUser.cs:84-99` |
| `AnonymousCurrentUser.TenantId => null` and `IsPlatformAdmin => false` | `AnonymousCurrentUser.cs:17-18` |
| `ICurrentUser.TenantId` is `Guid?`; null for unauthenticated/anonymous/worker-pre-resolution | `ICurrentUser.cs:35-42` |
| No existing DbCommandInterceptor / DbConnectionInterceptor / NpgsqlDataSourceBuilder customization | sub-agent Q1 + Q2 (zero hits) |
| No existing `SET LOCAL` or `current_setting` calls in source code | sub-agent Q3 (zero hits) |
| Connection string lives in `appsettings.json` `ConnectionStrings:Postgres`; no multiplexing settings | sub-agent Q4 |
| No DbContext pooling; all contexts are scoped via `AddDbContext<TContext>` | sub-agent Q5 (zero hits for `AddDbContextPool` / `AddPooledDbContextFactory`) |
| `IUnitOfWork` is per-module-DbContext via `services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<XxxDbContext>())` | sub-agent Q6; verified in `IdentityModule.cs:42` |
| No existing `IDbContextFactory<>` registration anywhere | sub-agent Q7 (zero hits) |
| `tests/VrBook.Api.IntegrationTests/Identity/TenantIdRolloutFixture.cs` is the OPS.M.3 testcontainer fixture (PostgreSql 16-alpine; multi-module migration apply) | sub-agent Q10 |
| `HandleStripeWebhookHandler.cs:55-94` resolves tenant from `stripe_account_id` BEFORE the tenant is known | `HandleStripeWebhookCommand.cs:53-94` |
| `Workers.Sync/Program.cs:80` calls `feeds.ListDueForPollAsync(now)` cross-tenant on bootstrap | `Program.cs:80` |
| `Workers.Sync/Program.cs:96` pushes `tenant_id` into Serilog log context per-feed | `Program.cs:96` |
| `BackgroundCommandTenantScopeBehavior` asserts `TenantId != Guid.Empty` for `IBackgroundCommand` | sub-agent Q7 (OPS.M.6) |
| M.8 `ListPlatformTenantsHandler` + `GetPlatformTenantHandler` exist in `PlatformTenantQueries.cs:35, :83` | sub-agent Q12 |
| M.8 `PlatformTenantStatsLookup.GetAsync` reads `identity.tenants` + calls `IPropertyCountByTenant` | sub-agent Q5 + Q12 |
| ADR-0014 specifies DB-wins precedence | per OPS.M.2 + OPS.M.8 references |
| MASTER_PLAN.md §2 row 10 locks per-statement binding granularity | `docs/MASTER_PLAN.md:58` |
| PHASE_3_RECONNAISSANCE.md lines 39-46 argues per-statement is the door-opening decision for Phase 4 OTA | `docs/PHASE_3_RECONNAISSANCE.md:39-46` |
| MULTI_TENANCY_OPS_PLAN.md §6 frames RLS as defense-in-depth (belt-and-braces) | referenced in OPS.M.8 §1.0 |
| OPS.M.8's `IsPlatformAdmin` bypass is APP-LAYER ONLY; M.9 adds the DB-LAYER bypass | OPS.M.8 §1.0 + this plan §1 + §8.5 |
| OPS.M.6's iCal worker uses `AnonymousCurrentUser` because it has no HTTP context | `Workers.Sync/Program.cs:45` |
| `DomainEventOutboxInterceptor` is a `SaveChangesInterceptor` (not a Command/Connection interceptor) | sub-agent Q8 |
| The 19 tenant-scoped tables don't change column shape in M.9 (only RLS policies added) | §3.1 + §3.4 template |
| `tests/VrBook.Architecture.Tests/` exists with existing arch tests (`PlatformAdminEndpointRoleGateTests`, `TenantScopedCommandTests`, `BackgroundCommandMarkerTests`, etc.) | repo `ls` output |

---

## Appendix B — Open questions (9 carved-out)

All M.9 decisions in §4 are locked. The brief explicitly said M.9 owns the bypass-factory contract authoring + the binding-granularity decision, both of which are locked. These open questions are carve-outs the user may want to promote into M.9 scope (and accept a re-estimate) or defer to follow-up slices.

### O1 — Should `line_items` / `guests` / `messages` child tables ship their own RLS policies?

§3.3 flagged this for verification at Step 5 implementation time. If these are first-class tables with denormalized `tenant_id` columns, they move to §3.1 and ship policies. If they're owned-types embedded in the parent (or child tables without their own `tenant_id`), the parent's RLS policy + the FK transitively protects them.

**Adds 0-2 hours** depending on verification outcome.

**Verdict default**: verify during Step 5; if first-class tables with `tenant_id`, add to §3.1 and ship. Otherwise stay in §3.2.

### O2 — Should `tenant_memberships` get a less-restrictive RLS policy that allows the bootstrap path?

D11 carves it out completely. An alternative (D11 option c) opens a bypass scope inside the middleware for the membership read; the table then gets a "tenant_id = GUC OR user_id = current_user OR bypass" policy. Pro: stronger DB-layer enforcement. Con: middleware complexity; the bypass scope is request-wide for one query.

**Verdict default**: stay with the carve-out (Phase 2 can revisit when per-tenant DB roles ship).

### O3 — Should the bypass factory log at `Warning` instead of `Information`?

Current §4.4: every bypass open emits a structured Information log line. Some operators may prefer Warning to make bypass events more visible in default log filters.

**Verdict default**: Information. Bypass is by-design routine on platform endpoints + webhooks + worker bootstrap; Warning would flood the dashboard.

### O4 — Should the bypass require a `IPlatformAdminContext` proof?

A stronger contract: `CreateForBypassAsync` requires an `IPlatformAdminContext` parameter (not just a reason string); the context object is only constructible if the caller has demonstrated PlatformAdmin status (via `currentUser.IsPlatformAdmin == true`). Pro: stronger compile-time guarantee that bypass is intentional. Con: worker + webhook paths have no `ICurrentUser.IsPlatformAdmin` — they're platform-level; the contract would need an escape hatch for them, weakening the guarantee.

**Verdict default**: defer. The arch test + reason string + log line are sufficient discipline for Phase 1.5.

### O5 — Should M.9 also ship an audit-log read endpoint?

OPS.M.8 §1.2 Open Question O2 deferred this. The M.9 bypass factory makes the endpoint trivial (one new handler that opens a bypass `IdentityDbContext` and reads `audit_log` with paging). Adds ~1 day.

**Verdict default**: defer (M.8's O2 already deferred it; M.9 is the mechanism, not the consumer).

### O6 — Should the per-statement binding overhead be benchmarked before shipping?

The `SET LOCAL` per-command is documented as <1ms. Should we measure on the actual workload? k6 load test (Slice OPS.3) would catch any regression. Adds ~half a day for the benchmark.

**Verdict default**: defer to Slice OPS.3 launch hardening. M.9 ships; the benchmark verifies post-ship.

### O7 — Should `BackgroundTenantScope` be a separate AsyncLocal or piggy-back on `RlsBypassScope`?

D5 introduced `BackgroundTenantScope` as a fallback chain. A simpler shape: the M.6 behavior just stamps a `currentUser`-shaped scope into AsyncLocal that overrides `ICurrentUser.TenantId` for the duration. Pro: one AsyncLocal, simpler interceptor. Con: conceptual confusion — `ICurrentUser` is supposed to be the per-request abstraction; overriding it via AsyncLocal feels like a layering violation.

**Verdict default**: separate scope (the D5 shape). Cleaner mental model.

### O8 — Should the `tenant_memberships` carve-out (D11) be revisited at all?

D11 picks "carve out entirely". An intermediate shape: a more permissive policy `USING (user_id = current_user_id GUC OR tenant_id = current_setting('app.tenant_id', true)::uuid OR bypass)`. This requires a new GUC `app.current_user_id` set by the middleware (additive). Pro: stronger DB-layer enforcement; the bootstrap-path read (which filters by `user_id`) succeeds because the user_id GUC is set first. Con: an extra GUC; another `set_config` per command; mental complexity.

**Verdict default**: stay with the carve-out (D11). The intermediate shape is Phase 2 hardening.

### O9 — Should M.9 introduce a `TenantBoundary` arch-test that asserts every cross-tenant code site uses the bypass factory?

A stronger discipline: a Roslyn analyzer (or post-build IL scan) that asserts every method body that contains a query against an RLS-protected table either (a) is invoked from a `TenantAuthorizationBehavior`-gated MediatR handler, OR (b) opens a bypass scope. Pro: compile-time enforcement; can't miss a cross-tenant read by mistake. Con: heavy infrastructure (Roslyn analyzer to write + maintain); the AsyncLocal nature of the bypass makes static analysis fragile.

**Verdict default**: defer. The Step 12 arch test (constructor-injection allow-list) + the §9 guard rails + the M.10 dynamic sweep is sufficient for Phase 1.5. Phase 2 hardening can promote.

---

## Appendix C — Pitfalls & lessons learned from prior slices

Carried forward from OPS.M.4 / M.5 / M.6 / M.7 / M.8 implementation experiences. These are the "things that bit us" that M.9 must avoid.

### C.1 Outbox transaction ordering (from OPS.M.4)

The `DomainEventOutboxInterceptor` (verified `src/VrBook.Infrastructure/Outbox/DomainEventOutboxInterceptor.cs:31-92`) runs as a `SaveChangesInterceptor`. It writes `OutboxMessage` rows in the same SaveChanges transaction as the aggregate. **M.9 caveat**: if the bypass-scoped DbContext writes an outbox row, the row's `tenant_id` (NULL — outbox doesn't carry one) lands fine, BUT the downstream event payload (which carries the tenant id) is the consumer's contract. Verify Step 7-10 that event payloads are still correct under bypass.

### C.2 Sealed DbContext + interceptor registration (from OPS.M.3)

All DbContexts are `sealed` (verified §3 Q3). The `DbContextOptionsBuilderExtensions.UseRlsTenantGuc(sp)` extension wires interceptors via `options.AddInterceptors(...)`; this works for sealed contexts. No subclassing required.

### C.3 Migrator path uses a slim service set (from OPS.M.3 + OPS.M.5)

`VrBook.Migrator/Program.cs` registers a minimal DI graph (no `ICurrentUser`, no MediatR). The M.9 interceptor depends on `ICurrentUser` — registering it on the migrator's DbContext would `NullReferenceException`. **Mitigation**: the migrator's `AddXxxDbContextForMigrator(...)` variants explicitly do NOT call `UseRlsTenantGuc(sp)`. Documented in Step 4 pitfalls.

### C.4 Background command tenant-stamping is fragile (from OPS.M.6)

The Sync worker's `Workers.Sync/Program.cs:96` already pushes `tenant_id` into Serilog context per-feed; M.9 adds the analogous `BackgroundTenantScope` push. They're parallel mechanisms (one is for log enrichment, the other for RLS GUC). Easy to forget one when adding a new worker. **Mitigation**: a future code template / cookiecutter for new workers should include both. M.9 documents the pattern in the runbook.

### C.5 EF Core 8 + Npgsql connection sharing (from OPS.M.5)

EF Core 8 + Npgsql 8 share connections within a `DbContext` across queries until the context disposes. The `SET LOCAL` semantics in §4.3 D3 rely on per-statement transaction scoping; verify in Step 1 that EF wraps each query in an implicit transaction (which it does — EF Core 8's `IExecutionStrategy` default).

### C.6 EF query caching is per-process, not per-tenant (general)

EF's plan cache is keyed by the SQL text. The query text doesn't include the tenant id (which lives in the GUC, not the parameters). So one cached query plan serves all tenants — correct behavior. The plan caching does NOT leak tenant data across requests because the GUC is evaluated at runtime per-query.

### C.7 LinqKit / dynamic LINQ (not used today)

If a future slice introduces LinqKit or dynamic LINQ, the bypass scope must wrap the entire query construction + execution. AsyncLocal handles this; documented as a future-pitfall reminder.

---

## Appendix D — Sample structured log lines after M.9

For the operator's grep convenience, here are the canonical log shapes emitted by M.9 components.

**Interceptor (Debug level, high volume)**:
```
2026-06-28T14:32:18.422Z [DBG] RLS GUC stamped: tenant_id=8c4d2e1a-9b3f-4a2c-8d5e-1f6b7c8d9e0a, is_platform_admin=false
```

**Bypass factory open (Information level, audit trail)**:
```
2026-06-28T14:32:19.103Z [INF] RLS bypass open for IdentityDbContext (reason=admin.platform.list-tenants)
```

**Bypass factory open with caller context (when the caller is identifiable)**:
```
2026-06-28T14:32:19.103Z [INF] RLS bypass open for IdentityDbContext (reason=admin.platform.list-tenants) {ActorUserId=a1b2c3d4-...}
```

**Sync worker bootstrap**:
```
2026-06-28T00:05:00.000Z [INF] RLS bypass open for SyncDbContext (reason=sync-worker.list-due-feeds)
2026-06-28T00:05:00.142Z [INF] Sync Worker started. Due feeds: 3
2026-06-28T00:05:00.143Z [INF] {tenant_id=8c4d... channel_feed_id=fd01...} ...
```

**Stripe webhook**:
```
2026-06-28T14:33:42.001Z [INF] RLS bypass open for PaymentDbContext (reason=stripe-webhook)
2026-06-28T14:33:42.002Z [INF] RLS bypass open for IdentityDbContext (reason=stripe-account-lookup)
```

**RLS rejection (production red flag)**:
```
2026-06-28T14:34:11.789Z [ERR] System.InvalidOperationException: new row violates row-level security policy for table "bookings" {request=PlaceBookingCommand actor=... tenant_id=...}
```

Operators grep `RLS bypass open` for after-the-fact audit. Grep `row-level security policy` for production incident triage.

---

**Plan ends.**
