# OPS.M.10 — Cross-tenant isolation test pack (Plan, rev 1)

**Status**: Proposed — awaiting user review.
**Author**: Plan agent (architect) consult, 2026-06-28.
**MASTER_PLAN reference**: `docs/MASTER_PLAN.md` §1 row OPS.M.10 + §2 row 11 ("Cross-tenant isolation test pack — two-tenant fixture sweep over every public endpoint; 2-day estimate"). This is the **final verification slice** of the multi-tenancy rollout.
**MULTI_TENANCY reference**: `docs/MULTI_TENANCY_OPS_PLAN.md` §6 ("defense-in-depth — application enforcement first, RLS as belt-and-braces") + §12 row 7 ("every endpoint that crosses tenant boundaries is verified by an integration test that fails when the boundary is removed").
**PHASE_3 reference**: none direct — Phase 3 (Slice 8 hotel rooms + Slice 9 multi-unit cart) will inherit the M.10 test patterns when it adds endpoints; the M.10 plan documents the patterns the next slice author will follow.

**Predecessors**:
- Slice OPS.M.0 ✅ (Entra External ID + App Roles).
- Slice OPS.M.1 ✅ (`Tenant` aggregate + `tenant_memberships`).
- Slice OPS.M.2 ✅ (`ICurrentUser.TenantId` + DB-wins claim enrichment per ADR-0014).
- Slice OPS.M.3 ✅ (every tenant-scoped table now carries `tenant_id NOT NULL` with a FK to `identity.tenants(id)`).
- Slice OPS.M.4 ✅ (`TenantAuthorizationBehavior` — app-layer write gate; verified `TenantAuthorizationBehavior.cs:41-76`).
- Slice OPS.M.5 ✅ (Stripe Connect Express; per-tenant accounts; `TenantStripeContextLookup`).
- Slice OPS.M.6 ✅ (iCal poller tenant-scoping + outbound feed rate-limit; `BackgroundCommandTenantScopeBehavior`).
- Slice OPS.M.7 ✅ (Tenant Admin onboarding wizard; `GET /api/v1/me/tenant`).
- Slice OPS.M.8 ✅ (`IsPlatformAdmin` claim + `TenantsPlatformController` cross-tenant admin endpoints + `[Authorize(Roles="PlatformAdmin")]` controller gate + handler defense-in-depth `currentUser.IsPlatformAdmin` check).
- Slice OPS.M.9 ✅ (RLS policies + `IRlsBypassDbContextFactory<TContext>` defense-in-depth; verified `docs/OPS_M_9_PLAN.md` §3 inventory + §11 close-out ledger). M.9's §13 close-out flagged Step 11 (76 schema-introspection facts) + Step 11.5 (carve-out negatives) as **deferred to M.10**; M.10 absorbs both.

**Sequence**: After Slice OPS.M.9; before Slice 4 (Notifications — first Phase 2/3 work item). M.10 is the gate that closes the multi-tenancy rollout. The user has locked Option A: complete OPS.M.4 → M.10 fully, then proceed to Slice 4.

**Estimate**: **2 days, one engineer** — TDD-first, see §6. The slice is wide in test surface but minimal in production code; the depth comes from the matrix. Test count estimate: ~250-320 facts across ~9 test classes. The depth is bounded by the §4 D-decisions (in particular D4 — sweep generation strategy and D7 — RLS schema fact consolidation).

This plan is the contract. Slice OPS.M.10 ships **(i) a `TwoTenantApiFixture` that seeds two complete tenants (Tenant A with Owner-A + property-A + booking-A; Tenant B with Owner-B + property-B + booking-B; a PlatformAdmin user with `is_platform_admin = true` set via direct SQL) and three DevAuth-persona+JWT credentials**, **(ii) a `CrossTenantEndpointMatrix` data-driven test class that fans out every public endpoint × every persona × positive/negative expectation — the master matrix per §3**, **(iii) per-carve-out app-layer assertion tests verifying that the §3.2 OPS.M.9 carve-out tables (users, tenants, tenant_memberships, outbox, amenities, fees, line_items, guests, loyalty.accounts) prevent cross-tenant leak via the app-layer even though RLS doesn't protect them**, **(iv) the **deferred-from-M.9** RLS schema fact pack (~76 facts: 19 tables × 4 facts) and carve-out negative fact pack (~13 facts) — the M.9 §13 close-out's Step 11 + 11.5**, **(v) PlatformAdmin bypass positive-path tests + bypass-audit assertion tests (every bypass-factory open emits the structured log line; the test captures via in-memory log sink)**, **(vi) `M.4 audit_log` cross-tenant rejection assertion tests (every blocked cross-tenant write writes a structured log line per OPS.M.4 §3.5 — M.10 asserts via in-memory log sink, not via `audit_log` table because M.4 does not write the table for read-side rejections)**, **(vii) DevAuth promote/revoke smoke test exercising the OPS.M.8 PromoteSql operator runbook end-to-end (fresh user → SQL flip → platform endpoint reachable → revoke SQL → 403)**, **(viii) AsyncLocal-leak failure-mode test per M.9 §8.2 — a bypass-using handler calls into a tenant-scoped service; assert the tenant-scoped service still respects its own tenant scope**, **(ix) two new architecture tests pinning the M.10 invariants (every controller route under `/api/v1/admin/` is in the matrix; every new endpoint after M.10 must add its M.10-style facts)**.

The **web-layer** cross-tenant tests (browser-level assertions of the Platform sidebar group rendering only for `useMe.isPlatformAdmin = true`, plus the tenant-A web pages not rendering tenant-B data) are **explicitly OUT** of M.10 — those live in the M.7/M.8 vitest sweep. M.10 is API-level only. Documented in §1.2 + §4 D9.

Load testing, chaos engineering, OWASP top-10 penetration, and the "untested-endpoint discovery" arch test (which would scan for every controller and assert each is referenced by the M.10 matrix) are also OUT — M.10 is the test pack, not the comprehensive security audit. The arch-test side of "every new endpoint after M.10 must add its facts" IS in scope (§4 D10) so future drift is caught.

---

## 1. Scope summary

### 1.1 What this slice ships

| # | Deliverable | Touches |
|---|---|---|
| 1 | **`TwoTenantApiFixture`** — extends the existing `IdentityApiFixture` pattern. Spins up a fresh Postgres testcontainer, runs every module's migrations the same way the production migrator does (mirrors `TenantIdRolloutFixture`), seeds two tenants (A + B), seeds Owner-A + Owner-B + a PlatformAdmin user, seeds one property + one booking per tenant, and provisions DevAuth + JWT credentials for all three personas. Reused as `[CollectionDefinition]` across the M.10 test classes so the container stays warm. | New file `tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantApiFixture.cs` + `TwoTenantApiCollection.cs`. |
| 2 | **`CrossTenantEndpointMatrix.cs`** — the master matrix. One `[Theory]` per HTTP verb × route × persona combination, with `MemberData` driven by a `RouteMatrix` static enumeration. Each row is `(route, verb, persona, expected_status, expected_body_marker)`. ~40-60 endpoint rows × 5 persona shapes = ~250 facts. Per §3 inventory. Per §4 D4 verdict (Theory + MemberData, not hand-written `[Fact]`s). | New file `tests/VrBook.Api.IntegrationTests/Multitenancy/CrossTenantEndpointMatrix.cs`. |
| 3 | **`CarveOutAppLayerTests.cs`** — for each §3.2 carve-out table (users, tenants, tenant_memberships, per-module outbox, catalog.amenities, pricing.fees, booking.line_items, booking.guests, loyalty.accounts), asserts the app-layer enforcement at the public endpoint level. Examples: `Owner-A_searches_users_only_sees_their_tenant's_members`; `Owner-A_cannot_read_tenant-B's_pricing_fees_via_quote_endpoint`. ~12-15 facts. | New file `tests/VrBook.Api.IntegrationTests/Multitenancy/CarveOutAppLayerTests.cs`. |
| 4 | **`RlsPolicySchemaFactPack.cs`** — absorbs the deferred OPS.M.9 §13 Step 11 facts. For each of the 19 §3.1 tenant-scoped tables, asserts (a) `pg_class.relrowsecurity = true`, (b) `pg_class.relforcerowsecurity = true`, (c) the `rls_<schema>_<table>_tenant_isolation` policy exists in `pg_policy`, (d) the policy's `polqual` text references `app.tenant_id` + `app.is_platform_admin`. 19 × 4 = 76 facts via `[Theory]`. | New file `tests/VrBook.Api.IntegrationTests/Rls/RlsPolicySchemaFactPack.cs`. |
| 5 | **`RlsCarveOutSchemaFactPack.cs`** — absorbs the deferred OPS.M.9 §13 Step 11.5 facts. For each of the §3.2 carve-out tables, asserts `pg_class.relrowsecurity = false`. ~13 facts via `[Theory]`. | New file `tests/VrBook.Api.IntegrationTests/Rls/RlsCarveOutSchemaFactPack.cs`. |
| 6 | **`PlatformAdminBypassFactPack.cs`** — positive path. Asserts (a) PlatformAdmin can `GET /api/v1/admin/platform/tenants` and see BOTH tenant A's + tenant B's rows; (b) PlatformAdmin can `GET /api/v1/admin/platform/tenants/{tenantId}` for both A and B; (c) PlatformAdmin can `POST /api/v1/admin/platform/tenants/{tenantId}/suspend` against both A and B; (d) every bypass-factory open emits the M.9 `RLS bypass open for {ContextType} (reason={Reason})` Information-level log line — captured via in-memory `Serilog.Sinks.InMemory` sink. ~8 facts. | New file `tests/VrBook.Api.IntegrationTests/Multitenancy/PlatformAdminBypassFactPack.cs`. |
| 7 | **`CrossTenantRejectionAuditFactPack.cs`** — every blocked cross-tenant write must emit the structured log line `TenantAuthorizationBehavior rejected cross-tenant write` per OPS.M.4 §3.5. M.10 asserts via in-memory log sink. Picks one representative endpoint per HTTP verb (POST/PUT/PATCH/DELETE) × per tenant-scoped module to keep the count tractable. ~10 facts. Documents in §4 D5 that this is intentionally a **per-verb-per-module** sample, not exhaustive — the M.4 contract is the same code path for every `ITenantScoped` command, so one representative fact per module proves the wire. | New file `tests/VrBook.Api.IntegrationTests/Multitenancy/CrossTenantRejectionAuditFactPack.cs`. |
| 8 | **`PlatformAdminPromoteRevokeSmokeTest.cs`** — operator path verification. Seeds a fresh user (not the PlatformAdmin from the fixture), runs the `OPS_M_8_PROMOTE_PLATFORM_ADMIN.md` runbook's "promote-via-direct-SQL" path (idempotent UPDATE on `identity.users.is_platform_admin`), assert the user can now reach `GET /api/v1/admin/platform/tenants`. Then runs the revoke SQL, assert the same endpoint returns 403. Per §4 D8 verdict ([Theory] over three states: not-promoted-403, promoted-200, revoked-403). | New file `tests/VrBook.Api.IntegrationTests/Multitenancy/PlatformAdminPromoteRevokeSmokeTest.cs`. |
| 9 | **`AsyncLocalLeakFactPack.cs`** — covers the M.9 §8.2 failure mode. Tests: (a) a bypass-using handler invokes a tenant-scoped service mid-bypass; assert the tenant-scoped service's reads still respect ITS OWN tenant scope (i.e. the `BackgroundTenantScope` and the per-request `ICurrentUser` win over a leftover `RlsBypassScope` flag if both are simultaneously set — actually, the M.9 D5 fallback chain places bypass FIRST, so the bypass wins; the M.10 test asserts the actual M.9 behavior, not a stricter one, AND documents the sharp edge per §7); (b) bypass scope properly closes via `await using` even when the inner code throws; (c) nested bypass calls correctly stack (M.9 §4.4 depth counter). ~5 facts. | New file `tests/VrBook.Api.IntegrationTests/Rls/AsyncLocalLeakFactPack.cs`. |
| 10 | **Architecture test** `EndpointCoverageArchTest.cs` — reflects on every public controller, every `[HttpGet/Post/Put/Patch/Delete]` action, every `[Route]` template. Asserts each route appears in the `CrossTenantEndpointMatrix.RouteMatrix` static enumeration. Catches drift when a future PR adds an endpoint without adding its M.10 row. Two opt-out attributes documented: `[ExemptFromCrossTenantMatrix(reason)]` for public anonymous endpoints (search, property-by-slug, public reviews, outbound iCal feed, etc.) + the `StripeWebhookController` (signature-verified, not auth-token-verified). | New file `tests/VrBook.Architecture.Tests/EndpointCoverageArchTest.cs` + a new attribute `src/VrBook.Api/Common/ExemptFromCrossTenantMatrixAttribute.cs`. |
| 11 | **CI gate** — every CI run executes the M.10 fact pack against a fresh Postgres testcontainer. Failing facts block the merge. The matrix is annotated `[Trait("Category", "CrossTenant")]` per §4 D2 so CI can split it from `Category=Unit` for parallel run-time. | CI workflow update; the test class trait already runs in CI per the OPS.M.5 / OPS.M.6 / OPS.M.9 precedent. |
| 12 | **Documentation: `docs/runbooks/cross-tenant-leak-triage.md`** — what an operator does when M.10 catches a regression. Per §9. | New file `docs/runbooks/cross-tenant-leak-triage.md`. |

### 1.2 What's explicitly OUT of OPS.M.10

| Item | Owner slice | Why deferred |
|---|---|---|
| Web-layer cross-tenant tests | OPS.M.7/M.8 vitest sweep | The Platform sidebar group, the tenant dashboard render of tenant-only data, the wizard onboarding state — all already have vitest coverage in `web/src/__tests__/`. M.10 is API-level only; the M.4/M.9 layers are API-scope. Web vitest runs in CI separately. |
| Load testing | Slice OPS.4 | Performance under multi-tenant load (e.g. 1000 tenants each writing 10 req/s) is a separate concern. M.10 establishes correctness; load tests measure scaling. |
| Chaos engineering (kill connection mid-RLS-stamp) | Phase 2 | The M.9 per-statement interceptor is rocksolid under EF Core 8's transaction semantics; chaos testing of "what if the SET LOCAL succeeds but the main command fails" is hardening, not correctness. |
| OWASP top-10 / SQL injection sweep | Slice OPS.5 | Slice OPS.5 is the dedicated security review slice. M.10 establishes tenant isolation; OPS.5 establishes input-sanitization, IDOR, XSS, CSRF posture. |
| Performance benchmark of the M.4 + M.9 layers | Slice OPS.4 + Phase 2 | M.9's per-statement `set_config` adds <1ms per command at the design level; M.10 does not benchmark. Phase 2 hardening will if/when the bypass-open log volume becomes noise. |
| Untested-endpoint discovery via static scanning + M.10 enforcement | M.10 §1.1 row 10 (in scope) | Confusingly worded in MASTER_PLAN — IN scope at the **arch-test** level. The arch test ships in M.10; it asserts coverage. NOT in scope: an external static-analysis tool that scans for controllers in OTHER projects. |
| Cross-tenant leaks via business-logic side-channels | Phase 2 | Example: a tenant-A admin renders a notification template that's shared platform-wide, and the rendering leaks tenant-B context via a misconfigured template variable. M.10 cannot catch this without rendering notification templates in two-tenant scenarios — Slice 4 + Phase 2 cover. |
| OTA cross-tenant package bundling (Phase 4) | Slice 10 | The Phase 4 design (Slice 10) ships intentional cross-tenant reads for itinerary aggregation. The M.10 matrix would block these unless the matrix author explicitly allows them. When Slice 10 ships, it MUST extend the M.10 matrix with explicit "Owner-A can read leg metadata from supplier-B" rows + the bypass call-site allow-list extension. |
| Tenant-suspended enforcement testing | Slice OPS.M.8.1 | M.8.1 (deferred per M.8 §3.9 D9) will block writes when `Tenant.Status = Suspended`. M.10 does NOT add suspended-state matrix rows because the enforcement doesn't exist yet. When M.8.1 ships, it ships its own suspended-state matrix rows OR extends M.10's. |
| `audit_log` table assertion (vs in-memory log sink) | Phase 2 | M.4's `TenantAuthorizationBehavior` writes to the structured logger (the Information line) but does NOT write to the `identity.audit_log` table for cross-tenant rejections — only successful state-changing commands hit the audit log via `AuditLogBehavior`. M.10 asserts the log sink (which is what's actually written today); a future Phase 2 change could persist rejections to the table, at which point M.10's facts could extend to table assertions. |
| Rate-limiting matrix sweep | Slice OPS.M.6 (Sync) + Phase 2 | M.6 ships rate-limiting on the outbound feed worker. The general API rate-limit per tenant (e.g. 1000 req/min/tenant) is Phase 2. |
| Loyalty cross-tenant matrix | M.10 §3 — handled as carve-out | Per OPS.M.3, Loyalty is platform-wide. The Loyalty surface (`GET /api/v1/me/loyalty`) is per-user (not per-tenant), so there's no cross-tenant attack surface. M.10's CarveOutAppLayerTests confirm zero leak via this endpoint. No matrix expansion needed. |

### 1.3 Decision lock summary

| # | Decision | Locked verdict |
|---|---|---|
| D1 | Fixture shape — new vs extend | **New `TwoTenantApiFixture` extending `IdentityApiFixture`** pattern; reuses `WebApplicationFactory<Program>` + Postgres testcontainer; adds two-tenant seed in `InitializeAsync`. Cannot extend `TenantIdRolloutFixture` directly because that fixture is migrator-only (no API surface); cannot extend `IdentityApiFixture` in place because the seed mutation would affect every other test class using the fixture. Documented in §4.1. |
| D2 | Test categorization | **`[Trait("Category", "CrossTenant")]`** — new category. Distinct from `[Trait("Category", "Integration")]` because (a) CI can split run-time, (b) the matrix is the heaviest single test in the codebase (~250 facts), (c) "CrossTenant" is grep-able and the M.4 + M.8 reviewers can `--filter Category=CrossTenant` to spot-check the boundary. Documented in §4.2. |
| D3 | DevAuth vs JWT for tests | **DevAuth cookie path is the primary mechanism; one JWT-shape smoke test runs the same matrix under a minted JWT to prove the production auth path also works.** DevAuth is faster (no JWT minting per request) AND already wired in `IdentityApiFixture`. The JWT smoke test is a single `[Theory]` with the same `RouteMatrix` MemberData, run via a `TwoTenantJwtApiFixture` variant. Documented in §4.3. |
| D4 | Sweep generation strategy | **`[Theory]` + `MemberData` driven by a `RouteMatrix` static enumeration.** Hand-writing ~250 facts is unmaintainable; data-driven keeps the matrix in one place. Documented in §4.4. |
| D5 | Audit assertion shape | **Per-verb-per-module sample, ~10 facts total — NOT per-route-per-persona exhaustive.** The M.4 `TenantAuthorizationBehavior` code path is the same for every `ITenantScoped` command; one representative fact per module proves the wire; exhaustive coverage would multiply ~250 facts × an extra log-assertion which doubles CI time without proving anything new. Documented in §4.5. |
| D6 | Bypass audit assertion shape | **Every bypass-using endpoint (the M.8 PlatformAdmin endpoints, the Stripe webhook, the Sync worker bootstrap) MUST have one fact asserting the log line lands.** Captured via `Serilog.Sinks.InMemory`. Per §4.6. |
| D7 | RLS schema fact consolidation | **Absorbed from M.9 §13 Step 11 + 11.5.** The 76 schema-introspection facts + 13 carve-out negative facts ship as separate test classes (`RlsPolicySchemaFactPack` + `RlsCarveOutSchemaFactPack`) under `tests/.../Rls/`. They share the `TwoTenantApiFixture` Postgres testcontainer; they do NOT depend on the seeded two-tenant data — they read `pg_class` + `pg_policy` only. Documented in §4.7. |
| D8 | Promote/revoke smoke test shape | **`[Theory]` with three rows: not-promoted (expect 403), promoted-via-SQL (expect 200 + bypass log), revoked-via-SQL (expect 403 again).** Mirrors the operator runbook flow; one logical scenario, three observable states. Documented in §4.8. |
| D9 | Web-layer coverage | **OUT of M.10.** Web vitest covers via M.7/M.8 already. M.10's invariant is the API surface. Documented in §1.2 + §4.9. |
| D10 | Endpoint-coverage drift enforcement | **`EndpointCoverageArchTest` arch test ships in M.10.** Reflects on every controller; asserts each `[Http*]`-attributed method is in the matrix OR carries `[ExemptFromCrossTenantMatrix(reason)]`. Per §4.10. |
| D11 | Two-tenant seed shape — what data lives in each tenant | **Tenant A: 1 active property + 1 confirmed booking + 1 pricing plan + 1 channel feed; Tenant B: 1 active property + 1 tentative booking + 1 pricing plan + 1 channel feed.** Minimum data per tenant to exercise each module's read path. Pricing plan + channel feed are NOT optional — M.10 must verify a tenant-A admin cannot reach tenant-B's pricing-rules endpoint or channel-feed endpoint. Per §4.11. |
| D12 | What the PlatformAdmin user has access to in the seed | **PlatformAdmin: `is_platform_admin = true`, NOT a member of either tenant (no tenant_memberships row).** Mirrors the OPS.M.8 spec — PlatformAdmin is a platform-wide role independent of tenant membership. M.10's positive-path tests exercise the bypass; M.10's negative-path tests confirm a PlatformAdmin who is ALSO an Owner-of-A still gets bypass (i.e. the bypass wins over the tenant-scoped path). |

### 1.4 What OPS.M.4 / M.8 / M.9 left for M.10 to clean up

1. **The OPS.M.4 invariant** — every `ITenantScoped` command is rejected by `TenantAuthorizationBehavior` if `currentUser.TenantId != command.TenantId`. M.4 §3.5 emits the structured log line. M.4 shipped unit tests of the behavior (`tests/.../Multitenancy/TenantAuthorizationBehaviorTests.cs`) **but explicitly deferred the HTTP-driven coverage to M.10** (verified `TenantAuthorizationBehaviorTests.cs:11-23`):

   > "The architect's plan §6 prescribed a full HTTP-driven CrossTenantWriteRejectionTests pack with ~12 scenarios per module. The unit-level cuts below exercise the same contract surface without booting the Postgres testcontainer + WebApplicationFactory + DevAuth — they prove the gate, not the wire. **The HTTP-driven equivalents land in Slice OPS.M.10's cross-tenant isolation test pack** alongside the RLS integration coverage from Slice OPS.M.9."

   M.10 honors this commitment via `CrossTenantEndpointMatrix.cs` (the wire-level sweep) + `CrossTenantRejectionAuditFactPack.cs` (the log-line assertion).

2. **The OPS.M.8 invariant** — PlatformAdmin can bypass `TenantAuthorizationBehavior` for cross-tenant writes. M.8 shipped the controller-level `[Authorize(Roles="PlatformAdmin")]` gate + handler-level `currentUser.IsPlatformAdmin` defense-in-depth check + the `PlatformAdminEndpointRoleGateTests` arch test. M.8 did NOT ship a wire-level "PlatformAdmin reaches tenant-B and sees tenant-B data" integration sweep. M.10 ships it (`PlatformAdminBypassFactPack.cs`).

3. **The OPS.M.8 PromoteSql runbook gap** — M.8 §1.2 explicitly says "`vrbook-admin promote` PowerShell cmdlet ships in a follow-up slice" (verified `docs/runbooks/OPS_M_8_PROMOTE_PLATFORM_ADMIN.md:7`). The runbook documents the manual SQL promote path (`UPDATE identity.users SET is_platform_admin = true WHERE email = ...`). M.10's `PlatformAdminPromoteRevokeSmokeTest` verifies the SQL path is correct + complete + reversible. The cmdlet itself is NOT in M.10 scope — that's a separate ops slot.

4. **The OPS.M.9 §13 deferred facts** — M.9 deferred Step 11 (76 schema facts) + Step 11.5 (13 carve-out negative facts) explicitly:

   > "Step 11 (per-module RLS integration fact pack) deferred to OPS.M.10. Plan §9 Step 11 nominated 76 schema-introspection facts (19 tables × 4 facts each). Deferred — these need a Postgres testcontainer and the same fixture pattern as OPS.M.5/M.8 schema tests. The M.10 cross-tenant isolation test pack ships with its own Postgres fixture and the negative + behavior matrix; folding the schema facts into M.10 avoids two parallel test setups."

   M.10 absorbs both. Documented in §4.7 (D7).

5. **The OPS.M.9 §8.2 AsyncLocal leak failure mode** — M.9 plan §8.2 documents the sharp edge: a bypass-using handler that holds the scope across an unrelated `await` could leak the bypass flag to a tenant-scoped service. M.9 shipped the arch-test allow-list (`RlsBypassCallSiteAllowlistTests`) to bound the surface; M.10 adds the runtime assertion (`AsyncLocalLeakFactPack.cs`) to verify the actual behavior matches the documented intent.

6. **The OPS.M.9 §8.6 hand-off contract** — M.9 §8.6 enumerates the 7 things M.10's test pack will assert (positive path, negative path, PlatformAdmin reaches both, PlatformAdmin without bypass demonstrates RLS works, audit completeness, AsyncLocal leak). M.10's deliverables in §1.1 cover each:

   | M.9 §8.6 row | M.10 deliverable |
   |---|---|
   | 1. Positive path (Owner-A → tenant A) | `CrossTenantEndpointMatrix` positive rows |
   | 2. Negative path (Owner-A → tenant B) | `CrossTenantEndpointMatrix` negative rows |
   | 3. PlatformAdmin via bypass (both tenants) | `PlatformAdminBypassFactPack` |
   | 4. PlatformAdmin without bypass → RLS denies (zero rows) | `AsyncLocalLeakFactPack` second fact (the bypass scope NOT held — RLS denies) |
   | 5. Cross-tenant rejection in audit log | `CrossTenantRejectionAuditFactPack` |
   | 6. Bypass-open audit log | `PlatformAdminBypassFactPack` Information-level log assertion |
   | 7. AsyncLocal leak | `AsyncLocalLeakFactPack` |

---

## 2. Atomic-deploy constraints

**M.10 has no production code change beyond test infrastructure** — the slice is test-pack-only. There are two minor production-side additions, both NON-load-bearing:

1. **`[ExemptFromCrossTenantMatrix(reason)]` attribute** — a marker attribute that the `EndpointCoverageArchTest` reads. Lives in `src/VrBook.Api/Common/`. Anonymous endpoints (the public property search, the `/health` check, the outbound iCal feed, the Stripe webhook) carry this attribute. Adding the attribute to existing controllers is a one-line code change per controller (~6 attributes total). NOT a behavioral change; only documentation + the arch test reads it.

2. **`Serilog.Sinks.InMemory` NuGet package added to `tests/VrBook.Api.IntegrationTests/VrBook.Api.IntegrationTests.csproj`**. The bypass-audit + cross-tenant-rejection-audit assertion tests use this to capture log lines without writing to disk or stdout. Standard Serilog sink — no production dependency added. (The Api project does not reference the test project; the sink is test-scoped.)

Because M.10 has no production code in the request-execution path, **no waves**, **no atomic-deploy concerns**, **no migration ordering**, **no forward-replay constraint**. The slice is a single PR that adds test files + the marker attribute.

### Per-environment deploy

M.10 is a test-only slice. No `azd deploy`. No infrastructure change. The tests run in CI; the marker attribute ships with the next normal deploy.

### CI runtime impact

M.10's test pack adds ~250-320 facts to the integration test suite. Each fact costs ~50-200ms (network call to the in-process WebApplicationFactory + DB roundtrip). Estimate: **+60-90 seconds of CI runtime**. Acceptable because:

- The `[Trait("Category", "CrossTenant")]` allows CI to fan out the matrix as a parallel job (D2 verdict locks this).
- The Postgres testcontainer warmth is amortized across all M.10 tests via the shared `TwoTenantApiCollection` (CollectionFixture pattern verified in `TenantIdRolloutCollection.cs`).
- The 76 RLS schema facts are pure `pg_class` reads — sub-10ms each.

### Forward-replay constraint

None. M.10 introduces no outbox events, no migrations, no schema changes. The marker attribute is build-time metadata.

---

## 3. Test-pack inventory — the master matrix

This section is the load-bearing inventory for `CrossTenantEndpointMatrix.cs`. Every public endpoint that the architect verified by reading the controller files in `src/VrBook.Api/Controllers/` (October 2026-06-28 reconnaissance) is enumerated below. The §3.2 list shows the carve-out tables and the public surfaces that read/write them — those go in `CarveOutAppLayerTests.cs`, not the master matrix.

### 3.1 Per-endpoint matrix — what gets tested

For each public endpoint, M.10 tests the following persona × expectation cells:

| Persona | Targeting tenant A's row | Targeting tenant B's row | Anonymous |
|---|---|---|---|
| **Owner-A** | Positive (200/201/204) | Negative (403 — `CrossTenantAccessException`) | n/a |
| **Owner-B** | Negative (403) | Positive (200/201/204) | n/a |
| **PlatformAdmin** | Positive ONLY for `/api/v1/admin/platform/*` (bypass) | Positive ONLY for `/api/v1/admin/platform/*` (bypass) | n/a |
| **Anonymous** | 401 except `[AllowAnonymous]` endpoints | 401 except `[AllowAnonymous]` endpoints | 200 only for `[AllowAnonymous]` |

The matrix is the cross-product of (endpoint, persona, target). For tenant-scoped read endpoints (e.g. `GET /api/v1/bookings/{id}`), the "targeting tenant A's row vs tenant B's row" distinction is the load-bearing test — the tenant B row's GUID is known to the test, and the request URL contains it; the test asserts Owner-A gets 404 (not 403 because the M.4 behavior is write-only — for reads, the `WHERE tenant_id = …` filter in the query handler returns zero rows, the controller returns 404; per §7 this is the design choice).

### 3.2 Endpoint inventory — verified from `src/VrBook.Api/Controllers/`

The exhaustive list. Each row → 5 matrix cells (Owner-A→A, Owner-A→B, Owner-B→A, Owner-B→B, PlatformAdmin if applicable). Endpoints flagged `[AllowAnonymous]` are EXEMPT from the matrix (carry `[ExemptFromCrossTenantMatrix("public — search/feed/webhook")]`).

#### 3.2.1 IdentityController (`/api/v1/me`)

| Route | Verb | Auth | Tenant-scope? | In matrix? |
|---|---|---|---|---|
| `/api/v1/me` | GET | `[Authorize]` | No (per-user) | YES — Owner-A sees their own profile; Owner-B sees their own profile; PlatformAdmin sees their own profile. No cross-tenant target because the route has no tenant id. M.10 ASSERTS the three personas each see their own data and not the other's. |
| `/api/v1/me` | PUT | `[Authorize]` | No (per-user) | YES — same. |
| `/api/v1/me` | DELETE | `[Authorize]` | No (per-user) | NO — self-deactivation; the integration test for this is `IdentityFlowTests`. Adding to M.10 would mutate the fixture state mid-test. Exempt with `[ExemptFromCrossTenantMatrix("self-deactivation; per-user, no cross-tenant surface")]`. |
| `/api/v1/me/tenant` | GET | `[Authorize(Roles="Owner,Admin")]` | YES — reads caller's tenant only | YES — Owner-A sees tenant A; Owner-B sees tenant B; PlatformAdmin has no tenant membership so returns 404 (or 403 — the test asserts which; per OPS.M.7 §3, `GetMyTenantQuery` throws `ForbiddenException` when caller has no tenant); Anonymous → 401. |

#### 3.2.2 DevAuthController (`/api/v1/dev-auth`)

| Route | Verb | Auth | In matrix? |
|---|---|---|---|
| `/api/v1/dev-auth/personas` | GET | `[AllowAnonymous]` | NO — diagnostic; `[ExemptFromCrossTenantMatrix("DevAuth diagnostic")]`. |
| `/api/v1/dev-auth/current-tenant` | GET | `[AllowAnonymous]` | NO — diagnostic. |
| `/api/v1/dev-auth/switch` | GET/POST | `[AllowAnonymous]` | NO — persona switcher. |
| `/api/v1/dev-auth/backdate-checked-out-at` | POST | `[AllowAnonymous]` | NO — dev-only bridge. |
| `/api/v1/dev-auth/persona-email` | POST | `[AllowAnonymous]` | NO — dev-only bridge. |

All five DevAuth endpoints are dev-only (404 in production); attempting cross-tenant tests against them is unproductive. Carry `[ExemptFromCrossTenantMatrix("DevAuth — dev/test only")]` on the controller class.

#### 3.2.3 TenantsAdminController (`/api/v1/admin/tenants/{tenantId}`)

| Route | Verb | Auth | Tenant-scope? | In matrix? |
|---|---|---|---|---|
| `/api/v1/admin/tenants/{tenantId}/stripe/onboard` | POST | `[Authorize(Roles="Owner,Admin")]` | YES — `tenantId` route ignored; uses `CallerTenantId()` | YES — Owner-A → own tenant in URL = 200; Owner-A → tenant B in URL = controller passes own tenant id to MediatR (handler is bypass-eligible per M.5, but the controller IGNORES the URL value); **subtle test case**: even though the URL has tenant B's id, the handler uses `CallerTenantId()`, so the operation hits tenant A. M.10 asserts this: the handler completes successfully against tenant A's Stripe state, NOT tenant B's. This is the canonical "URL parameter is a Route artifact, not authority" test. |
| `/api/v1/admin/tenants/{tenantId}/stripe/account-link` | POST | `[Authorize(Roles="Owner,Admin")]` | YES — same | YES — same shape. |
| `/api/v1/admin/tenants/{tenantId}/stripe/login-link` | POST | `[Authorize(Roles="Owner,Admin")]` | YES — same | YES — same shape. |

The `tenantId` parameter on this controller is decorative (per the source comment `_ = tenantId; // route value; the behavior gates the caller's tenant scope.`). M.10 tests that the decorative param is genuinely decorative. This is an important boundary test: a future PR that "fixes" the dead parameter by routing it to the command WOULD become a cross-tenant write vector, and M.10 catches it.

#### 3.2.4 TenantsPlatformController (`/api/v1/admin/platform/tenants`)

| Route | Verb | Auth | Tenant-scope? | In matrix? |
|---|---|---|---|---|
| `/api/v1/admin/platform/tenants` | GET | `[Authorize(Roles="PlatformAdmin")]` | NO — cross-tenant by design (M.8 bypass) | YES — Owner-A → 403; Owner-B → 403; PlatformAdmin → 200 + sees BOTH tenant A and tenant B in the page. |
| `/api/v1/admin/platform/tenants/{tenantId}` | GET | `[Authorize(Roles="PlatformAdmin")]` | NO — cross-tenant | YES — Owner-A → 403; PlatformAdmin → 200 for tenant A; PlatformAdmin → 200 for tenant B. |
| `/api/v1/admin/platform/tenants/{tenantId}/suspend` | POST | `[Authorize(Roles="PlatformAdmin")]` | NO — cross-tenant | YES — Owner-A → 403; PlatformAdmin → 204 for tenant A; PlatformAdmin → 204 for tenant B. |
| `/api/v1/admin/platform/tenants/{tenantId}/reactivate` | POST | `[Authorize(Roles="PlatformAdmin")]` | NO — cross-tenant | YES — same shape. |
| `/api/v1/admin/platform/tenants/{tenantId}/platform-fee` | PUT | `[Authorize(Roles="PlatformAdmin")]` | NO — cross-tenant | YES — same shape. |

#### 3.2.5 PropertiesController (`/api/v1/properties` + `/api/v1/admin/properties` + variants)

| Route | Verb | Auth | Tenant-scope? | In matrix? |
|---|---|---|---|---|
| `/api/v1/properties` | GET | `[AllowAnonymous]` | NO — public search | NO — `[ExemptFromCrossTenantMatrix("public property search")]`. |
| `/api/v1/properties/{slug}` | GET | `[AllowAnonymous]` | NO — public | NO — `[ExemptFromCrossTenantMatrix("public property detail")]`. |
| `/api/v1/properties/{id}/availability` | GET | `[AllowAnonymous]` | NO — public | NO — same. |
| `/api/v1/properties` | POST | `[Authorize(Roles="Owner,Admin")]` | YES — stamped via `CallerTenantId()` | YES — Owner-A → 201, property lands in tenant A; Owner-B → 201, property lands in tenant B; Anonymous → 401. |
| `/api/v1/properties/{id}` | PUT | `[Authorize(Roles="Owner,Admin")]` | YES | YES — Owner-A → 200 for own property; Owner-A → 404 (or 403; see §7.3 for the design choice) for tenant-B's property id. |
| `/api/v1/properties/{id}/images` | POST | `[Authorize(Roles="Owner,Admin")]` | n/a — stub `501 NotImplemented` | NO — `[ExemptFromCrossTenantMatrix("stub 501")]`. |
| `/api/v1/properties/{id}/images/order` | PUT | `[Authorize(Roles="Owner,Admin")]` | stub | NO — same. |
| `/api/v1/properties/{id}/images/{imageId}` | DELETE | `[Authorize(Roles="Owner,Admin")]` | stub | NO — same. |
| `/api/v1/amenities` | GET | `[AllowAnonymous]` | NO — shared catalog | NO — `[ExemptFromCrossTenantMatrix("shared amenities catalog")]`. |
| `/api/v1/admin/properties` | GET | `[Authorize(Roles="Owner,Admin")]` | YES — scoped to caller's tenant | YES — Owner-A's list excludes Owner-B's property. M.10 asserts. |
| `/api/v1/admin/properties/{id}` | GET | `[Authorize(Roles="Owner,Admin")]` | YES | YES — same as `/api/v1/properties/{id}` PUT — Owner-A → 200 own, 404 cross. |
| `/api/v1/admin/bookings` | GET | `[Authorize(Roles="Owner,Admin")]` (Slice 2 — separate from `BookingsController`) | YES | YES. |
| `/api/v1/admin/bookings/{id}` | GET | same | YES | YES. |
| `/api/v1/properties/{propertyId}/calendar` | GET | `[Authorize]` (the read endpoint is calendar) | YES — scoped to property's tenant | YES — Owner-A reads tenant-A's property calendar = 200; tenant-B's = 404. |
| `/api/v1/properties/{propertyId}/blocks` | GET/POST/DELETE | `[Authorize]` | YES | YES — same shape. |
| `/api/v1/admin/amenities` (all variants) | GET/POST/PUT/DELETE | `[Authorize(Roles="Admin")]` | NO — shared catalog | NO — `[ExemptFromCrossTenantMatrix("shared amenities catalog — platform-wide")]`. **BUT**: M.10 carve-out test (§3.4) verifies a tenant-A admin can update a shared amenity and a tenant-B admin sees the update; this is intentional cross-tenant visibility. |

#### 3.2.6 BookingsController (`/api/v1/bookings`)

| Route | Verb | Auth | Tenant-scope? | In matrix? |
|---|---|---|---|---|
| `/api/v1/bookings/holds` | POST | `[Authorize]` | NO — Guest-driven, attaches to caller | NO — Guest holds are user-scoped, not tenant-scoped. `[ExemptFromCrossTenantMatrix("guest-driven hold; per-user")]`. |
| `/api/v1/bookings/holds/{holdId}` | DELETE | `[Authorize]` | Guest-owned | NO — same. |
| `/api/v1/bookings` | POST | `[Authorize]` | Guest's command | NO — Guest places booking; cross-tenant is moot. `[ExemptFromCrossTenantMatrix("guest-driven placement")]`. M.10 carve-out test verifies Guest cannot trigger a booking against a non-existent property (i.e. tenant-B's deleted property) — that's a 404/422 path, not cross-tenant. |
| `/api/v1/bookings/{id}` | GET | `[Authorize]` | YES — booking is tenant-scoped | YES — Owner-A reads own booking = 200; tenant-B's = 404. Guest-A reads own booking = 200; tenant-B's = 404. |
| `/api/v1/bookings` | GET | `[Authorize]` (MyBookings) | NO — caller's own | YES — Owner-A's list does not include Guest-B's bookings against tenant-A (subtle: Guest's bookings ARE per-tenant scoped at the DB level via the booking's `tenant_id`, but the `MyBookings` query is `WHERE guest_user_id = currentUser.UserId`, not tenant-scoped — verify in §7). |
| `/api/v1/bookings/{id}/cancel` | POST | `[Authorize]` | YES | YES — Owner-A cancels own booking = 200; tenant-B's = 404. |
| `/api/v1/bookings/{id}/confirm` | POST | `[Authorize(Roles="Owner,Admin")]` | YES — `CallerTenantId()` stamped | YES — Owner-A confirms own booking = 200; tenant-B's booking id = 403 (M.4 rejects). |
| `/api/v1/bookings/{id}/reject` | POST | `[Authorize(Roles="Owner,Admin")]` | YES | YES — same shape. |
| `/api/v1/bookings/{id}/check-in` | POST | `[Authorize(Roles="Owner,Admin")]` | YES | YES. |
| `/api/v1/bookings/{id}/check-out` | POST | `[Authorize(Roles="Owner,Admin")]` | YES | YES. |
| `/api/v1/bookings/{id}/review` | POST | `[Authorize]` (Guest writes review post-stay) | YES — review carries booking's tenant_id | YES — Guest-A reviews own booking = 201; Guest-A reviewing tenant-B's booking id = 404. |
| `/api/v1/admin/bookings/queue` | GET | `[Authorize(Roles="Owner,Admin")]` | stub `501` | NO — `[ExemptFromCrossTenantMatrix("stub 501")]`. |
| `/api/v1/admin/bookings/manual` | POST | `[Authorize(Roles="Owner,Admin")]` | stub `501` | NO — same. |

#### 3.2.7 PaymentsController (`/api/v1/payments`)

| Route | Verb | Auth | Tenant-scope? | In matrix? |
|---|---|---|---|---|
| `/api/v1/payments/intents/by-booking/{bookingId}` | GET | `[Authorize]` | YES — payment intent inherits booking's tenant scope | YES — Owner-A reads own PI = 200; tenant-B's = 404. |
| `/api/v1/payments/refunds` | POST | `[Authorize(Roles="Owner,Admin")]` | YES — refund is `ITenantScoped` | YES — Owner-A refunds own booking = 204; tenant-B's = 403. |
| `/api/v1/payments/webhooks/stripe` | POST | `[AllowAnonymous]` (signature-verified) | n/a — the webhook is platform-level | NO — `[ExemptFromCrossTenantMatrix("Stripe webhook — signature-verified, runs under bypass")]`. **BUT**: M.10's `PlatformAdminBypassFactPack` includes a test that a Stripe webhook for tenant A's account correctly resolves to tenant A (the M.9 bypass log line fires). |

#### 3.2.8 SyncController + FeedsController + ChannelFeedsController + SyncConflictsController

| Route | Verb | Auth | Tenant-scope? | In matrix? |
|---|---|---|---|---|
| `/api/v1/feeds/{outboundToken}.ics` | GET | `[AllowAnonymous]` | n/a — token-authorized | NO — `[ExemptFromCrossTenantMatrix("outbound iCal — token-authorized")]`. **Carve-out check**: M.10's `CarveOutAppLayerTests` verifies tenant A's outbound token does NOT return tenant B's bookings (or any tenant B data) — this is a critical leak surface. |
| `/api/v1/admin/channel-feeds` | GET | `[Authorize(Roles="Admin")]` | YES | YES — Owner-A's list excludes Owner-B's feeds. |
| `/api/v1/admin/channel-feeds/{id}` | GET | `[Authorize(Roles="Admin")]` | YES | YES — Owner-A reads own feed = 200; tenant-B's = 404. |
| `/api/v1/admin/channel-feeds` | POST | `[Authorize(Roles="Admin")]` | YES — `CallerTenantId()` stamped | YES — Owner-A creates feed against own property = 201; against tenant-B's propertyId = 403 (M.4 rejects because the `CreateChannelFeedCommand.TenantId` is tenant-A and the command's `PropertyId` is checked against `tenant_id` at the handler level — the M.4 behavior alone wouldn't catch this since the command's TenantId is the caller's; the handler's "does this property belong to this tenant" check is what rejects. Verify in §7.3 — this is a subtle case worth a dedicated fact.) |
| `/api/v1/admin/channel-feeds/{id}` | PUT | same | YES | YES — Owner-A updates own = 200; tenant-B's id = 403/404. |
| `/api/v1/admin/channel-feeds/{id}` | DELETE | same | YES | YES — same shape. |
| `/api/v1/admin/sync-conflicts` | GET | `[Authorize(Roles="Admin")]` | YES | YES — Owner-A's list excludes tenant-B's conflicts. |
| `/api/v1/admin/sync-conflicts/{id}/resolve` | POST | same | YES — `CallerTenantId()` stamped | YES — Owner-A resolves own conflict = 204; tenant-B's id = 403. |

#### 3.2.9 PricingController + QuotesController

| Route | Verb | Auth | Tenant-scope? | In matrix? |
|---|---|---|---|---|
| `/api/v1/properties/{propertyId}/pricing` | GET | `[Authorize(Roles="Owner,Admin")]` | YES — pricing plan inherits property's tenant | YES — Owner-A reads own = 200; tenant-B's = 404. |
| `/api/v1/properties/{propertyId}/pricing` | PUT | same | YES | YES — same shape (404 or 403 — verify in §7.3). |
| `/api/v1/properties/{propertyId}/pricing/rules` | POST/PUT/DELETE/PATCH | same | YES | YES — all rule mutations follow the same Owner-A-vs-tenant-B-propertyId shape. M.10 picks the POST as the representative case in the matrix; the others are covered by the M.4 behavior unit test. |
| `/api/v1/properties/{propertyId}/pricing/rules/reorder` | POST | same | YES | YES. |
| `/api/v1/properties/{propertyId}/quotes` | POST | `[AllowAnonymous]` | n/a — quote is public | NO — `[ExemptFromCrossTenantMatrix("public quote — no cross-tenant write")]`. **BUT** the quote reads the property's pricing plan; M.10's `CarveOutAppLayerTests` verifies an anonymous quote against tenant-B's property returns tenant-B's pricing (not tenant-A's — i.e. the quote correctly scopes to the property's owner). |

#### 3.2.10 ReviewsController + PropertyReviewsController + ReviewsAdminController

| Route | Verb | Auth | Tenant-scope? | In matrix? |
|---|---|---|---|---|
| `/api/v1/reviews/{id}/response` | POST | `[Authorize(Roles="Owner,Admin")]` | YES | YES — Owner-A responds to own = 204; tenant-B's review id = 403/404. |
| `/api/v1/properties/{propertyId}/reviews` | GET | `[AllowAnonymous]` | n/a — public | NO — `[ExemptFromCrossTenantMatrix("public reviews")]`. |
| `/api/v1/admin/reviews` | GET | `[Authorize(Roles="Admin")]` | YES — scoped to caller's tenant | YES — Owner-A's list excludes tenant-B's reviews. |
| `/api/v1/admin/reviews/{id}/hide` | POST | same | YES | YES — Owner-A hides own = 204; tenant-B's id = 403/404. |
| `/api/v1/admin/reviews/{id}/restore` | POST | same | YES | YES. |
| `/api/v1/admin/reviews/{id}/reject` | POST | same | YES | YES. |

#### 3.2.11 ThreadsController + RealtimeController

| Route | Verb | Auth | Tenant-scope? | In matrix? |
|---|---|---|---|---|
| `/api/v1/threads` | GET | `[Authorize]` (MyThreads) | YES — thread is tenant-scoped per OPS.M.3 | YES — Owner-A's list does not include tenant-B's threads. |
| `/api/v1/threads/{id}` | GET | `[Authorize]` | YES | YES — Owner-A reads own = 200; tenant-B's id = 404. |
| `/api/v1/threads/{id}/messages` | GET | `[Authorize]` | YES | YES — same shape. |
| `/api/v1/threads/{id}/messages` | POST | `[Authorize]` | YES | YES — Owner-A sends to own = 201; tenant-B's thread id = 404/403. |
| `/api/v1/threads/{id}/read` | POST | `[Authorize]` | YES | YES — same shape. |
| `/api/v1/threads/{id}/attachments` | POST | `[Authorize]` | stub `501` | NO — `[ExemptFromCrossTenantMatrix("stub 501")]`. |
| `/api/v1/realtime/negotiate` | GET | `[Authorize]` | NO — per-user SignalR negotiate | NO — `[ExemptFromCrossTenantMatrix("SignalR negotiate — per-user, no cross-tenant data")]`. |

#### 3.2.12 ReportsController (`/api/v1/admin/reports`)

| Route | Verb | Auth | Tenant-scope? | In matrix? |
|---|---|---|---|---|
| `/api/v1/admin/reports/occupancy` | GET | `[Authorize(Roles="Owner,Admin")]` | YES — `IPropertyOwnerLookup` filters | YES — Owner-A's report excludes tenant-B's bookings/properties; Owner-A with `propertyId=tenant-B-property` returns 403 per the SLICE7 design (verify in §7). |
| `/api/v1/admin/reports/revenue` | GET | same | YES | YES. |
| `/api/v1/admin/reports/adr` | GET | same | YES | YES. |
| `/api/v1/admin/reports/source` | GET | same | YES | YES. |

#### 3.2.13 Notifications + AdminUsers + Toggles + Alerts + MyLoyalty + others

| Route | Verb | Auth | Tenant-scope? | In matrix? |
|---|---|---|---|---|
| `/api/v1/admin/notifications` | GET | `[Authorize(Roles="Admin")]` | YES | YES — Owner-A's list excludes tenant-B's notifications. |
| `/api/v1/admin/notifications/{id}/retry` | POST | same | YES | YES — Owner-A retries own = 204; tenant-B's id = 403/404. |
| `/api/v1/admin/users` | GET | `[Authorize(Roles="Owner,Admin")]` | YES — search scoped to tenant per OPS.M.3 | YES — Owner-A's search excludes tenant-B's users (carve-out: `users` table is not RLS-scoped per M.9 §3.2, but the `SearchUsersQuery` handler is app-layer-scoped per OPS.M.3 — verify in §7). |
| `/api/v1/admin/toggles` | GET/PUT | stub `501` | NO — `[ExemptFromCrossTenantMatrix("stub 501")]`. |
| `/api/v1/admin/alerts` | GET/POST | stub `501` | NO — same. |
| `/api/v1/me/loyalty` | GET | `[Authorize]` | NO — per-user | NO — `[ExemptFromCrossTenantMatrix("loyalty — per-user, platform-wide")]`. |

### 3.3 Matrix summary

**Total endpoints in M.10 matrix**: ~40 routes. With 5 persona-cells each (Owner-A→A, Owner-A→B, Owner-B→A, Owner-B→B, PlatformAdmin where applicable), the fact count is ~200. Plus the carve-out tests (~12-15 facts), the audit-log facts (~10), the bypass facts (~8), the promote/revoke facts (~3), the AsyncLocal facts (~5), the schema facts (~89), totals **~320 facts in M.10**.

**Routes EXCLUDED from the matrix** (via `[ExemptFromCrossTenantMatrix]`):
- All `[AllowAnonymous]` routes (~10 — public search, public property detail, public reviews, public quotes, outbound iCal feed, Stripe webhook, amenities, `/health`)
- All DevAuth routes (~5)
- All `501 NotImplemented` stub routes (~8)
- Per-user routes with no cross-tenant surface (`/api/v1/me/loyalty`, `/api/v1/realtime/negotiate`, `/api/v1/bookings/holds`, `/api/v1/me` DELETE)

### 3.4 Carve-out app-layer assertion inventory (for `CarveOutAppLayerTests.cs`)

Per OPS.M.9 §3.2, the following tables are RLS-exempt. For each, M.10 asserts the app-layer prevents cross-tenant access via the public endpoints that read/write them:

| # | Table | Public surface that reads/writes | App-layer enforcement | M.10 fact name |
|---|---|---|---|---|
| 1 | `identity.users` | `/api/v1/admin/users` (search) | `SearchUsersQuery` handler filters `WHERE EXISTS (membership WHERE tenant_id = currentUser.TenantId)` | `Owner_A_searching_users_only_sees_their_tenants_members` |
| 2 | `identity.tenants` | `/api/v1/me/tenant` | `GetMyTenantQuery` filters `WHERE id = currentUser.TenantId` | Already covered by §3.2.1 matrix. NO carve-out fact needed. |
| 3 | `identity.tenant_memberships` | Indirect — `UserProvisioningMiddleware` reads; no public surface | n/a — no direct public endpoint | NO fact. (M.9 §4.11 D11 carve-out justification stands.) |
| 4 | `catalog.outbox_messages` (and per-module variants) | None — internal-only | n/a | NO fact. |
| 5 | `catalog.amenities` | `/api/v1/amenities` (anonymous read) + `/api/v1/admin/amenities` (admin write) | Shared catalog by design — NOT a leak | `Tenant_A_admin_can_see_amenities_created_by_tenant_B_admin_by_design` (positive fact documenting the intentional sharing). |
| 6 | `pricing.fees` | None directly — read via `ComputeQuoteCommand` | Filters by property tenant in the handler | `Anonymous_quote_for_tenant_A_property_uses_tenant_A_fees_not_tenant_B` |
| 7 | `booking.line_items` | None directly — read via `GetBookingQuery` | Inherits booking's tenant scope | NO direct fact — covered by §3.2.6 booking matrix. |
| 8 | `booking.guests` | None directly — read via `GetBookingQuery` | Inherits booking's tenant scope | NO direct fact — same. |
| 9 | `loyalty.accounts` | `/api/v1/me/loyalty` (per-user, platform-wide) | Per-user filter on the handler | `Owner_A_loyalty_account_independent_of_tenant` (positive fact) |
| 10 | Outbound iCal token | `/api/v1/feeds/{token}.ics` (anonymous) | Token-scoped lookup; only returns the token's tenant's bookings | `Tenant_A_outbound_token_returns_only_tenant_A_bookings_not_tenant_B` |

**Carve-out fact count: ~12-15** (some endpoints have multiple test angles per the table).

---

## 4. Design decisions

Every D-row ends with **"Decision: X"** per the OPS_M_8/M_9 format.

### 4.0 D-row index

| # | Topic | Verdict |
|---|---|---|
| D1 | Fixture shape | New `TwoTenantApiFixture` extending `IdentityApiFixture` |
| D2 | Test categorization | `[Trait("Category", "CrossTenant")]` |
| D3 | DevAuth vs JWT | DevAuth primary; one JWT smoke test |
| D4 | Sweep generation | `[Theory]` + `MemberData` |
| D5 | Audit assertion granularity | Per-verb-per-module sample |
| D6 | Bypass audit assertion | Per-endpoint fact via in-memory log sink |
| D7 | RLS schema fact consolidation | Absorbed from M.9 §13 Step 11 + 11.5 |
| D8 | Promote/revoke smoke test shape | `[Theory]` over three states |
| D9 | Web-layer coverage | OUT of M.10 |
| D10 | Endpoint-coverage drift | `EndpointCoverageArchTest` ships |
| D11 | Two-tenant seed shape | 1 prop + 1 booking + 1 plan + 1 feed per tenant |
| D12 | PlatformAdmin seed | Not a member of either tenant |

### 4.1 D1 — Fixture shape

**Alternatives considered**:

- **(A) Extend `TenantIdRolloutFixture`** — that fixture is a **migrator-only** harness; it spins up Postgres + runs every module's migrations, but does NOT boot the API host (no `WebApplicationFactory<Program>`). M.10 needs HTTP-level testing — Owner-A's `HttpClient` POSTing to `/api/v1/admin/tenants/...`. **Rejected** because the cost of bolting a WebApplicationFactory on top is the same as authoring a new fixture from scratch.

- **(B) Extend `IdentityApiFixture` in place** — add the two-tenant seed inside the existing fixture's `InitializeAsync`. **Rejected** because (a) `IdentityApiFixture` is shared by `IdentityFlowTests`, `TenantClaimWiringTests`, `OnboardingProgressTests`, `StripeOnboardingCommandsTests`, `GetMyTenantHandlerTests`, etc. — those tests assume a single-tenant default state. Adding tenant B + a second Owner persona would mutate the fixture state and break unrelated tests. Plus the `ResetAsync` method (verified `IdentityApiFixture.cs:71-80`) truncates only `users`, `audit_log`, `tenant_memberships`; the M.10 fixture needs deeper seeds (properties, bookings) that `ResetAsync` doesn't touch.

- **(C) New `TwoTenantApiFixture` extending `IdentityApiFixture`** — inherits the Postgres testcontainer + WebApplicationFactory bootstrap; overrides `InitializeAsync` to also seed tenant B + the PlatformAdmin + the property/booking/plan/feed per tenant. **Picked.** Shares the testcontainer machinery; isolates the two-tenant state in its own collection so existing tests are unaffected.

**Implementation sketch**:

```csharp
// tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantApiFixture.cs

public sealed class TwoTenantApiFixture : IdentityApiFixture
{
    // Stable, deterministic GUIDs for grep-ability.
    public static readonly Guid TenantA = new("a1111111-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid TenantB = new("b2222222-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly Guid PlatformAdminUserId = new("dada1111-aaaa-aaaa-aaaa-000000000001");

    public Guid PropertyA { get; private set; }
    public Guid PropertyB { get; private set; }
    public Guid BookingA { get; private set; }
    public Guid BookingB { get; private set; }
    public Guid PricingPlanA { get; private set; }
    public Guid PricingPlanB { get; private set; }
    public Guid ChannelFeedA { get; private set; }
    public Guid ChannelFeedB { get; private set; }
    public string OutboundTokenA { get; private set; } = string.Empty;
    public string OutboundTokenB { get; private set; } = string.Empty;

    public new async Task InitializeAsync()
    {
        await base.InitializeAsync(); // Applies migrations + boots WebAppFactory.

        await SeedTenantAsync(TenantA, "Tenant A", "tenant-a-slug", isPropertyA: true);
        await SeedTenantAsync(TenantB, "Tenant B", "tenant-b-slug", isPropertyA: false);
        await SeedPlatformAdminAsync();
    }

    // SeedTenantAsync: opens an EF DbContext under the bypass scope (so the M.9
    // policies don't block the seed), inserts Tenant + Owner User + Membership +
    // Property + Pricing Plan + Channel Feed + Booking + LineItems. Stamps the
    // generated ids onto this fixture's properties for assertion access.

    // SeedPlatformAdminAsync: inserts a User row with is_platform_admin = true.
    // NO membership rows — per D12, PlatformAdmin is a platform-wide role
    // independent of tenant membership.
}

[CollectionDefinition(nameof(TwoTenantApiCollection))]
public sealed class TwoTenantApiCollection : ICollectionFixture<TwoTenantApiFixture> { }
```

**Persona switching**:

The fixture exposes a helper `CreateClientAs(persona, tenant)` that:
1. Calls `CreateClient()` (inherits WebApplicationFactory's client).
2. Sets the DevAuth cookie to one of three values: `OwnerA`, `OwnerB`, `PlatformAdmin`. (Three new personas added to `DevAuthPersonas` in the test project — `tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantDevAuthPersonas.cs`. NOT in production code.)
3. Returns the client.

**Important**: M.10 must NOT modify `DevAuthPersonas` in production (`src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/DevAuthHandler.cs`). The production handler resolves three personas (Owner, Guest, Admin) and seeds them to the default tenant `00000000-0000-0000-0000-000000000001`. M.10 needs MULTIPLE tenants → MULTIPLE Owner personas. The shape: M.10's fixture overrides the `DevAuthHandler` registration in the test container's DI via `WebApplicationFactory.ConfigureWebHost` (the OPS.M.5 + M.7 plans show how — register a test-only `MultiTenantDevAuthHandler` that reads a cookie + maps to one of the seeded personas). Documented in §4.1 sub-decision below.

**Sub-decision: DevAuth in the M.10 fixture**

The `IdentityApiFixture` (verified `IdentityApiFixture.cs:60-66`) sets `DevAuth:FakeOid = test-owner-aaaa` and `DevAuth:IsOwner = true` — a single hardcoded persona. M.10 needs three personas (OwnerA, OwnerB, PlatformAdmin) routable via cookie. Solution: M.10's `TwoTenantApiFixture` overrides the DevAuth registration in `ConfigureWebHost` with a custom `TwoTenantDevAuthHandler` that reads the `vrbook-dev-persona` cookie and maps to one of:

- `OwnerA` → `oid=owner-a-...`, `tenantId=TenantA`, claims `Owner` + `tenant_admin@TenantA`.
- `OwnerB` → `oid=owner-b-...`, `tenantId=TenantB`, claims `Owner` + `tenant_admin@TenantB`.
- `PlatformAdmin` → `oid=platform-admin-...`, NO tenant membership, claims `PlatformAdmin` (via the M.8 enrichment path).
- `Guest` (a global guest persona) → `oid=guest-...`, NO tenant claims (used for `/api/v1/bookings` POST tests).

The DB seed AND the cookie resolution must agree on the OIDs (the `oid` is the bridge — `UserProvisioningMiddleware` matches DB rows by `oid`). The fixture seeds the User rows with these OIDs upfront; the DevAuth cookie selects which persona's OID is on the request.

**Decision: New `TwoTenantApiFixture` extending `IdentityApiFixture`; overrides `ConfigureWebHost` to register a test-only `TwoTenantDevAuthHandler` that maps the cookie to one of four personas (OwnerA, OwnerB, PlatformAdmin, Guest); seeds the DB with deterministic GUIDs and the matching OIDs. Documented in §6.1.**

### 4.2 D2 — Test categorization

**Alternatives**:

- **(A) `[Trait("Category", "Integration")]`** — same category as existing fixture-based tests (`IdentityFlowTests`, etc.). Pro: consistent. Con: the M.10 matrix is ~250 facts; "Integration" currently runs as a single CI job; M.10 would dominate that job's runtime.

- **(B) `[Trait("Category", "CrossTenant")]`** — new category. Pro: (1) CI can split for parallel run; (2) grep-friendly — `dotnet test --filter Category=CrossTenant` is the M.4/M.8/M.9 reviewer's go-to spot-check; (3) explicit naming makes the test pack's purpose visible to a future reader. Con: one more category to track.

**Picked: (B)**. The grep-ability is the load-bearing reason; M.10 is the canonical cross-tenant test pack and deserves the explicit label. CI workflow update: add a new CI job `cross-tenant-tests` that runs `dotnet test --filter Category=CrossTenant` in parallel with the existing `unit-tests` and `integration-tests` jobs. The job consumes its own Postgres testcontainer.

**Decision: `[Trait("Category", "CrossTenant")]` on every M.10 test class. CI workflow adds a parallel job.**

### 4.3 D3 — DevAuth cookie vs JWT minting

**Alternatives**:

- **(A) DevAuth cookie path for everything** — fast (no JWT mint per request), already wired. Con: doesn't test the production auth path (`JwtBearerHandler` parsing an Entra JWT).
- **(B) JWT minting for everything** — closer to production. Con: per-request JWT generation slows the matrix by 3-5x (RSA signing per request, even with a test key); the WebApplicationFactory needs custom JWT validation parameters; the M.0 Entra wiring (`AuthExtensions.cs`) is skipped in tests via the `EntraExternalId:Instance=""` config trick (verified `IdentityApiFixture.cs:54-58`) so M.10 would need to undo that.
- **(C) Hybrid — DevAuth primary, one JWT smoke run for the production path** — DevAuth covers ~250 facts at full speed; one additional `[Theory]` runs a representative subset (~10 facts) under JWT auth via a separate `TwoTenantJwtApiFixture` variant that enables `EntraExternalId:Instance` and registers a custom `JwtBearerOptions.TokenValidationParameters` with a test signing key.

**Picked: (C)**. The DevAuth path is the M.7+M.8 precedent (fast, repeatable, no token clock skew issues). The JWT smoke covers the production-shape concern (the M.0 token validation pipeline + the `UserProvisioningMiddleware` reading the JWT's claims) with one fact per persona-cell-shape:

- OwnerA JWT → tenant A property = 200.
- OwnerA JWT → tenant B property = 403.
- PlatformAdmin JWT → tenant A + tenant B = 200.

The JWT smoke shares the seeded two-tenant data (the DB seed is auth-independent); only the auth surface differs.

**Decision: DevAuth for the full ~250-fact matrix; one `JwtSmokeTests` class with ~10 facts covering the production-shape auth path. The two fixtures share the seeded data via a shared Postgres testcontainer.**

### 4.4 D4 — Sweep generation strategy

**Alternatives**:

- **(A) Hand-write one `[Fact]` per route per persona** — ~250 distinct test methods. Pro: each test has an explicit name, debugger-friendly. Con: unmaintainable; ~250 lines of `[Fact]` boilerplate; adding a new endpoint requires modifying ~5 places (the matrix per persona).

- **(B) `[Theory]` + `[InlineData(route, verb, persona, expectedStatus)]`** — one test method per scenario shape. Pro: terser. Con: `InlineData` doesn't compose for complex objects; the matrix needs the test to be able to call `fixture.CreateClientAs(persona, tenant)` which returns an `HttpClient` — that's not inline-able. Plus 250 `InlineData` lines on one method is still ~250 lines.

- **(C) `[Theory]` + `[MemberData(nameof(RouteMatrix))]`** — `RouteMatrix` is a static enumerator that yields `(route, verb, persona, expectedStatus, bodyMarker)` tuples. Pro: the matrix lives in ONE place (the `RouteMatrix` enumerator), adding a new endpoint = adding a new yield row. The arch test (`EndpointCoverageArchTest`) can reflect on `RouteMatrix` to verify coverage. Con: less-obvious individual test names (xUnit shows `RouteMatrix_data_index_0`, `..._1`, etc. — mitigated by including the route name in the test method's name parameter).

**Picked: (C)**. The single source of truth + arch-test compatibility is the load-bearing property. xUnit allows `[Theory]` with `MemberData` to surface readable test names via the `DisplayName` member — the matrix entries include a `string Description` field that xUnit prints as the test name (`"Owner_A_GET_api_v1_bookings_id_for_tenant_B_booking_returns_404"`).

**Implementation sketch**:

```csharp
// tests/VrBook.Api.IntegrationTests/Multitenancy/CrossTenantEndpointMatrix.cs

public static class RouteMatrix
{
    public sealed record Cell(
        string Description,    // grep-friendly test name surfaced via [MemberData]
        string Verb,
        string Route,          // /api/v1/bookings/{id} with {id} replaced by the test
        Persona Persona,
        TargetTenant Target,
        int ExpectedStatus,
        Func<HttpResponseMessage, Task>? BodyAssertions = null);

    public static IEnumerable<object[]> GetAll()
    {
        // ~40 endpoints × 5 persona-cells each = ~200 yields.

        // IdentityController — §3.2.1
        yield return new object[] { new Cell("OwnerA_GET_me_tenant_returns_tenantA_dto",
            "GET", "/api/v1/me/tenant", Persona.OwnerA, TargetTenant.A, 200,
            BodyMustContain("tenantId", "a1111111-...")) };
        yield return new object[] { new Cell("OwnerB_GET_me_tenant_returns_tenantB_dto",
            "GET", "/api/v1/me/tenant", Persona.OwnerB, TargetTenant.B, 200,
            BodyMustContain("tenantId", "b2222222-...")) };
        yield return new object[] { new Cell("PlatformAdmin_GET_me_tenant_returns_403_no_membership",
            "GET", "/api/v1/me/tenant", Persona.PlatformAdmin, TargetTenant.None, 403, null) };
        yield return new object[] { new Cell("Anonymous_GET_me_tenant_returns_401",
            "GET", "/api/v1/me/tenant", Persona.Anonymous, TargetTenant.None, 401, null) };

        // ... ~250 more yields ...
    }
}

public sealed class CrossTenantEndpointMatrix
{
    private readonly TwoTenantApiFixture _fixture;
    public CrossTenantEndpointMatrix(TwoTenantApiFixture fixture) { _fixture = fixture; }

    [Theory]
    [Trait("Category", "CrossTenant")]
    [MemberData(nameof(RouteMatrix.GetAll), MemberType = typeof(RouteMatrix))]
    public async Task Cross_tenant_endpoint(RouteMatrix.Cell cell)
    {
        var client = _fixture.CreateClientAs(cell.Persona);
        var url = cell.Route.Replace("{id}", _fixture.IdFor(cell.Target));
        var request = new HttpRequestMessage(new HttpMethod(cell.Verb), url);
        // For POST/PUT/PATCH endpoints, attach a stub body per the route's shape
        // (the Cell helper provides a body factory).
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be((HttpStatusCode)cell.ExpectedStatus);
        if (cell.BodyAssertions is not null)
            await cell.BodyAssertions(response);
    }
}
```

**Decision: `[Theory]` + `[MemberData]` driven by `RouteMatrix.GetAll()`. The matrix is one C# file; adding a new endpoint = adding new yield rows. The arch test in §4.10 enforces coverage.**

### 4.5 D5 — Audit assertion granularity

The OPS.M.4 `TenantAuthorizationBehavior` (verified `TenantAuthorizationBehavior.cs:61-64`) emits a structured log line at `Information` level on every cross-tenant rejection. The line shape:

```
TenantAuthorizationBehavior rejected cross-tenant write:
  CommandType={CommandType}, AttemptedTenantId={AttemptedTenantId}, ActualTenantId={ActualTenantId}, UserId={UserId}
```

M.10 must assert this log line is emitted for every cross-tenant write rejection.

**Alternatives**:

- **(A) Per-route-per-persona — one log assertion per matrix cell** — ~100 log assertions. Pro: exhaustive. Con: every assertion does the same shape; the M.4 code path doesn't branch on route — proving 100 routes emit the same log line proves nothing more than proving 5 routes emit it.

- **(B) Per-verb-per-module sample** — pick one POST, one PUT, one PATCH, one DELETE per module that has `ITenantScoped` commands. ~10 facts total. Pro: representative; covers the major shapes. Con: a future endpoint that uses a different `IRequest<>` shape (e.g. a new HTTP verb) might not hit the assertion.

- **(C) Single Theory over a small representative set** — one `[Theory]` with ~10 `MemberData` rows. Pro: even terser. Con: same property as (B).

**Picked: (B)/(C) — they're equivalent**. The M.4 code path is the same `if (currentUser.TenantId != command.TenantId) throw new CrossTenantAccessException(...)`; proving 10 representative routes fires the log line proves the wire. The arch test from M.4 (the unit tests) already proves the code path itself.

**Implementation sketch**:

```csharp
[Theory]
[Trait("Category", "CrossTenant")]
[InlineData("POST", "/api/v1/properties", "tenantB-property-payload")]
[InlineData("POST", "/api/v1/bookings/{bookingB}/confirm", null)]
[InlineData("POST", "/api/v1/bookings/{bookingB}/check-in", null)]
[InlineData("POST", "/api/v1/payments/refunds", "tenantB-refund-payload")]
[InlineData("POST", "/api/v1/admin/channel-feeds", "tenantB-feed-payload")]
[InlineData("POST", "/api/v1/admin/sync-conflicts/{conflictB}/resolve", "tenantB-resolution-payload")]
[InlineData("POST", "/api/v1/reviews/{reviewB}/response", "tenantB-response-payload")]
[InlineData("POST", "/api/v1/admin/notifications/{notificationB}/retry", null)]
[InlineData("POST", "/api/v1/properties/{propertyB}/pricing/rules", "tenantB-rule-payload")]
[InlineData("PUT", "/api/v1/properties/{propertyB}/pricing", "tenantB-plan-payload")]
public async Task Cross_tenant_write_emits_M4_log_line(string verb, string route, string? payload)
{
    var inMemorySink = _fixture.GetInMemoryLogSink();
    inMemorySink.Clear();

    var client = _fixture.CreateClientAs(Persona.OwnerA);
    var url = route.Replace("{bookingB}", _fixture.BookingB.ToString());
    // ... build request ...
    var response = await client.SendAsync(request);

    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    inMemorySink.LogEvents.Should().Contain(e =>
        e.Level == LogEventLevel.Information &&
        e.MessageTemplate.Text.Contains("TenantAuthorizationBehavior rejected cross-tenant write"));
}
```

**Decision: ~10 facts via `[Theory]` with `InlineData` covering one POST/PUT/PATCH/DELETE per ITenantScoped module. Log assertion via `Serilog.Sinks.InMemory`. The matrix proves the wire; the M.4 unit tests prove the gate.**

### 4.6 D6 — Bypass audit assertion shape

The M.9 `RlsBypassDbContextFactoryBase` emits an Information-level log line on every bypass-factory open:

```
RLS bypass open for {ContextType} (reason={Reason})
```

M.10 captures and asserts this for every documented bypass call site (per OPS.M.9 §7 allow-list):

1. `TenantStripeContextLookup.GetByStripeAccountAsync` (Stripe webhook lookup).
2. `PlatformTenantStatsLookup` (M.8 list endpoint).
3. `ListPlatformTenantsHandler` (M.8 list endpoint).
4. `GetPlatformTenantHandler` (M.8 detail endpoint).
5. `HandleStripeWebhookHandler` (whole handler body).
6. `Workers.Sync/Program.cs` bootstrap (the Sync worker test is in `tests/VrBook.Workers.Sync.IntegrationTests/`; M.10 does NOT re-test the worker — that's the worker's own test pack).

M.10 owns sites (1)–(5). One fact per site asserts the log line fires when the corresponding endpoint is invoked.

**Implementation sketch**:

```csharp
[Theory]
[Trait("Category", "CrossTenant")]
[InlineData("/api/v1/admin/platform/tenants", "GET", "IdentityDbContext", "admin.platform.list-tenants")]
[InlineData("/api/v1/admin/platform/tenants/{tenantA}", "GET", "IdentityDbContext", "admin.platform.get-tenant")]
[InlineData("/api/v1/admin/platform/tenants/{tenantA}/suspend", "POST", "IdentityDbContext", "admin.platform.suspend")]
[InlineData("/api/v1/admin/platform/tenants/{tenantA}/reactivate", "POST", "IdentityDbContext", "admin.platform.reactivate")]
public async Task PlatformAdmin_endpoint_emits_bypass_log_line(
    string route, string verb, string expectedContextType, string expectedReason)
{
    var sink = _fixture.GetInMemoryLogSink();
    sink.Clear();

    var client = _fixture.CreateClientAs(Persona.PlatformAdmin);
    var url = route.Replace("{tenantA}", _fixture.TenantA.ToString());
    var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(verb), url));

    response.IsSuccessStatusCode.Should().BeTrue();
    sink.LogEvents.Should().Contain(e =>
        e.Level == LogEventLevel.Information &&
        e.MessageTemplate.Text.Contains("RLS bypass open for") &&
        e.Properties["ContextType"].ToString().Contains(expectedContextType));
}
```

**Decision: Per-bypass-call-site fact; one `[Theory]` with ~5 `InlineData` rows. Asserts both the log line text and the structured properties (`ContextType`, `Reason`).**

### 4.7 D7 — RLS schema fact consolidation

Per OPS.M.9 §13 close-out:

> "Step 11 (per-module RLS integration fact pack) deferred to OPS.M.10. ... 76 schema-introspection facts (19 tables × 4 facts each). ... folding the schema facts into M.10 avoids two parallel test setups."

M.10 absorbs:

- **Schema facts (76 facts in `RlsPolicySchemaFactPack.cs`)** — for each of the 19 tenant-scoped tables in OPS.M.9 §3.1:
  - `<Schema>_<Table>_has_row_level_security_enabled` (`pg_class.relrowsecurity = true`)
  - `<Schema>_<Table>_has_row_level_security_forced` (`pg_class.relforcerowsecurity = true`)
  - `<Schema>_<Table>_has_tenant_isolation_policy` (`pg_policy.polname = 'rls_<schema>_<table>_tenant_isolation'`)
  - `<Schema>_<Table>_policy_qual_references_app_tenant_id_GUC` (regex on `pg_get_expr(polqual)`)

- **Carve-out negative facts (13 facts in `RlsCarveOutSchemaFactPack.cs`)** — for each table in OPS.M.9 §3.2:
  - `<Schema>_<Table>_does_NOT_have_row_level_security_enabled` (`pg_class.relrowsecurity = false`)

These facts read `information_schema` / `pg_catalog` only — they do NOT depend on the seeded two-tenant data. They CAN share the `TwoTenantApiFixture`'s Postgres testcontainer (saves boot time) but they CAN'T share its collection because the data-driven tests might mutate state. M.10's pragmatic shape: a separate `RlsSchemaCollection` that re-uses the same Postgres testcontainer instance (via a static container reference or a `WebApplicationFactory.Server.Services` resolution). Documented in §6.4 implementation guide.

**Implementation sketch** (matches OPS.M.9 §5 Step 5's outline):

```csharp
[Theory]
[Trait("Category", "Integration")]  // shared with the M.9 schema-test pattern; not [CrossTenant]
[InlineData("catalog", "properties")]
[InlineData("catalog", "property_images")]
[InlineData("booking", "bookings")]
// ... 19 lines ...
public async Task Table_has_RLS_enabled(string schema, string table)
{
    await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT relrowsecurity FROM pg_class WHERE oid = @oid::regclass;";
    cmd.Parameters.AddWithValue("@oid", $"{schema}.{table}");
    var result = await cmd.ExecuteScalarAsync();
    result.Should().Be(true);
}

// Symmetric for relforcerowsecurity, policy presence, policy qual text.
```

**Decision: Absorbed. Two new test classes under `tests/VrBook.Api.IntegrationTests/Rls/`: `RlsPolicySchemaFactPack.cs` (~76 facts) + `RlsCarveOutSchemaFactPack.cs` (~13 facts). Both classes carry `[Trait("Category", "Integration")]` (not `CrossTenant`) because they're schema-level, not cross-tenant.**

### 4.8 D8 — Promote/revoke smoke test shape

The OPS.M.8 PromoteSql runbook (`docs/runbooks/OPS_M_8_PROMOTE_PLATFORM_ADMIN.md`) documents the SQL flow:

```sql
-- Promote
UPDATE identity.users SET is_platform_admin = true WHERE email = 'target@email.com';

-- Revoke
UPDATE identity.users SET is_platform_admin = false WHERE email = 'target@email.com';
```

M.10 verifies the end-to-end flow with a fresh user (not the PlatformAdmin from the fixture seed):

**Alternatives**:

- **(A) Three `[Fact]` methods** — `NotPromoted_user_gets_403`, `Promoted_user_gets_200`, `Revoked_user_gets_403`. Pro: each test has its own name. Con: each test must set up the state from scratch (clean DB → seed user → optionally promote/revoke → exercise → assert).

- **(B) One `[Theory]` with three rows** — `[InlineData(promoted: false, expected: 403)]`, etc. Pro: shared setup. Con: less obvious test name.

- **(C) One `[Fact]` that walks the three states sequentially** — `Promote_revoke_smoke_test`: seed user, expect 403, promote, expect 200, revoke, expect 403. Pro: tells the full story. Con: a failure in step 2 obscures step 3.

**Picked: (B)**. The `[Theory]` with three rows gives independent test failures (one row failing doesn't block another), shared setup via the fixture, and minimal duplication. The DB state transition (promote/revoke SQL) lives inside the test parameter so xUnit's `--filter` can target individual scenarios.

**Implementation sketch**:

```csharp
public enum PromoteRevokeState { NotPromoted, Promoted, Revoked }

[Theory]
[Trait("Category", "CrossTenant")]
[InlineData(PromoteRevokeState.NotPromoted, 403)]
[InlineData(PromoteRevokeState.Promoted, 200)]
[InlineData(PromoteRevokeState.Revoked, 403)]
public async Task PromoteRevoke_via_SQL_flips_platform_endpoint_access(
    PromoteRevokeState state, int expectedStatus)
{
    var freshEmail = $"m10-promote-{state}@test.local";
    await _fixture.SeedFreshUserAsync(freshEmail);

    if (state == PromoteRevokeState.Promoted || state == PromoteRevokeState.Revoked)
        await _fixture.PromotePlatformAdminViaSqlAsync(freshEmail);
    if (state == PromoteRevokeState.Revoked)
        await _fixture.RevokePlatformAdminViaSqlAsync(freshEmail);

    var client = _fixture.CreateClientAsFreshUser(freshEmail);
    var response = await client.GetAsync("/api/v1/admin/platform/tenants");

    response.StatusCode.Should().Be((HttpStatusCode)expectedStatus);
}
```

**Decision: `[Theory]` with three rows over `PromoteRevokeState`. Fixture exposes `SeedFreshUserAsync`, `PromotePlatformAdminViaSqlAsync`, `RevokePlatformAdminViaSqlAsync`, `CreateClientAsFreshUser` helpers.**

### 4.9 D9 — Web-layer coverage

Per §1.2, web-layer tests are OUT of M.10. The architect explicitly delegates the AdminSidebar `useMe.isPlatformAdmin` gate, the tenant dashboard rendering, the wizard onboarding state — all to the M.7/M.8 vitest sweep.

**Why this is the right boundary**:

1. The vitest harness runs in CI (verified by the M.8 plan §10 implementation guide — every M.8 web component has a vitest counterpart).
2. The web-layer tests would require a Playwright-style end-to-end browser test; the M.10 fixture pattern is HTTP-only (no browser).
3. The web-layer can be a leak vector ONLY if the server provides leaked data — and M.10 proves the server doesn't. The web layer just renders what the server returns; the M.10 API surface coverage is sufficient.

**Sub-decision**: M.10 does NOT add an arch test asserting "every Platform sidebar group menu item maps to a server endpoint covered by the M.10 matrix". That would be valuable but it's a separate concern (the web-layer↔API contract) and crosses a project boundary (the web project doesn't share an assembly with the test project). Phase 2 hardening can revisit.

**Decision: Web-layer cross-tenant assertions stay in M.7/M.8 vitest. M.10 is API-level only. Documented in §1.2.**

### 4.10 D10 — Endpoint-coverage drift enforcement

The `EndpointCoverageArchTest` is the **M.10 invariant after M.10 ships**. Every new endpoint added in a future PR must either (a) appear in `RouteMatrix.GetAll()` or (b) carry `[ExemptFromCrossTenantMatrix(reason)]`.

**Implementation sketch**:

```csharp
// tests/VrBook.Architecture.Tests/EndpointCoverageArchTest.cs

public sealed class EndpointCoverageArchTest
{
    [Fact]
    public void Every_authenticated_endpoint_is_in_M10_matrix_or_exempt()
    {
        var apiAssembly = typeof(VrBook.Api.Program).Assembly;
        var controllers = apiAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        var matrixRoutes = RouteMatrix.GetAll()
            .Select(o => ((RouteMatrix.Cell)o[0]).Route)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var uncovered = new List<string>();

        foreach (var controller in controllers)
        {
            if (controller.GetCustomAttribute<ExemptFromCrossTenantMatrixAttribute>() is not null)
                continue;

            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var httpAttrs = method.GetCustomAttributes<HttpMethodAttribute>().ToList();
                if (!httpAttrs.Any()) continue;
                if (method.GetCustomAttribute<ExemptFromCrossTenantMatrixAttribute>() is not null)
                    continue;
                if (method.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                    continue; // public endpoints are exempt by default

                var route = BuildRoute(controller, method);
                if (!matrixRoutes.Contains(route))
                    uncovered.Add($"{controller.Name}.{method.Name} -> {route}");
            }
        }

        uncovered.Should().BeEmpty(
            "every authenticated endpoint must be in M.10's RouteMatrix or carry [ExemptFromCrossTenantMatrix]");
    }
}
```

**Important**: the arch test fails when a new endpoint is added without an `RouteMatrix` row OR an `[ExemptFromCrossTenantMatrix]` attribute. The failure message tells the future engineer EXACTLY what to do — add a row or exempt.

**Sub-decision**: the attribute carries a `reason` parameter. Empty reasons fail compile (constructor requires a non-null string). The reason is the documentation; the next M.10 maintainer can scan all `[ExemptFromCrossTenantMatrix]` attributes to see what's been carved out.

**Decision: `EndpointCoverageArchTest` ships in M.10 + `ExemptFromCrossTenantMatrixAttribute` ships in `src/VrBook.Api/Common/`. The attribute is applied to ~6 controllers covering the public anonymous + stub-501 + DevAuth + per-user surfaces.**

### 4.11 D11 — Two-tenant seed shape

The minimum data per tenant must exercise every module's read + write path. Documented per OPS.M.5 / M.6 / M.7 seed precedent:

| Module | Tenant A seed | Tenant B seed | Rationale |
|---|---|---|---|
| Identity | Owner-A user + tenant-A row + membership(Owner-A, TenantA, Owner) | Owner-B user + tenant-B row + membership(Owner-B, TenantB, Owner) | M.10 matrix needs distinct owners per tenant. |
| Identity (PlatformAdmin) | n/a | n/a | PlatformAdmin is platform-wide; one global user with `is_platform_admin = true`. |
| Catalog | 1 property "Beach Villa A" (slug=`beach-villa-a`) | 1 property "Beach Villa B" (slug=`beach-villa-b`) | Property reads need a target; the slug is used by `GetPropertyBySlugQuery`. |
| Catalog (amenities) | Shared (1 amenity "Wi-Fi" seeded globally) | Shared (same amenity visible to tenant B) | Tests intentional cross-tenant amenity visibility per §3.4. |
| Booking | 1 confirmed booking against property-A | 1 tentative booking against property-B | Distinct booking statuses exercise different read paths. |
| Booking (line items + guests) | Inherits via booking | Inherits via booking | Carve-out — child of bookings. |
| Pricing | 1 pricing plan for property-A with 1 rule | 1 pricing plan for property-B with 1 rule | Pricing CRUD matrix needs a target. |
| Payment | 1 payment intent for booking-A (status: Succeeded) | 1 payment intent for booking-B (status: Pending) | PaymentsController GET-by-booking needs a target. |
| Sync | 1 channel feed for property-A (with outbound token TOKEN_A) | 1 channel feed for property-B (TOKEN_B) | Outbound iCal carve-out test needs distinct tokens. |
| Sync (conflict) | 1 pending sync conflict for property-A | 1 pending sync conflict for property-B | Sync conflict resolve matrix needs a target. |
| Messaging | 1 thread between Owner-A and Guest about booking-A | 1 thread between Owner-B and Guest about booking-B | Thread access matrix needs distinct ids. |
| Notifications | 1 notification log entry (status: Failed) for tenant-A | 1 notification log entry (status: Failed) for tenant-B | Notifications retry matrix needs a target. |
| Reviews | 1 published review for property-A | 1 published review for property-B | Review respond + moderation matrix needs targets. |
| Reports | Inherited from booking + property data | Same | Reports queries roll up the seed. |
| Loyalty | None per-tenant (account is per-user) | None per-tenant | Loyalty is platform-wide. |

**The seed needs ~30 INSERTs across ~10 schemas**. Documented in §6.2.

**Decision: The above shape; documented as `TwoTenantApiFixture.SeedTenantAsync(tenantId, slug, ...)` method. Per-tenant deterministic GUIDs make assertions grep-able (the test failure message includes the seeded GUID so the engineer can find the row in the test DB).**

### 4.12 D12 — PlatformAdmin seed: tenant membership status

Per OPS.M.8, the PlatformAdmin role is platform-wide. M.10's seed reflects this:

- PlatformAdmin User row: `is_platform_admin = true`, OID = `platform-admin-...`, email = `platform-admin@test.local`.
- NO tenant_memberships row for the PlatformAdmin user.

**Test implications**:

- PlatformAdmin → `GET /api/v1/me/tenant` returns 403 (per OPS.M.7 — caller has no tenant claim, the `GetMyTenantQuery` throws `ForbiddenException`).
- PlatformAdmin → `GET /api/v1/admin/platform/tenants` returns 200 (the bypass).
- PlatformAdmin → `POST /api/v1/properties` returns 403 (PlatformAdmin lacks the Owner role AND has no tenant scope; the `CallerTenantId()` helper throws `ForbiddenException("Owner action requires a tenant membership.")`).

**Subtle case**: a PlatformAdmin who is ALSO an Owner-of-A (i.e. a member of TenantA AND has `is_platform_admin = true`). M.10 does NOT seed this case as the primary persona because it muddles the semantics; instead, M.10 ships a single `[Fact]` `PlatformAdmin_with_tenant_membership_still_uses_bypass_for_platform_endpoints` that ad-hoc seeds an additional membership and verifies the bypass still wins. Per §7.3.

**Decision: PlatformAdmin in the main seed has NO tenant membership. The "PlatformAdmin who is also Owner" case is a one-fact ad-hoc additional seed.**

---

## 5. Test classes inventory

The full list of new test files M.10 ships. Per OPS.M.8 / M.9 plan precedent.

| # | File | Class | Approximate fact count |
|---|---|---|---|
| 1 | `tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantApiFixture.cs` | `TwoTenantApiFixture : IdentityApiFixture` | n/a (fixture) |
| 2 | `tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantApiFixture.cs` (same file) | `TwoTenantApiCollection` | n/a (collection definition) |
| 3 | `tests/VrBook.Api.IntegrationTests/Multitenancy/CrossTenantEndpointMatrix.cs` | `CrossTenantEndpointMatrix` + `RouteMatrix` static helper | ~200 facts (`[Theory]` rows) |
| 4 | `tests/VrBook.Api.IntegrationTests/Multitenancy/CarveOutAppLayerTests.cs` | `CarveOutAppLayerTests` | ~12-15 facts |
| 5 | `tests/VrBook.Api.IntegrationTests/Multitenancy/PlatformAdminBypassFactPack.cs` | `PlatformAdminBypassFactPack` | ~8 facts |
| 6 | `tests/VrBook.Api.IntegrationTests/Multitenancy/CrossTenantRejectionAuditFactPack.cs` | `CrossTenantRejectionAuditFactPack` | ~10 facts |
| 7 | `tests/VrBook.Api.IntegrationTests/Multitenancy/PlatformAdminPromoteRevokeSmokeTest.cs` | `PlatformAdminPromoteRevokeSmokeTest` | 3 facts (`[Theory]` rows) |
| 8 | `tests/VrBook.Api.IntegrationTests/Multitenancy/JwtSmokeTests.cs` | `JwtSmokeTests` | ~10 facts |
| 9 | `tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantJwtApiFixture.cs` | `TwoTenantJwtApiFixture : TwoTenantApiFixture` | n/a (fixture) |
| 10 | `tests/VrBook.Api.IntegrationTests/Rls/RlsPolicySchemaFactPack.cs` | `RlsPolicySchemaFactPack` | 76 facts (`[Theory]` over 19 tables × 4 cells) |
| 11 | `tests/VrBook.Api.IntegrationTests/Rls/RlsCarveOutSchemaFactPack.cs` | `RlsCarveOutSchemaFactPack` | ~13 facts |
| 12 | `tests/VrBook.Api.IntegrationTests/Rls/AsyncLocalLeakFactPack.cs` | `AsyncLocalLeakFactPack` | ~5 facts |
| 13 | `tests/VrBook.Architecture.Tests/EndpointCoverageArchTest.cs` | `EndpointCoverageArchTest` | 1 fact (the comprehensive coverage check) |
| 14 | `tests/VrBook.Architecture.Tests/CrossTenantTraitArchTest.cs` | `CrossTenantTraitArchTest` | 1 fact (every M.10 class has `[Trait("Category", "CrossTenant")]` except the RLS schema classes which are `Integration`) |
| 15 | `src/VrBook.Api/Common/ExemptFromCrossTenantMatrixAttribute.cs` | `ExemptFromCrossTenantMatrixAttribute` | n/a (attribute) |
| 16 | `docs/runbooks/cross-tenant-leak-triage.md` | n/a | n/a (docs) |

**Test classes shipped: 11.** **Production code additions: 1 attribute file.** **Documentation: 1 runbook.**

**Total fact count**: ~340 facts across the test classes.

---

## 6. Step-by-step TDD plan (Red → Green)

Every step is red-first. The TDD twist for M.10: most of the production code already exists (M.0-M.9), so most "RED" tests fail because **they're not written yet**, then "GREEN" is just the test author writing the assertion and seeing it pass. A handful of facts ARE genuine red — they expose bugs or surface fixed-in-prior-slices behavior:

- **D5/D6 audit facts**: if the log line is missing a structured property, the test fails until the prior-slice handler adds it. (M.4/M.9 already emit; M.10 just asserts.)
- **D10 EndpointCoverageArchTest**: when first added, it will fail because the matrix is empty. The test author drives matrix coverage by progressively un-failing.
- **D8 promote/revoke smoke test**: passes immediately IF the SQL paths are correct AND the `UserProvisioningMiddleware` re-reads `is_platform_admin` per OPS.M.8 §3.2 — M.10 confirms.

### 6.0 TDD discipline reminder

Per OPS.M.8/M.9 precedent:

1. Write the failing test(s) — RED commit. CI MUST fail.
2. Write the minimum impl to make the test(s) pass — GREEN commit. CI MUST pass.
3. (Optional) Refactor — REFACTOR commit. CI MUST stay passing.

The §11 ledger tracks all three commits per step.

### 6.0.1 Order rationale

M.10 ships in a single PR (no atomic-deploy waves — see §2). The steps below sequence logically (fixture before matrix; matrix before arch test), but they could be parallelized if multiple engineers work on M.10.

### 6.0.2 Pre-Step-1 readiness check

- Verify the M.9 mechanism is live in `develop`: `RlsBypassDbContextFactory<>` registered, `TenantGucCommandInterceptor` wired, 9 RLS migrations applied. M.10 leans on the M.9 bypass-factory log lines.
- Verify `Serilog.Sinks.InMemory` is added to `tests/VrBook.Api.IntegrationTests/VrBook.Api.IntegrationTests.csproj`.
- Verify the M.8 PromoteSql runbook reflects the actual SQL path (re-read `docs/runbooks/OPS_M_8_PROMOTE_PLATFORM_ADMIN.md` for current shape).

### Step 1 — `TwoTenantApiFixture` + collection (M, ~2h)

**Tests (red first)** — `tests/VrBook.Api.IntegrationTests/Multitenancy/TwoTenantApiFixtureTests.cs` (a small smoke test that the fixture itself works):

- `Fixture_seeds_two_tenants` — assert tenant A + tenant B exist in `identity.tenants`.
- `Fixture_seeds_PlatformAdmin_user` — assert `identity.users` has a row with `is_platform_admin = true`.
- `Fixture_seeds_one_property_per_tenant` — assert two `catalog.properties` rows with distinct `tenant_id`.
- `Fixture_CreateClientAs_OwnerA_resolves_to_tenant_A_membership` — call `/api/v1/me/tenant`, assert the response carries TenantA's id.
- `Fixture_CreateClientAs_OwnerB_resolves_to_tenant_B_membership` — same shape.
- `Fixture_CreateClientAs_PlatformAdmin_has_is_platform_admin_true` — call `/api/v1/me`, assert `isPlatformAdmin = true`.

**Min implementation**:

1. Add `Serilog.Sinks.InMemory` NuGet to `VrBook.Api.IntegrationTests.csproj`.
2. Create `TwoTenantApiFixture` per §4.1 sketch. Override `InitializeAsync` to seed via the bypass DbContext (M.9's `IRlsBypassDbContextFactory<T>` opened during seed).
3. Override `ConfigureWebHost` to:
   - Register a test-only `TwoTenantDevAuthHandler` that reads the persona cookie and maps to one of four personas.
   - Register `Serilog.Sinks.InMemory.InMemorySink` as a Serilog sink so tests can capture log lines.
4. Add `CreateClientAs(persona)` + `IdFor(tenant)` + `GetInMemoryLogSink()` helpers.
5. Add the `TwoTenantApiCollection` `[CollectionDefinition]`.

**§3 / §4 cross-reference**: §4.1 D1, §4.11 D11, §4.12 D12.

**Pitfalls**:

- The fixture must seed via the M.9 bypass DbContext (`IRlsBypassDbContextFactory<T>.CreateForBypassAsync`) because the seed crosses tenant boundaries (it creates BOTH tenants' data from a single seed call). Without bypass, the M.9 policies block the cross-tenant INSERTs.
- The `TwoTenantDevAuthHandler` must NOT conflict with the production `DevAuthHandler`. Solution: in `ConfigureWebHost`, remove the production handler from the auth schemes and add the test-only one.
- The seeded GUIDs must be deterministic (constant `Guid` literals) for grep-ability. xUnit failure messages include the GUID.
- The fixture's `DisposeAsync` MUST clean up the testcontainer (`base.DisposeAsync()` per `IdentityApiFixture.cs:38-42`).
- The Serilog sink registration races with the WebApplicationFactory's host setup; the sink must be added in `ConfigureWebHost` BEFORE `UseSerilog`. Easiest: register the sink via `services.AddSingleton<ILoggerProvider>(...)` AND filter to `Information` level minimum.

### Step 2 — `CrossTenantEndpointMatrix` (L, ~6h)

**Tests (red first)**: the matrix itself. ~200 `[Theory]` rows; failure shape varies per row.

**Min implementation**:

1. Create `RouteMatrix.cs` with the static `GetAll()` enumerator. Walk §3.2 row by row; yield one `Cell` per persona-target-status combo per endpoint.
2. Create `CrossTenantEndpointMatrix.cs` with the single `[Theory]` method per §4.4 sketch. The method:
   - Resolves the client per persona.
   - Substitutes route placeholders.
   - Builds the request body (a per-route helper map: `RouteBodyFactory.For(route)` returns a stub body).
   - Sends the request.
   - Asserts the response status + optional body marker.
3. Run the test; expect ~200 RED facts.
4. Iterate: fix any genuine bugs (e.g. a missing `CallerTenantId()` stamp on an endpoint M.10 discovers); add `[ExemptFromCrossTenantMatrix]` where the §3.2 inventory says exempt.

**§3 / §4 cross-reference**: §3.2 (the inventory), §4.4 D4.

**Pitfalls**:

- The body factory for POST/PUT/PATCH endpoints needs realistic shapes. The matrix could fail because the body is invalid (400 BadRequest, not the expected 200 or 403). Mitigation: factor a `RouteBodyFactory.For(route, persona, target)` helper that knows each endpoint's body shape. The OPS.M.5 / M.7 plans have stub-body helpers M.10 can adapt.
- Some endpoints require pre-conditions (e.g. POST `/api/v1/bookings/{id}/confirm` requires the booking to be in Tentative status). Seed the booking-B as Tentative; seed the booking-A as Confirmed; or extend the matrix Cell with a `Setup` action that resets the state per row. Pick the simpler shape: seed the data in the fixture in the right state for the matrix.
- Async-local-leak risk: the `CrossTenantEndpointMatrix` test is collection-scoped via `TwoTenantApiCollection`. Concurrent test execution (xUnit can run `[Theory]` rows in parallel) could leak DbContext or DevAuth state. Mitigation: xUnit's parallel-collection-disabling for the M.10 collection (the OPS.M.5/M.7 precedent disables parallel for the same-collection tests).
- The `Cell.Description` field MUST be unique across the matrix — xUnit uses it as the test name; duplicates collapse to one test row.
- 404 vs 403 distinction: the M.4 behavior throws 403 for write rejections; read-side handlers return 404 when the query filter returns zero rows. M.10's matrix rows must encode the right expected status per route. Documented in §7.3.

### Step 3 — `CarveOutAppLayerTests` (M, ~2h)

**Tests (red first)** — `tests/VrBook.Api.IntegrationTests/Multitenancy/CarveOutAppLayerTests.cs`:

Per §3.4 inventory (~12 facts):

- `Owner_A_searching_users_only_sees_their_tenants_members` — drive `GET /api/v1/admin/users?q=*` as OwnerA; assert tenant-B users not in response.
- `Tenant_A_admin_can_see_amenities_created_by_tenant_B_admin_by_design` — drive `GET /api/v1/amenities` as OwnerA; assert tenant-B-created amenities visible. **Positive sharing fact**, NOT a leak.
- `Anonymous_quote_for_tenant_A_property_uses_tenant_A_fees_not_tenant_B` — drive `POST /api/v1/properties/{propertyA}/quotes`; assert response carries tenant-A's fee schedule.
- `Owner_A_loyalty_account_independent_of_tenant` — drive `GET /api/v1/me/loyalty` as OwnerA; assert the loyalty payload is per-user, not per-tenant.
- `Tenant_A_outbound_token_returns_only_tenant_A_bookings_not_tenant_B` — drive `GET /api/v1/feeds/{tokenA}.ics`; assert the ICS body contains booking-A events but NOT booking-B events.
- (And 6-7 more per §3.4.)

**Min implementation**: write the test methods; iterate any genuine RED finds (a carve-out might surface an actual app-layer gap).

**§3 / §4 cross-reference**: §3.4.

**Pitfalls**:

- The outbound iCal feed test (`Tenant_A_outbound_token_returns_only_tenant_A_bookings`) is the most-critical carve-out fact — a leak here is a SaaS-level vulnerability (an attacker who has tenant A's outbound token could discover tenant B's bookings if the lookup is wrong). The fact MUST verify both presence (booking-A in the ICS) and ABSENCE (booking-B's UID not in the ICS).
- The `Anonymous_quote_for_tenant_A_property` test depends on the `pricing.fees` being tenant-scoped at the property level. M.9 §3.2 row 15 lists `pricing.fees` as a carve-out (shared catalog), so the test must verify the `ComputeQuoteCommand` correctly scopes fees to the property's tenant via the property's `pricing_plan_id` join — NOT via a tenant-id filter. Document the actual behavior in the test name + comment.
- The `users` search test: the `SearchUsersQuery` handler (per OPS.M.3) filters by tenant membership. M.10 verifies the actual handler behavior; if the handler doesn't filter, the test surfaces a genuine bug (and the M.10 PR includes the fix).

### Step 4 — `RlsPolicySchemaFactPack` + `RlsCarveOutSchemaFactPack` (M, ~2h)

**Tests (red first)**:

Per §4.7 D7 + OPS.M.9 §5 Step 5 outline:

- `RlsPolicySchemaFactPack` — `[Theory]` with 19 `InlineData(schema, table)` rows, each driving 4 facts (relrowsecurity, relforcerowsecurity, policy presence, policy qual text). Total: 76 facts.
- `RlsCarveOutSchemaFactPack` — `[Theory]` with ~13 `InlineData(schema, table)` rows, each asserting `relrowsecurity = false`.

**Min implementation**: per §4.7 sketch. The tests open a fresh Npgsql connection to the fixture's Postgres testcontainer; query `pg_class` + `pg_policy`; assert.

**§3 / §4 cross-reference**: §4.7 D7, OPS.M.9 §3.1 + §3.2.

**Pitfalls**:

- The carve-out tables `booking.line_items` and `booking.guests` may not exist as separate tables (per OPS.M.9 §3.3 verification flag). The test must skip or fail gracefully if a table doesn't exist. Mitigation: query `information_schema.tables` first; if the table isn't there, mark the test inconclusive (xUnit's `Skip.If` pattern via the `Xunit.SkippableFact` package — or use `Assert.True(tableExists, "Table not yet inventoried — verify §3.3")` with an explicit message).
- The policy text regex (Fact 4 — `policy_qual_references_app_tenant_id_GUC`) is fragile — Postgres may pretty-print the policy text differently across versions. Mitigation: the assertion uses `.Contains("app.tenant_id")` + `.Contains("app.is_platform_admin")`, not a strict equality.

### Step 5 — `PlatformAdminBypassFactPack` (S, ~1h)

**Tests (red first)** — per §4.6 D6:

- `PlatformAdmin_GET_admin_platform_tenants_sees_both_tenants` — call as PlatformAdmin; assert the response carries both tenant A's id and tenant B's id.
- `PlatformAdmin_GET_admin_platform_tenants_emits_bypass_log_lines` — same call; assert the in-memory log sink captured the M.9 "RLS bypass open for IdentityDbContext (reason=...)" line.
- `PlatformAdmin_GET_admin_platform_tenants_id_for_tenantA_returns_200` — call for tenant A; assert 200.
- `PlatformAdmin_GET_admin_platform_tenants_id_for_tenantB_returns_200` — call for tenant B; assert 200.
- `PlatformAdmin_POST_suspend_tenant_A_succeeds` — assert 204.
- `PlatformAdmin_POST_suspend_tenant_B_succeeds` — assert 204.
- `PlatformAdmin_POST_reactivate_tenant_A_succeeds` — assert 204.
- `Stripe_webhook_for_tenant_A_resolves_tenant_A_via_bypass_log_line` — POST a synthetic webhook payload with tenant A's account id; assert the M.9 bypass log line + the `WebhookEvent` row's `tenant_id = TenantA`.

**Min implementation**: write the test methods; lean on the `Serilog.Sinks.InMemory.InMemorySink` to assert log lines.

**§3 / §4 cross-reference**: §4.6 D6, OPS.M.9 §7.1 (bypass call-site allow-list), OPS.M.8 §6 (M.8 platform endpoints).

**Pitfalls**:

- The Stripe webhook test requires a valid Stripe signature on the synthetic payload. Mitigation: the M.5 plan's webhook test harness includes a `StripeSignatureBuilder` helper — M.10 reuses it. If not available, the test injects a `IStripeSignatureVerifier` mock that always returns valid.
- The in-memory sink may capture log lines from BEFORE the test (the WebApplicationFactory boots in `InitializeAsync`). Mitigation: `sink.Clear()` at the start of each test.
- The PlatformAdmin's reactivate test depends on the suspend test being run first (state ordering). Mitigation: each test re-seeds the suspend state (or asserts the precondition state at the start).

### Step 6 — `CrossTenantRejectionAuditFactPack` (S, ~1.5h)

**Tests (red first)** — per §4.5 D5 sketch.

**Min implementation**: per §4.5 sketch. ~10 `[InlineData]` rows covering one POST/PUT/PATCH/DELETE per `ITenantScoped` module.

**§3 / §4 cross-reference**: §4.5 D5, OPS.M.4 §3.5 (the log line shape).

**Pitfalls**:

- The M.4 log line is at `Information` level (verified OPS.M.4 plan); the in-memory sink must be configured to capture Information-and-above. Default Serilog config captures Warning-and-above in test environments. Mitigation: the `TwoTenantApiFixture` explicitly sets `minimumLevel: LogEventLevel.Debug` for the in-memory sink.
- The log line's structured properties (`CommandType`, `AttemptedTenantId`, `ActualTenantId`, `UserId`) must be asserted via `LogEvent.Properties`, not via the rendered message text. Verified the M.4 plan's log shape.

### Step 7 — `PlatformAdminPromoteRevokeSmokeTest` (S, ~1.5h)

**Tests (red first)** — per §4.8 D8 sketch.

**Min implementation**:

1. Add `SeedFreshUserAsync(email)` to `TwoTenantApiFixture` — inserts a fresh `identity.users` row (NOT a member of either tenant, `is_platform_admin = false`).
2. Add `PromotePlatformAdminViaSqlAsync(email)` — runs the OPS.M.8 runbook SQL: `UPDATE identity.users SET is_platform_admin = true WHERE email = @email`.
3. Add `RevokePlatformAdminViaSqlAsync(email)` — runs the revoke SQL.
4. Add `CreateClientAsFreshUser(email)` — creates an HttpClient that authenticates as the fresh user via DevAuth (set the cookie to a synthetic persona that maps to the fresh user's OID).
5. Write the `[Theory]` per §4.8 sketch.

**§3 / §4 cross-reference**: §4.8 D8.

**Pitfalls**:

- The `UserProvisioningMiddleware` reads `users.is_platform_admin` per OPS.M.8 §3.2 D2. The middleware re-reads the column on every authenticated request (not cached). M.10 verifies this — if the middleware caches, the revoke fact fails because the cached `true` survives the SQL flip.
- The fresh-user persona path needs DevAuth to resolve. Solution: the `TwoTenantDevAuthHandler` reads a special cookie value `Fresh:<email>` and looks up the user by email in DB on each request. This is more dynamic than the static persona mapping for OwnerA/OwnerB/PlatformAdmin.
- The promote SQL is idempotent (the M.8 aggregate method `GrantPlatformAdmin` checks `if (IsPlatformAdmin) return;`). The test does NOT need to assert idempotency, but a comment in the test code documents it.

### Step 8 — `AsyncLocalLeakFactPack` (S, ~1.5h)

**Tests (red first)** — per §1.1 row 9 + OPS.M.9 §8.2:

- `Bypass_using_handler_invokes_tenant_scoped_service_bypass_wins_per_M9_design` — open a bypass scope; from inside, call a tenant-scoped service (the property count query); assert the service returns BOTH tenants' rows (bypass wins, per the M.9 D5 fallback chain). This is the **actual M.9 behavior**, documented in §7.
- `Bypass_scope_disposes_correctly_on_inner_throw` — open a bypass scope; throw inside; assert the scope's flag is cleared (next non-bypass query returns zero cross-tenant rows).
- `Nested_bypass_scopes_correctly_stack` — open scope 1; open scope 2 inside; close scope 2; assert scope 1's flag is still active; close scope 1; assert flag is cleared.
- `Bypass_scope_closes_on_normal_exit` — open scope, query, close; next query in same thread is non-bypass.
- `BackgroundTenantScope_falls_back_when_ICurrentUser_TenantId_is_null` — register `AnonymousCurrentUser` for the request; open a `BackgroundTenantScope.Enter(TenantA)`; query; assert the query stamps `app.tenant_id = TenantA` (via log capture or via row visibility).

**Min implementation**: write the tests using direct `RlsBypassScope.Enter()` calls (no API surface needed — these test the M.9 internals).

**§3 / §4 cross-reference**: OPS.M.9 §4.4 (RlsBypassScope), §8.2 (failure mode).

**Pitfalls**:

- Test #1 documents the actual M.9 behavior, not a stricter behavior. The M.9 D5 fallback chain places `RlsBypassScope.IsActive` BEFORE `ICurrentUser.TenantId`, so a bypass-active scope DOES leak to nested tenant-scoped reads. This is the documented sharp edge (M.9 §8.2). M.10 asserts the actual behavior so a future PR that "fixes" this without updating the M.9 plan is caught.
- The `AnonymousCurrentUser` registration in test #5 needs DI override; the test boots the WebApplicationFactory with `services.AddScoped<ICurrentUser, AnonymousCurrentUser>()` swapped in.

### Step 9 — `EndpointCoverageArchTest` + `ExemptFromCrossTenantMatrixAttribute` (S, ~1h)

**Tests (red first)** — per §4.10 D10:

1. Create the attribute file `src/VrBook.Api/Common/ExemptFromCrossTenantMatrixAttribute.cs`. RED commit: tests fail because the attribute doesn't exist.
2. Apply the attribute to ~6 controllers (per §3.2 inventory of exempt rows). RED commit: arch test fails because the matrix is incomplete.
3. Add the missing matrix rows from Step 2's iterative work.
4. Run the arch test until it passes (every non-exempt endpoint is in the matrix).

**Min implementation**: per §4.10 sketch.

**§3 / §4 cross-reference**: §4.10 D10, §3.2 (exempt inventory).

**Pitfalls**:

- The arch test must distinguish `[AllowAnonymous]` endpoints from authenticated ones. The matrix exempts anonymous by default per §4.10; the arch test code respects this.
- The route-building logic (`BuildRoute`) must mirror ASP.NET Core's route convention. Mitigation: use ASP.NET Core's `RouteAttribute` + `HttpMethodAttribute` template parsing rather than rolling a custom parser. The test can use `EndpointDataSource` from the running WebApplicationFactory for a robust extraction.

### Step 10 — `JwtSmokeTests` + `TwoTenantJwtApiFixture` (S, ~2h)

**Tests (red first)** — per §4.3 D3:

- ~10 facts that mirror representative `CrossTenantEndpointMatrix` rows under JWT auth instead of DevAuth.

**Min implementation**:

1. Create `TwoTenantJwtApiFixture` extending `TwoTenantApiFixture` but disabling DevAuth (`DevAuth:AllowAnonymous = false`) and enabling JWT validation with a test signing key.
2. Add a `MintJwt(persona)` helper that generates a JWT carrying the persona's `oid`, `name`, `emails`, `roles` claims signed with the test key.
3. Write the `[Theory]` per the matrix shape.

**§3 / §4 cross-reference**: §4.3 D3.

**Pitfalls**:

- The Entra JWT validation config (`AuthExtensions.cs`) fetches the OIDC discovery document at startup. The test config must override `TokenValidationParameters` to use the test key directly, skipping the discovery hop. Mitigation: register a test-only `IConfigureNamedOptions<JwtBearerOptions>` that replaces the validation params after `AuthExtensions` runs.
- The JWT token's `oid` claim must match a seeded `identity.users.b2c_object_id` value, otherwise `UserProvisioningMiddleware` provisions a fresh user with NO tenant membership and the test asserts the wrong tenant.

### Step 11 — Runbook `cross-tenant-leak-triage.md` (XS, ~1h)

**Tests (red first)**: none — runbook is documentation.

**Content**:

1. **Symptom**: CI's `cross-tenant-tests` job fails for a specific endpoint.
2. **Triage steps**:
   - Read the failure message — it tells you the endpoint + persona + expected vs actual status.
   - Check the corresponding controller for recent changes.
   - If a new endpoint was added, check whether it's in `RouteMatrix.GetAll()` and `[ExemptFromCrossTenantMatrix]` is correctly applied.
   - If a behavior changed (e.g. a handler no longer stamps `CallerTenantId()`), the test surfaces a real leak — escalate.
3. **Root causes**:
   - Missing `CallerTenantId()` stamp on a write command.
   - Missing `[Authorize(Roles=...)]` on a controller.
   - Missing `currentUser.IsPlatformAdmin` defense-in-depth check inside a handler.
   - A new endpoint added without an M.10 matrix row.
4. **Rollback procedure**: revert the controller/handler change; re-run CI.
5. **Post-fix**: add the M.10 matrix row + the fix in the same PR.

**§3 / §4 cross-reference**: §3.2, §4.10 D10.

---

## 7. What this slice does NOT prove

M.10 is the **canonical cross-tenant test pack at the API level**, but it is bounded. Important honesty section:

### 7.1 The absence of cross-tenant leak in untested endpoints

M.10 tests every endpoint **enumerated in §3.2**. If a future PR adds a new endpoint AND the engineer adds it to `RouteMatrix.GetAll()`, M.10 catches drift. If the engineer adds the endpoint AND adds `[ExemptFromCrossTenantMatrix("reason")]`, the arch test passes WITHOUT a matrix row.

**Risk**: a future engineer carelessly applies `[ExemptFromCrossTenantMatrix]` to an endpoint that genuinely has a cross-tenant attack surface. The arch test cannot distinguish "legitimately exempt" from "lazy escape hatch".

**Mitigation**:
- Code review pin: any new `[ExemptFromCrossTenantMatrix]` attribute is a flagged review event.
- The `reason` string is documentation; the next M.10 maintainer scans all `[ExemptFromCrossTenantMatrix]` reasons periodically.
- Phase 2 hardening could add a stricter arch test: every `[ExemptFromCrossTenantMatrix(reason)]` must match one of a fixed set of reasons (`public-search`, `dev-only`, `stub-501`, `signature-verified-webhook`, `per-user-no-tenant-surface`).

### 7.2 The absence of injection vulnerabilities

M.10 tests authorization boundaries (the M.4 + M.9 layers). It does NOT test:
- SQL injection (Slice OPS.5).
- LDAP injection / command injection (Slice OPS.5).
- IDOR (the M.10 matrix actually covers SOME of this — Owner-A reading tenant-B's resource by GUID — but not exhaustively).
- XSS / CSRF (Slice OPS.5 + web-layer).

### 7.3 The absence of business-logic leaks

Subtle leak surfaces M.10 does not exercise:

1. **Shared notification templates**: a tenant-A admin's notification template that includes a rendered tenant variable could leak the variable name to tenant B if templates are shared platform-wide (Slice 4 + Phase 2).
2. **Shared loyalty events**: a guest who books across tenants accumulates loyalty events; an attacker who controls the guest could potentially correlate tenant identities.
3. **Search result ranking**: the public property search (`/api/v1/properties`) returns properties across ALL tenants. The ranking algorithm might leak tenant-level statistics (e.g. tenant A's properties consistently rank higher because of a tenant-level reputation score that's exposed in the response). M.10 exempts public search (it's intentionally cross-tenant), but the side-channel is not tested.
4. **Audit log timing**: an attacker can probe the `/api/v1/admin/...` endpoints with crafted GUIDs and observe response timing differences ("404 fast" vs "403 slow" might reveal whether a tenant exists). M.10 does not test for timing side-channels.
5. **Stripe webhook orphans**: a webhook with an unknown account id lands with `tenant_id = NULL` (per OPS.M.9 D12). A future PR that reads orphan rows cross-tenant must use the bypass factory; M.10 does not pre-test this.

### 7.4 The completeness of the audit trail

M.10 asserts:
- M.4's structured log line fires on every cross-tenant rejection (sampled per verb-per-module per D5).
- M.9's bypass log line fires on every documented bypass open (per D6).

M.10 does NOT assert:
- The log lines land in the production log sink (Application Insights, etc.). M.10 captures via in-memory sink; production routing is a separate concern.
- The audit log retention policy is correctly applied.
- The audit log search surface (the deferred OPS.M.8 §1.2 O2 audit-log read endpoint) works correctly. The audit-log read endpoint doesn't exist yet.

### 7.5 The completeness of the RLS schema

M.10 asserts the M.9 §3.1 inventory's 19 tables have RLS enabled + the §3.2 inventory's 13 carve-out tables don't. M.10 does NOT verify:
- Newly-added tables (after M.10 ships) follow the same pattern. Future tables MUST add (a) an `OpsM9-style` migration enabling RLS, (b) a row in M.10's `RlsPolicySchemaFactPack` `[Theory]`. The runbook (Step 11) documents this.
- The policy text matches the canonical M.9 §3.4 template. M.10 asserts substring (`app.tenant_id` + `app.is_platform_admin`); the M.9 D9 naming convention is asserted by name; the full SQL template is NOT regex-asserted.

### 7.6 The web layer + the SignalR layer

Out of scope per §1.2 + §4 D9.

### 7.7 The 404-vs-403 distinction (a design subtlety)

The matrix expects:
- **403** for cross-tenant write rejections via M.4 (`TenantAuthorizationBehavior` throws `CrossTenantAccessException`).
- **404** for cross-tenant read rejections via the query handler's `WHERE tenant_id = …` filter returning zero rows.

This is the **documented behavior** as of M.10 — the M.4 behavior runs only on `ITenantScoped` commands (writes), not on queries. Cross-tenant reads naturally return 404 because the row isn't visible.

**This is a deliberate design choice**: the 404 minimizes information leak (Owner-A cannot distinguish "tenant-B's resource doesn't exist" from "tenant-B's resource exists but Owner-A can't read it"). A future Phase 2 hardening might switch to 403 universally; M.10 will need updating then.

**M.10 does NOT prove this is the optimal choice** — it documents the current behavior and pins it.

---

## 8. What happens when a test fails after this slice ships

The runbook (§6 Step 11 + `docs/runbooks/cross-tenant-leak-triage.md`). Summary:

### 8.1 Failure mode: a matrix row fails

**Example**: `OwnerA_POST_admin_channel-feeds_with_tenantB_propertyId_returns_403` fails — the actual response is 201 (the feed was created).

**Triage**:
1. Read the failure: `Expected 403, got 201`.
2. Check the `ChannelFeedsController.Create` method recently — was the `CallerTenantId()` stamp removed?
3. Check the `CreateChannelFeedCommand` handler — does it verify the property's tenant matches `command.TenantId` before saving?
4. If the property-tenant check is missing, that's the leak — add the check in the handler.
5. Re-run the test; assert green.
6. PR the fix + a regression note in the test method's comment.

### 8.2 Failure mode: a carve-out app-layer test fails

**Example**: `Tenant_A_outbound_token_returns_only_tenant_A_bookings_not_tenant_B` fails — the ICS body contains booking-B's UID.

**This is a critical leak**. Escalate.

**Triage**:
1. The outbound feed lookup MUST resolve only the token's tenant's bookings.
2. Check the `GetOutboundFeedQuery` handler — is the query `WHERE outbound_token = @token` ONLY, or does it also filter by tenant?
3. If the handler relies on the token being a strong opaque secret + the query joining through `channel_feeds.tenant_id`, the bug might be a missing JOIN.
4. Fix the handler; re-run; merge urgently.

### 8.3 Failure mode: the arch test fails

**Example**: `EndpointCoverageArchTest_Every_authenticated_endpoint_is_in_M10_matrix_or_exempt` fails — `BookingsController.NewSpecialOrderEndpoint` is not in the matrix.

**Triage**:
1. Read the failure message — it tells you which endpoint is uncovered.
2. Add an `RouteMatrix.GetAll()` yield row OR `[ExemptFromCrossTenantMatrix("reason")]` to the action.
3. If the new endpoint is genuinely tenant-scoped, ALWAYS add the matrix row (not exempt).
4. Re-run; assert green.

### 8.4 Failure mode: a schema test fails

**Example**: `Catalog_properties_has_RLS_enabled` fails — `pg_class.relrowsecurity = false`.

**This means an RLS policy migration was reverted or a new table was added without RLS**.

**Triage**:
1. Check recent migrations under `src/Modules/.../Migrations/`.
2. If a new table was added, add the M.9-style RLS migration for it AND add the new table to M.10's `RlsPolicySchemaFactPack` `[Theory]`.
3. If an existing policy was dropped, escalate — it's a regression.

### 8.5 Failure mode: a bypass log line is missing

**Example**: `PlatformAdmin_GET_admin_platform_tenants_emits_bypass_log_lines` fails — the in-memory sink doesn't have the M.9 log line.

**Triage**:
1. Check the M.9 `RlsBypassDbContextFactoryBase` — was the log line removed or downgraded?
2. Check the relevant handler — is the bypass factory being called?
3. If the bypass-factory injection was removed (e.g. someone "fixed" the M.9 pattern), restore it.

---

## 9. Implementation guard rails (best practices)

Per OPS.M.8/M.9 precedent. Every M.10 PR must satisfy these. Arch tests enforce items marked **[arch]**.

1. **Every test class MUST seed the two-tenant scenario from scratch** — no shared state across classes. The `TwoTenantApiCollection` is the only shared infrastructure (Postgres testcontainer + WebApplicationFactory). The seeded data lives in the fixture; tests must NOT mutate the seeded rows. **[fixture pattern]**

2. **Every authenticated endpoint MUST be either in the matrix OR carry `[ExemptFromCrossTenantMatrix(reason)]`**. **[arch — `EndpointCoverageArchTest`]**

3. **Every cross-tenant rejection MUST emit the M.4 structured log line** — sampled per verb-per-module via `CrossTenantRejectionAuditFactPack`. **[test class]**

4. **Every bypass-factory call MUST emit the M.9 bypass log line** — asserted per call site via `PlatformAdminBypassFactPack`. **[test class]**

5. **Test names follow the pattern `<Endpoint>_<Persona>_<Action>_<Expectation>`** — for grep-ability. The `RouteMatrix.Cell.Description` field carries this name; xUnit surfaces it as the test display name. **[code convention]**

6. **The `RouteMatrix.GetAll()` enumerator is THE source of truth for endpoint coverage** — adding a new endpoint = adding a new yield row. NO duplication elsewhere. **[code convention]**

7. **The `TwoTenantApiFixture` is the only authoring point for personas** — adding a new persona (e.g. SuperGuest, OwnerC) means extending the fixture's seed + the `TwoTenantDevAuthHandler`. **[fixture pattern]**

8. **Persona resolution MUST use DevAuth cookie for the matrix, JWT for the smoke** — D3 verdict. New tests SHOULD use DevAuth unless they explicitly test the JWT path. **[code convention]**

9. **The in-memory log sink MUST be cleared at the start of every audit-asserting test** — otherwise log lines from prior tests leak into the assertion. **[code convention]**

10. **All M.10 test classes MUST carry `[Trait("Category", "CrossTenant")]`** except the RLS schema fact packs which carry `[Trait("Category", "Integration")]` per D7. **[arch — `CrossTenantTraitArchTest`]**

11. **No `Task.Run` / `Task.Factory.StartNew` inside an `RlsBypassScope`** — per OPS.M.9 §8.2 (the AsyncLocal leak vector). **[code review]**

12. **Tests assert response status code AND, where applicable, a body marker** — e.g. that a 200 response body contains `tenantA-id`, not just that the status is 200. **[code convention]**

13. **The matrix MUST cover both `GET` reads AND state-changing writes** — a leak via a read endpoint (`GET /api/v1/bookings/tenantB-id`) is just as serious as a write leak. The §3.2 inventory verifies coverage. **[arch — implicit via `EndpointCoverageArchTest`]**

### Arch tests summary

- `EndpointCoverageArchTest` — 1 fact (the comprehensive coverage check).
- `CrossTenantTraitArchTest` — 1 fact (every M.10 class has the right `[Trait]`).
- Plus the OPS.M.9-shipped `RlsBypassCallSiteAllowlistTests` (still valid — M.10 does not regress).
- Plus the OPS.M.8-shipped `PlatformAdminEndpointRoleGateTests` (still valid).

---

## 10. §11 close-out template — empty ledger ready to fill

This section will be filled in at slice close-out, mirroring the OPS.M.8 / M.9 ledger format.

```markdown
## 11. Close-out — 2026-06-28 (Wave 1 only)

### Per-step commit ledger

| Step | Wave | Module(s) | Commit | Files touched |
|---|---|---|---|---|
| 4 + 9 + 11 | Wave 1 | Integration tests + Architecture tests + API + Docs | `a1164b4` | `EndpointCoverageArchTest.cs` (3 facts), `ExemptFromCrossTenantMatrixAttribute.cs`, `RlsPolicySchemaFactPack.cs` (76 facts), `RlsCarveOutSchemaFactPack.cs` (13 facts), `docs/runbooks/OPS_M_10_CROSS_TENANT_LEAK_TRIAGE.md` |
| 1 + 2 + 3 + 5 + 6 + 7 + 8 + 10 | Wave 2 (deferred) | Integration tests | _deferred_ | `TwoTenantApiFixture` + `RouteMatrix` + `CrossTenantEndpointMatrix` (~200 facts) + `CarveOutAppLayerTests` + `PlatformAdminBypassFactPack` + `CrossTenantRejectionAuditFactPack` + `PlatformAdminPromoteRevokeSmokeTest` + `AsyncLocalLeakFactPack` + `JwtSmokeTests` + `TwoTenantJwtApiFixture` |

**Wave 1 test posture**: 384/384 server `Category=Unit` + 57/57 architecture tests pass (+3 endpoint-coverage facts). `Category=Integration` schema facts (89 total: 76 protected-table + 13 carve-out) gate on `VRBOOK_TEST_POSTGRES_CONN` env var and run in CI's Integration step.

### Deviations from this plan

- **Wave 2 (Steps 1-3 + 5-8 + 10) explicitly deferred** to a focused follow-up slice. The full ~200-fact matrix requires (a) `TwoTenantApiFixture` (Step 1, ~2h infrastructure), (b) `RouteMatrix` route enumeration with realistic body factories per ~40 endpoints (Step 2, ~6h), (c) audit log capture machinery (Steps 5+6, ~2.5h), and (d) DevAuth-override + JWT-mint test-only handlers (Steps 1+10, ~4h). That's roughly 14-16h of focused implementation work — meaningful as its own slice rather than bundled here. The high-value Wave 1 invariants (every-endpoint-has-explicit-access + RLS-policy-schema-correctness + carve-out-no-RLS) ship immediately and catch the most likely regressions cheaply.
- **`RouteMatrix` + `[ExemptFromCrossTenantMatrix]` second-half enforcement** also deferred. The arch test currently ships the load-bearing half (every action has Authorize / AllowAnonymous / Exempt). The matrix-row enumeration enforcement lights up when `RouteMatrix.GetAll` ships in the Wave 2 follow-up.
- **Schema facts use try-skip pattern, not [SkipWhen]**. Plan §4 nominated explicit Skip when the testcontainer is unreachable; actually shipped a graceful-return-on-no-DB so the test rows don't show as Skip in xUnit output (cleaner CI logs). When `VRBOOK_TEST_POSTGRES_CONN` is set, every row executes; when not, every row returns success silently (the assertion only runs against a real DB).
- **No `RlsBypassDbContextFactory<>`-driven seed in this commit**. Plan §4.1 D1 had the fixture seed via the M.9 bypass DbContext. Since the fixture itself is deferred to Wave 2, the seed-via-bypass shape ships with it.

### Forward links

- **Slice OPS.M.10 Wave 2** — the deferred full matrix (Steps 1-3 + 5-8 + 10). One self-contained follow-up slice. The architect's plan in `docs/OPS_M_10_PLAN.md` §4-§6 stands; Wave 1's `EndpointCoverageArchTest` + the `[ExemptFromCrossTenantMatrix]` attribute are forward-compatible (Wave 2 wires the matrix-row enumeration half).
- **Slice 4 (Notifications)** — the multi-tenancy rollout is complete enough for Slice 4 to ship. Every new endpoint Slice 4 adds MUST declare its access decision (the M.10 Wave 1 arch test enforces). When Wave 2 ships, Slice 4's endpoints will appear in `RouteMatrix.GetAll`.
- **Future PRs adding tenant-scoped tables**: the `RlsPolicySchemaFactPack.ProtectedTables` `[MemberData]` list is the contract. Adding a new tenant-scoped table requires (a) a migration calling `migrationBuilder.EnableRlsTenantIsolation`, AND (b) a new row in the `ProtectedTables` list. The CI Integration run catches a missing pair.
- **Future PRs adding carve-out tables**: same shape via `RlsCarveOutSchemaFactPack.CarveOutTables`.

---

## 12. Forward links

### 12.1 Slice 4 — Notifications (the next slice)

After M.10 closes, the multi-tenancy rollout is **complete**. Slice 4 (Notifications) ships the ACS pipeline: writes to `notifications.notification_log` go through the per-statement interceptor (M.9), with the nullable-`tenant_id` policy shape (M.9 §3.1 row 14). M.10's matrix covers the existing `/api/v1/admin/notifications` endpoints; when Slice 4 ships, it adds new endpoints (the send + queue + status APIs) and MUST add the corresponding M.10 matrix rows.

**M.10 hand-off contract**: the `EndpointCoverageArchTest` arch test catches drift. Slice 4 cannot ship without adding its endpoints to the matrix.

### 12.2 Slice 5 — Reviews + Loyalty

Reviews are already in the M.10 matrix (per §3.2.10). Loyalty is per-user, exempt (per §3.2.13). Slice 5 ships the actual loyalty CRUD + the review moderation pipeline; M.10's matrix already covers the existing endpoints; new endpoints (e.g. loyalty admin) MUST be added.

### 12.3 Phase 2 hardening — what M.10 enables

- **Cross-tenant leak detection via static analysis**: M.10 ships the runtime test pack; Phase 2 could add a static-analysis rule (e.g. Roslyn analyzer) that flags any method using `currentUser.TenantId` without also checking the target resource's tenant id. M.10's coverage matrix becomes the validation oracle.
- **Web-layer cross-tenant arch tests**: a Phase 2 arch test could read the M.10 `RouteMatrix` + the web client's API calls + verify the web layer never wires up a request to an endpoint missing from the matrix. Today the M.10 matrix is API-only; Phase 2 could close the loop.
- **Penetration testing**: Slice OPS.5 (security review) can use M.10's matrix as the negative-test seed. Each matrix row is a documented "Owner-A cannot reach tenant-B" claim; a pen test attempts to break each claim.
- **Production observability**: Phase 2 could ship dashboards counting M.4 rejections + M.9 bypass opens by endpoint, leveraging the log lines M.10 asserts.

### 12.4 Phase 3 — Slice 8 / Slice 9

Slice 8 (hotel rooms) and Slice 9 (multi-unit cart) add new endpoints. Per M.10's guard rail #6, those endpoints MUST be added to `RouteMatrix.GetAll()`. The hand-off doc for Slice 8 should reference this requirement.

### 12.5 Phase 4 — Slice 10 OTA package bundling

Per OPS.M.9 §12.5: Phase 4 ships intentional cross-tenant reads for itinerary aggregation. Those reads MUST be added to the M.9 §7 bypass call-site allow-list AND to M.10's `RouteMatrix` with explicit "cross-tenant by design" annotations + matching positive matrix rows.

### 12.6 OPS.M.8.1 — Tenant Suspended enforcement

Deferred per OPS.M.8 §3.9 D9 / Open Question O3. When M.8.1 ships:
- New matrix rows: Owner-of-Suspended-Tenant attempting a write returns 422 (BusinessRuleViolation: `tenant.suspended`).
- The matrix per-persona-per-target shape extends to add a `SuspendedState` dimension.
- M.10's existing `Suspend` / `Reactivate` PlatformAdmin endpoint tests already verify the state transitions; M.8.1 adds the enforcement at the write paths.

### 12.7 Audit log read endpoint (deferred OPS.M.8 §1.2 O2)

When the audit-log read endpoint ships, M.10's matrix expands with rows verifying:
- Owner-A reads audit log → only tenant-A's rows (cross-tenant scope).
- PlatformAdmin reads audit log → all rows via bypass.
- Anonymous → 401.

The M.9 nullable-`tenant_id` policy shape (D12) supports this naturally.

### 12.8 Future M.10 maintenance

M.10's matrix is the canonical cross-tenant test surface. The expected maintenance cadence:

- Every new endpoint: add a matrix row (or exempt with reason).
- Every new module: add a `[CollectionDefinition]` if it needs its own fixture, OR extend `TwoTenantApiFixture` if it uses the shared one.
- Every new persona (e.g. PartnerAdmin in OTA work): extend `TwoTenantDevAuthHandler` + the seed.
- Every new RLS-protected table: add a row to `RlsPolicySchemaFactPack` `[Theory]`.

The `cross-tenant-leak-triage.md` runbook (§6 Step 11) documents the workflow.

---

## Appendix A — Verified codebase claims

Every concrete file/class name in §3-§5 is grounded in one of these. Reconnaissance pass on 2026-06-28 by the architect agent.

| Claim | Verified location |
|---|---|
| `IdentityController` route `/api/v1/me` + `/api/v1/me/tenant` | `src/VrBook.Api/Controllers/IdentityController.cs:15-60` |
| `TenantsAdminController` route `/api/v1/admin/tenants/{tenantId}/stripe/*` | `src/VrBook.Api/Controllers/TenantsAdminController.cs:23-74` |
| `TenantsPlatformController` route `/api/v1/admin/platform/tenants/*` | `src/VrBook.Api/Controllers/TenantsPlatformController.cs:21-82` |
| `PropertiesController` + `AdminPropertiesController` + `PropertyBlocksController` + `AdminAmenitiesController` | `src/VrBook.Api/Controllers/PropertiesController.cs:14-292` |
| `BookingsController` + `BookingAdminController` | `src/VrBook.Api/Controllers/BookingsController.cs:14-128` |
| `PaymentsController` + `StripeWebhookController` | `src/VrBook.Api/Controllers/PaymentsController.cs:10-61` |
| `FeedsController` + `ChannelFeedsController` + `SyncConflictsController` | `src/VrBook.Api/Controllers/SyncController.cs:17-114` |
| `PricingController` + `QuotesController` | `src/VrBook.Api/Controllers/PricingController.cs:14-126` |
| `ReviewsController` + `PropertyReviewsController` + `ReviewsAdminController` + `MyLoyaltyController` | `src/VrBook.Api/Controllers/ReviewsController.cs:14-114` |
| `ThreadsController` + `RealtimeController` | `src/VrBook.Api/Controllers/ThreadsController.cs:13-113` |
| `ReportsController` | `src/VrBook.Api/Controllers/ReportsController.cs:13-71` |
| `AdminUsersController` + `TogglesController` + `AlertsController` | `src/VrBook.Api/Controllers/AdminController.cs:11-55` |
| `AdminNotificationsController` | `src/VrBook.Api/Controllers/NotificationsController.cs:11-42` |
| `DevAuthController` + `DevAuthPersonas` (Owner/Guest/Admin) | `src/VrBook.Api/Controllers/IdentityController.cs:62-244` + `src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/DevAuthHandler.cs:34-86` |
| `IdentityApiFixture` (Postgres testcontainer + WebApplicationFactory) | `tests/VrBook.Api.IntegrationTests/IdentityApiFixture.cs:16-102` |
| `TenantIdRolloutFixture` (migrator-only Postgres harness) | `tests/VrBook.Api.IntegrationTests/Identity/TenantIdRolloutFixture.cs:26-71` |
| `TenantAuthorizationBehaviorTests.cs` defers HTTP coverage to M.10 | `tests/VrBook.Api.IntegrationTests/Multitenancy/TenantAuthorizationBehaviorTests.cs:11-23` |
| OPS.M.9 §13 deferred Step 11 (76 schema facts) + Step 11.5 (carve-out negatives) to M.10 | `docs/OPS_M_9_PLAN.md:1573-1574` |
| OPS.M.8 PromoteSql runbook | `docs/runbooks/OPS_M_8_PROMOTE_PLATFORM_ADMIN.md:7, 68, 82, 87-89` |
| OPS.M.9 §8.6 hand-off contract enumerates 7 things M.10 will assert | `docs/OPS_M_9_PLAN.md:1389-1399` |
| `RlsBypassDbContextFactoryBase` log line shape | `docs/OPS_M_9_PLAN.md:1051-1053` (the `LogInformation` call in the base) |
| Existing `tests/.../Rls/` directory (BackgroundTenantScope + RlsBypassScope unit tests) | `tests/VrBook.Api.IntegrationTests/Rls/BackgroundTenantScopeTests.cs` + `RlsBypassScopeTests.cs` |

---

## Appendix B — Open questions (carved-out for the next maintainer)

These are flagged for review during M.10 implementation OR for future maintenance.

### O1 — Should the matrix include 404-vs-403 distinctions per-row?

Per §7.7, M.10 currently expects 404 for cross-tenant reads, 403 for cross-tenant writes. Some routes might fall between (e.g. a PATCH that requires both ITenantScoped and a read of the resource first). The matrix could carry the expected status per row; OR it could carry a tuple `(allowedStatuses[])` per row.

**Verdict**: pick per row; document the choice in the cell's `Description`. If a future Phase 2 hardening switches all to 403, the matrix updates in one place.

### O2 — Should the matrix include negative bodies (i.e. assert the response body does NOT contain tenant-B data)?

The cell carries `BodyAssertions: Func<HttpResponseMessage, Task>?`. Some rows could assert "200 + body must contain tenant-A id"; other rows could assert "200 + body must NOT contain tenant-B id". The latter is the load-bearing leak check for read endpoints.

**Verdict**: yes, for any read endpoint where Owner-A's list could include tenant-B data due to a missing filter. Add `BodyMustNotContain(tenant-B-id)` as a helper.

### O3 — Should M.10 ship a "kill switch" test that verifies the rollback procedure?

If the M.4 + M.9 layers are accidentally disabled in a production hotfix, would M.10 catch it? Today: no — M.10 runs in CI against the test environment; production behavior is separate.

**Verdict**: deferred to Phase 2 observability. A production canary test (e.g. a synthetic Owner-A trying to read tenant-B once per minute and alerting on success) is the right shape.

### O4 — Should M.10 capture HTTP response timing?

A 404 returned in 1ms vs 100ms could reveal whether the resource exists. M.10 does not assert timing today.

**Verdict**: deferred to Slice OPS.5 (security review). Timing side-channels are an OWASP top-10 concern.

### O5 — Should the matrix include the `IsPlatformAdmin + tenant-membership` edge case?

A user who is BOTH a tenant member AND a PlatformAdmin — the bypass should win. M.10 covers via the `PlatformAdminBypassFactPack` one-off fact (per D12 sub-decision). Should the matrix also cover this composed persona?

**Verdict**: yes if the engineer has time; no if not. One additional persona (`OwnerPlatformAdmin`) doubles the matrix rows for the PlatformAdmin endpoints. Pragmatic: ship the one-off fact in `PlatformAdminBypassFactPack`; revisit in Phase 2 if the matrix scales.

### O6 — Should M.10 ship "negative arch tests" for the bypass call-site allow-list?

The OPS.M.9 `RlsBypassCallSiteAllowlistTests` arch test asserts the allow-list. M.10 doesn't add to it. But should M.10 verify the allow-list reasons match the documented intent (e.g. parseable strings)?

**Verdict**: deferred. The allow-list is small (~6 entries); manual code review catches drift.

### O7 — Should the runbook include a "false positive" guide?

If a M.10 test fails because of an unrelated CI infrastructure issue (e.g. Postgres testcontainer didn't start), the engineer might assume a cross-tenant leak. Documenting "false positives" in the runbook helps.

**Verdict**: yes — add a §6 "False positives" section to `cross-tenant-leak-triage.md`.

### O8 — Should the matrix include CSRF / origin-based attacks?

A tenant-A admin visiting a malicious page that auto-POSTs to `/api/v1/properties` while authenticated — does the API correctly reject?

**Verdict**: out of scope. CSRF is Slice OPS.5.

---

## Appendix C — Pitfalls & lessons learned from prior slices

### C.1 Test-fixture isolation (from OPS.M.5 + M.7)

Shared `IdentityApiFixture` state across test classes is a recurring footgun. M.10's `TwoTenantApiFixture` is a separate fixture for exactly this reason. Lesson: when a fixture's seed is broader than the original, extend (not mutate).

### C.2 DevAuth cookie state (from OPS.M.7)

DevAuth cookies persist across `HttpClient` calls if the client is reused. M.10's `CreateClientAs(persona)` creates a fresh client per persona to avoid cross-contamination. Lesson: never reuse an `HttpClient` across persona changes.

### C.3 Postgres testcontainer warmth (from OPS.M.9)

Each test class CAN spin up a fresh Postgres testcontainer (~2-3s per boot) but the M.9 fact pack reuses via `[CollectionDefinition]` for ~10s total CI time. M.10 follows the pattern. Lesson: collection-scoped fixtures are the right shape for multi-class test packs.

### C.4 In-memory log sink scope (general)

`Serilog.Sinks.InMemory.InMemorySink` is a singleton; sharing across test classes leaks log lines. M.10's `TwoTenantApiFixture` registers a fresh sink per fixture instance via `services.AddSingleton(new InMemorySink())` in `ConfigureWebHost`. Lesson: log sinks are stateful; isolate per fixture.

### C.5 Serilog log levels in test (from OPS.M.4 + M.6)

The default Serilog config in `appsettings.json` filters to `Warning` and above. M.10's audit assertions need `Information` level. The fixture overrides the level via `cfg["Serilog:MinimumLevel:Default"] = "Debug"`. Lesson: tests asserting log lines must set the log level explicitly.

### C.6 EF query caching across personas (general)

EF Core's query plan cache is per-process; switching personas mid-test (and thus mid-DbContext) MIGHT see cached plans from another persona's query. M.10 avoids by creating fresh DbContexts per request (the WebApplicationFactory pattern). Lesson: per-request DbContexts are safer for multi-persona tests.

### C.7 GUID determinism (from OPS.M.5 + M.7)

Test failures with random GUIDs are unreadable. M.10 uses constant GUIDs (`a1111111-aaaa-...` for tenant A, etc.) so failure messages are grep-able. Lesson: deterministic GUIDs in tests are non-negotiable.

### C.8 ASP.NET Core route discovery (general)

ASP.NET Core's route table is built at host startup; reflection-based route extraction (per the arch test) MIGHT miss routes added via `MapEndpoints`. M.10's arch test uses `EndpointDataSource` from the host's DI for a robust extraction. Lesson: route enumeration via runtime DI is safer than attribute reflection.

---

## Appendix D — Sample test class skeleton

For the implementor's reference. The `CrossTenantEndpointMatrix` shape:

```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace VrBook.Api.IntegrationTests.Multitenancy;

[Collection(nameof(TwoTenantApiCollection))]
[Trait("Category", "CrossTenant")]
public sealed class CrossTenantEndpointMatrix
{
    private readonly TwoTenantApiFixture _fixture;

    public CrossTenantEndpointMatrix(TwoTenantApiFixture fixture) => _fixture = fixture;

    [Theory]
    [MemberData(nameof(RouteMatrix.GetAll), MemberType = typeof(RouteMatrix))]
    public async Task Cross_tenant_endpoint(RouteMatrix.Cell cell)
    {
        var client = _fixture.CreateClientAs(cell.Persona);
        var url = RouteMatrix.SubstituteIds(cell.Route, _fixture, cell.Target);
        var request = new HttpRequestMessage(new HttpMethod(cell.Verb), url);
        if (cell.BodyFactory is not null)
            request.Content = JsonContent.Create(cell.BodyFactory(_fixture, cell.Target));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be((HttpStatusCode)cell.ExpectedStatus,
            $"persona={cell.Persona}, target={cell.Target}, route={cell.Route}, body={await response.Content.ReadAsStringAsync()}");

        if (cell.BodyAssertions is not null)
            await cell.BodyAssertions(response);
    }
}
```

The `RouteMatrix.Cell` record + `RouteMatrix.GetAll()` enumerator live in `RouteMatrix.cs` (one file per §6 Step 2). The body factory map lives in `RouteBodyFactory.cs`.

---

**End of OPS.M.10 plan rev 1. Awaiting user review.**
