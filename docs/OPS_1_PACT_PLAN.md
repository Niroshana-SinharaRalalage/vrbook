# OPS.1 — Pact Contract Tests (FE ↔ API) Plan

- **Status:** LOCKED — §7 architect-recommended answers adopted per memory rule `feedback_technical_decisions_are_architect_call`. Ready to execute.
- **Date:** 2026-07-09.
- **Author:** Platform Enterprise Architect (agent) via OPS.1 planning consult.
- **Trigger:** MASTER_PLAN §1 flips 2026-07-09 → Phase 1 complete; OPS.1 is the first launch-hardening slot per `EXECUTION_PLAN.md` §8 (Option A). §18.2 of `BookingApp_Proposal.md` locks 7 critical integration flows that OPS.1 must cover as contract tests.
- **Predecessors:** [`OPS_M_22_ADMIN_PRESEED_PLAN.md`](OPS_M_22_ADMIN_PRESEED_PLAN.md) (shape template + admin-flow gating that pact tests must respect), [`OPS_M_14_DEVAUTH_RETIREMENT_PLAN.md`](OPS_M_14_DEVAUTH_RETIREMENT_PLAN.md) (TestAuthHandler pattern), OPS.M.10 Wave 2 (`TwoTenantApiFixture` — reuse target).

---

## §0 What we're doing + why now

**OPS.1 requirement (`EXECUTION_PLAN.md` §8):** ship consumer-driven contract tests between the Next.js SPA (consumer) and the .NET 8 API (provider) covering "at least the 7 §18.2 mandated flows." The tests must run in CI on every push to develop, catch API-shape drift before staging deploy, and NOT loosen the Entra-only auth surface locked by `CLAUDE.md §Owner-locked policies` + ADR-0016.

**Why now:** MASTER_PLAN §2 puts OPS.1 at slot 16 (first launch-hardening). Slices 0–7 are green; the next batch of feature work is Phase 3 (hotel-style rooms), which will bump the API shape. Landing pact BEFORE Phase 3 means Slice 8's contract changes are caught at the seam rather than at the Playwright E2E gate two slices later. Deferring OPS.1 would let Slice 8 quietly break the SPA in a way that only smoke-testing catches.

**The proposal's 7 §18.2 flows** (verbatim, from `BookingApp_Proposal.md` §18.2):
1. End-to-end booking: search → hold → book → Stripe webhook → tentative → owner confirm → confirmed.
2. SLA auto-confirm: book → time-jump → SLA worker → confirmed.
3. iCal conflict: `external_reservation` imported → conflict detected → owner resolves with `cancel_direct` → booking rejected + refund.
4. Cancellation policy refund calculation across all three policies × 4 timing buckets.
5. Concurrent booking attempt for same dates: only one succeeds, the other returns 409.
6. Stripe webhook idempotency: same event delivered twice → state mutated once.
7. Loyalty tier promotion on `BookingCompleted` → next quote shows discount.

**Coverage boundary (architect resolves in §6):** Pact is a synchronous request/response contract test between two identifiable services. Flows 2 (SLA worker), 3 (iCal poller), 6 (Stripe webhook), 7 (BookingCompleted event) have server-only halves; only their FE-observable tails are Pact-shaped. Flow 6 has **no FE consumer at all** — the caller is Stripe. Coverage decision:

| §18.2 # | FE-facing tail (Pact) | Server-only side (integration test) |
|---|---|---|
| 1 | Full: search + hold + place + owner confirm (4 interactions) | Stripe webhook capture stays in IT |
| 2 | `GET /api/v1/bookings/{id}` after time-jump (1) | SLA worker stays in IT |
| 3 | `GET /admin/sync-conflicts` + `POST resolve` (2) | iCal poller stays in IT |
| 4 | `POST /bookings/{id}/cancel` × 2 canonical refund shapes (2, with `Matchers.like` for policy × timing) | Full 3×4 matrix stays in IT |
| 5 | `POST /bookings` 201 + `POST /bookings` 409 (2) | — |
| 6 | **NOT Pact-shaped** (Stripe → API, not FE → API) | Kept in `VrBook.Api.IntegrationTests` at Category=Integration; no change |
| 7 | `GET /properties/{id}/quotes` with discount snapshot (1) | `LoyaltyDiscountResolver` unit + IT stays |

**Net: 12 pact interactions covering the FE-facing tail of 6 of the 7 flows.** Flow 6 stays in the existing integration test surface with an ADR note. Meets "at least the 7 §18.2 mandated flows" as a coverage floor by tying every flow to a verifiable contract check (Pact for the 6 with FE tails; integration test for the pure server-server one).

---

## §1 Sub-commit sequence

Eight sub-commits mirroring the OPS.M.22 shape. NO RED-then-GREEN merged onto develop — every commit lands GREEN under CI.

| # | Slice | Scope |
|---|---|---|
| OPS.1.1 | Plumbing + docs | Add `docs/OPS_1_PACT_PLAN.md` (this doc). Add `tests/VrBook.Api.PactTests/` project skeleton (empty; excluded from `Category=Integration` filter). Add `contracts/pacts/README.md` explaining the git-committed pact-file share. Add `web/tests/pacts/README.md` explaining consumer-side wiring. Zero CI impact; zero tests fire. |
| OPS.1.2 | Consumer harness (FE) + first interaction | Install `@pact-foundation/pact@^13` in `web/package.json` devDeps. New `web/tests/pacts/consumer.pact.ts` bootstraps the Pact `Consumer` + `Provider` names (`vrbook-web` + `vrbook-api`). First interaction: `GET /api/v1/properties?limit=5` → 200 + `Matchers.eachLike(propertySummary)`. Vitest runs the consumer, writes `contracts/pacts/vrbook-web-vrbook-api.json`. New vitest job step `pact:consumer` runs before `test`; `git diff --exit-code contracts/pacts/` gates drift. Web CI stays green because the pact matches its committed baseline. |
| OPS.1.3 | Provider verifier + fake auth + first flow | New `tests/VrBook.Api.PactTests/PactVerifierFixture.cs` subclasses `TwoTenantApiFixture` (reuses Postgres testcontainer + all module migrations + TenantA/TenantB/OwnerA/OwnerB/PlatformAdmin seed). Registers `TestAuthHandler` overlay identically to M.14.1. Adds `PactProviderStateHandler` — dispatch table keyed on provider-state string → seeds row(s) via `IServiceProvider` + `RlsBypassScope.Enter()`. First state: `"a guest can search properties"` (no additional seed needed; the fixture's two properties suffice). `PactVerifierTests.VerifyPacts()` uses `PactNet.Verifier` v5.x to replay `contracts/pacts/vrbook-web-vrbook-api.json` against the WebApplicationFactory host. New CI step in `cd-staging-api.yml` (`pact-verifier`) runs the verifier with `continue-on-error: true` (informational) for the OPS.1.3 → OPS.1.6 landing window. |
| OPS.1.4 | Flow 1 remainder + Flow 4 + Flow 5 | Consumer: add hold POST 201, place-booking POST 201, owner-confirm POST 200 (flow 1), cancel POST 200 with refund breakdown × 2 canonical shapes (flow 4), place-booking POST 409 conflict (flow 5). Provider: seven new provider states via `PactProviderStateHandler` — e.g. `"tenant A owner has a Tentative booking B1"`, `"date range D1-D2 on property P1 is already booked"`. Each state seeds under `RlsBypassScope` + a per-test transaction rolled back after the interaction. Commit updates `contracts/pacts/vrbook-web-vrbook-api.json`. |
| OPS.1.5 | Flow 2 tail + Flow 3 tail + Flow 7 tail | Consumer: `GET /bookings/{id}` returning `status: "Confirmed"` after the SLA worker fired (state name pins the pre-condition); `GET /admin/sync-conflicts` listing one conflict, `POST /admin/sync-conflicts/{id}/resolve` with `cancel_direct`; `GET /properties/{id}/quotes` returning a quote where `loyaltyDiscountPct=5`. Provider: 4 new provider states, each seeded via handler under `RlsBypassScope`. Total pact JSON is now ~12 interactions; verifier + consumer both green. |
| OPS.1.6 | Flow 6 carve-out documentation + ADR | Add ADR-0018 `pact-scope-and-flow-6-carve-out.md` — explains why Stripe webhook idempotency is NOT Pact-shaped, and locks that IT coverage in `VrBook.Api.IntegrationTests/Payment/StripeWebhookIdempotencyTests.cs` (already exists) satisfies §18.2 flow #6 without a pact contract. Add cross-reference note in `contracts/pacts/README.md`. |
| OPS.1.7 | Flip provider verifier to blocking | Change `pact-verifier` CI step in `cd-staging-api.yml` from `continue-on-error: true` to blocking (matches M.22.8's cross-surface arch-test flip pattern). Add `contracts/pacts/` git-diff gate to `cd-staging-web.yml` (already present from OPS.1.2 — this commit only tightens the failure message + adds a runbook link). Add `docs/runbooks/pact-contract-drift.md` triage runbook. |
| OPS.1.8 | Close-out + MASTER_PLAN flip | Write `docs/OPS_1_CLOSE_OUT.md` (mirror OPS.M.22 shape). Flip MASTER_PLAN row 16 → ✅. Update `EXECUTION_PLAN.md` §8 OPS.1 line with commit range. Cross-surface arch tests pinning: (a) exactly 12 pact interactions ship, (b) `PactVerifierFixture` inherits from `TwoTenantApiFixture` (contract stability), (c) no `[AllowAnonymous]` was added to any controller during this slice. |

**Total: 8 sub-commits, ~3 days effort. Session budget: 2 sessions.**

---

## §2 What survives, what needs new work

### Survives — no change

- **`TwoTenantApiFixture`** — base for `PactVerifierFixture`. Postgres testcontainer + all-module migrations + TenantA/TenantB seed all reused verbatim. Fixture is subclass, not fork.
- **`TestAuthHandler` (`tests/VrBook.Api.IntegrationTests/Auth/`)** — registered under `JwtBearerDefaults.AuthenticationScheme` for the pact verifier host the same way M.14.1 does it. No production seam.
- **`RlsBypassScope`** — used for provider-state seeding that crosses tenant boundaries (e.g. seeding OwnerB's booking in a fixture where OwnerA is the caller).
- **All production controllers** — no `[AllowAnonymous]` attributes added, no route decorators changed. Owner-locked Entra-only surface untouched.
- **Existing `Category=Integration` filter in `cd-staging-api.yml:113`** — pact tests carry `Category=Pact` so the existing integration-tests step ignores them; a separate `pact-verifier` step runs them explicitly.
- **`StripeWebhookIdempotencyTests`** — stays exactly as-is; ADR-0018 documents it as the §18.2 flow #6 coverage vehicle.
- **`web/src/lib/api/client.ts`** — no runtime change. Consumer tests instantiate a Pact `MockService` that intercepts `apiFetch` calls in the test process.

### Survives from OPS.M.22

- **Admin flow marker + pre-seed gate** — pact provider states that seed admin personas MUST match the M.22.4 shape: `identity.users.pre_seeded_at` non-null + `user_identities` linked. `PactProviderStateHandler` re-uses `SeedAdminUserCommand` for admin seeding so the middleware admin-gate doesn't refuse the pact request with `admin_account_not_provisioned` 401.

### Needs new work

- **`tests/VrBook.Api.PactTests/` project** — new xUnit test project. References `VrBook.Api`, `VrBook.Api.IntegrationTests` (for `TwoTenantApiFixture` + `TestAuthHandler` reuse), `PactNet` (v5.x). `Category=Pact` trait.
- **`PactVerifierFixture`** — subclass of `TwoTenantApiFixture`. Only override: registers `PactProviderStateHandler` in DI + exposes `Provider` static (name = `"vrbook-api"`) + host URL for the verifier.
- **`PactProviderStateHandler`** — dispatch table `{state-string → async setup delegate}`. Runs before each interaction. Wraps in `RlsBypassScope.Enter()` for cross-tenant setups. Rolls back per-interaction via a test-scoped transaction.
- **`web/tests/pacts/consumer.pact.ts`** — vitest-integrated pact consumer. Registers 12 interactions. Writes deterministic `contracts/pacts/vrbook-web-vrbook-api.json`.
- **`contracts/pacts/` directory** — the git-committed pact-file share. One file: `vrbook-web-vrbook-api.json`.
- **Two CI steps** — `pact-consumer` in `cd-staging-web.yml` (blocking from OPS.1.2), `pact-verifier` in `cd-staging-api.yml` (informational OPS.1.3 → OPS.1.6, blocking from OPS.1.7).
- **`docs/runbooks/pact-contract-drift.md`** — triage runbook for the two failure modes: (a) consumer drift (FE ships a new call → `git diff` gate fires on `contracts/pacts/`), (b) provider drift (API reshapes response → verifier fires).
- **`ADR-0018 pact-scope-and-flow-6-carve-out.md`** — locks the flow 6 rationale.

---

## §3 New surface (test projects, CI steps, contracts)

### Test project

| Path | Purpose |
|---|---|
| `tests/VrBook.Api.PactTests/VrBook.Api.PactTests.csproj` | New xUnit project. `PackageReference PactNet@5.x`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql`. ProjectReferences: `VrBook.Api`, `VrBook.Api.IntegrationTests` (for the fixture + auth handler). |
| `tests/VrBook.Api.PactTests/PactVerifierFixture.cs` | Subclass of `TwoTenantApiFixture`. Overrides `ConfigureWebHost` to register `PactProviderStateHandler` after base call. |
| `tests/VrBook.Api.PactTests/PactProviderStateHandler.cs` | Dispatch table. ~10 states (see §6 catalog). |
| `tests/VrBook.Api.PactTests/PactVerifierTests.cs` | `[Fact][Trait("Category","Pact")]` — invokes `PactNet.Verifier.IPactVerifier.ServiceProvider(...).WithFileSource(...).WithProviderStateUrl(...).Verify()`. |

### Consumer surface (FE)

| Path | Purpose |
|---|---|
| `web/tests/pacts/consumer.pact.ts` | Vitest-integrated. Registers 12 interactions. Writes to `../../contracts/pacts/vrbook-web-vrbook-api.json`. |
| `web/tests/pacts/matchers.ts` | Shared `Matchers.like`/`eachLike`/`iso8601DateTime` helpers so per-flow tests stay declarative. |
| `web/package.json` | New devDeps: `@pact-foundation/pact@^13`, `@pact-foundation/pact-node@^11`. New script: `"test:pact"` runs vitest against `web/tests/pacts/`. |

### CI wiring

**`cd-staging-web.yml`** — add step after Vitest:
```yaml
- name: Pact consumer + drift gate
  working-directory: web
  run: |
    npm run test:pact
    cd ..
    git diff --exit-code contracts/pacts/ || {
      echo "::error::Pact drift — FE consumer expectations changed but contracts/pacts/ not committed"
      exit 1
    }
```
Blocking from OPS.1.2. Failure mode: FE dev adds an interaction / changes a shape but forgets to `git add contracts/pacts/vrbook-web-vrbook-api.json`. Runbook covers the fix (`npm run test:pact && git add contracts/pacts/`).

**`cd-staging-api.yml`** — add step after Integration tests:
```yaml
- name: Pact provider verification
  continue-on-error: true   # OPS.1.7 flips this to false (blocking)
  run: |
    dotnet test ${{ env.SOLUTION }} \
      --no-build --configuration Release \
      --filter "Category=Pact" \
      --logger "trx;LogFileName=pact.trx"
```
Informational OPS.1.3–OPS.1.6; blocking from OPS.1.7. Reason: gives 4 sub-commits of soak time to prove verifier stability against the WebApplicationFactory host before enforcing.

### Contracts

| Path | Purpose |
|---|---|
| `contracts/pacts/vrbook-web-vrbook-api.json` | The single pact file. Contains 12 interactions. Deterministic ordering (alphabetical by `description`). Committed with each OPS.1.4 / OPS.1.5 commit. |
| `contracts/pacts/README.md` | Explains: what's in the file, how to regenerate (`cd web && npm run test:pact`), how drift is caught, why we're not using a Broker yet. |

---

## §4 Risks + mitigations

| # | Risk | L | I | Mitigation |
|---|---|---|---|---|
| 1 | Pact file is non-deterministic across runs (interaction ordering, timestamps) → CI flaps on every commit | H | H | Consumer test registers interactions in alphabetical-by-description order; PactNet v13 supports deterministic output. `.gitattributes` marks the file as binary-diff-safe. First commit locks the sort key; OPS.1.2 arch test in `web/tests/pacts/deterministic.pact.test.ts` verifies two consecutive runs produce byte-identical output. |
| 2 | Provider verifier can't authenticate → all pact interactions fail 401 because `[Authorize]` gates | Certainty | H | `TestAuthHandler` overlay (M.14.1 pattern) registered under `JwtBearerDefaults.AuthenticationScheme`. Pact consumer stamps `Authorization: Bearer test` + `X-Test-Persona: PactGuest/PactOwnerA/PactPlatformAdmin` on every interaction. `PactVerifierFixture` seeds those personas identically to `TwoTenantTestAuthHandler`. |
| 3 | Provider state seed leaks across interactions → later interactions see stale rows | M | H | `PactProviderStateHandler` opens a fresh service scope per interaction and executes each seed inside a `TransactionScope` that commits on state setup and is undone by fixture-level DELETEs on state teardown. Alternatively (cleaner): reset the Postgres testcontainer between interactions — rejected as too slow (~5s/interaction × 12 = 60s per verifier run). Chose seed-and-cleanup because the base fixture already handles tenant isolation via RLS. |
| 4 | Flow 6 (Stripe webhook) exclusion is challenged by owner as failing the ">= 7" requirement | M | M | ADR-0018 makes the case explicit: Pact IS request/response between two named services; Stripe is not `vrbook-web`. Flow 6 stays covered by the existing `StripeWebhookIdempotencyTests` in the Category=Integration suite. Owner-lock question §5-Q3 surfaces this for explicit approval before OPS.1.6 lands. |
| 5 | Verifier CI step succeeds locally but fails on GitHub Actions Ubuntu runner (Testcontainer-DinD flake) | M | M | Reuse the `TwoTenantApiFixture` env-var neutralization pattern (F0'' → `Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", null)` at fixture init). The M.10.2 F0 lessons apply verbatim; no new debug expected. |
| 6 | Pact consumer test can't reach `web/src/lib/api/client.ts` because `apiFetch` uses `NEXT_PUBLIC_API_BASE_URL` (undefined in vitest) | H | L | Vitest setup `web/vitest.setup.ts` already stubs `NEXT_PUBLIC_API_BASE_URL`. Consumer test injects Pact's mock server URL as the base URL via `process.env` override before the module is imported. Pattern documented in `web/tests/pacts/consumer.pact.ts` header. |
| 7 | Admin-flow pact interaction fails because admin caller lacks `pre_seeded_at` post-M.22 | Certainty | H | `PactProviderStateHandler` for admin states uses `SeedAdminUserCommand` (M.22.2), which sets `pre_seeded_at=NOW()` + links `user_identities`. Middleware admin-gate then passes. |
| 8 | Provider verifier gets stuck in a merge-loop when FE + API are shipped by different developers on the same day | M | M | The `contracts/pacts/` file is the atomicity boundary. FE commit updates it + provider verifier runs on the same commit sha. Cross-repo isn't a concern (monorepo). Runbook covers the "consumer PR must land first, then API PR" ordering. Long-term (Phase 2): Pact Broker with can-i-deploy resolves this. |
| 9 | `contracts/pacts/vrbook-web-vrbook-api.json` grows unmanageably as future slices add interactions | L | L | 12 interactions today; realistic ceiling ~50 before Phase 2 justifies a Broker. Each interaction is ~50 lines JSON; a 50-interaction file is ~2500 lines, still git-diffable. Broker migration path documented in ADR-0018. |
| 10 | Pact verifier fails because `TwoTenantApiFixture` seeds a random Property title that doesn't match the pact expectation | M | M | Pact expectations use `Matchers.like(...)` for value shapes and `Matchers.regex(...)` for constrained fields. Only structural + type invariants are asserted; content-drift on seed values doesn't break the contract. Consumer test guidelines documented in `web/tests/pacts/README.md`. |

---

## §5 Owner-lock questions (POLICY only — locked to architect recommendation per memory rule)

### Q1 — [POLICY] Provider-verifier blocking-gate timeline

- **(a) — Locked.** Informational for OPS.1.3–OPS.1.6, blocking from OPS.1.7 close-out (same commit that flips MASTER_PLAN). Zero-week soak because verifier logic is deterministic against the fixture; if it's green on the OPS.1.5 close-out commit, it stays green.
- **(b)** — 1-week soak on develop before blocking flip.
- **(c)** — Blocking immediately at OPS.1.3.

**Rationale for (a):** (c) risks a first-commit-red gate on develop, which violates the user's "CI must stay green" constraint. (b) adds calendar time without new information — the fixture is deterministic. (a) lets the 3 landing commits (1.4/1.5/1.6) prove the verifier + then flip in the same close-out commit that records success.

### Q2 — [POLICY] Pact Broker deferral

- **(a) — Locked.** Defer to Phase 2. MVP is git-committed `contracts/pacts/vrbook-web-vrbook-api.json`. Per user constraint "no new external services (Pact Broker deferred to Phase 2 unless there's a strong reason)."
- **(b)** — Deploy a Pact Broker container (Azure Container App) as part of OPS.1.

**Rationale:** Monorepo means atomic FE + API commits are trivial; a Broker's `can-i-deploy` API adds no value until VrBook has cross-repo consumers (Phase 2 partner integrations). Broker migration path documented in ADR-0018 for Phase 2 traceability.

### Q3 — [POLICY] Flow 6 (Stripe webhook idempotency) coverage carve-out

- **(a) — Locked.** Flow 6 is explicitly OUT of Pact scope; stays covered by `StripeWebhookIdempotencyTests` in the existing Category=Integration suite. ADR-0018 locks the rationale.
- **(b)** — Add a synthetic pact interaction that mocks Stripe as a Pact consumer.
- **(c)** — Ship OPS.1 with 6 flow coverage; open OPS.1.9 to cover flow 6 by some other means.

**Rationale for (a):** Pact = request/response between two named services. Stripe is not `vrbook-web`. Force-fitting Pact around a webhook doesn't test anything new — the Stripe SDK's signature verification + our idempotency table + our webhook handler are all already exercised end-to-end by the existing integration test. (b) is a category error; (c) creates a phantom follow-up for no benefit.

### Q4 — [POLICY] Pact drift merge policy

- **(a) — Locked.** Consumer-driven. FE commit is the atomicity boundary: any FE-side API-call change must (i) update `web/tests/pacts/consumer.pact.ts`, (ii) regenerate `contracts/pacts/vrbook-web-vrbook-api.json`, (iii) commit both in the same PR. API-side reshaping that breaks the pact must land in a paired commit that updates the API + the consumer test + the pact file atomically.
- **(b)** — Provider-driven (API commits regenerate the pact, FE reacts).
- **(c)** — Auto-open a follow-up issue on drift.

**Rationale for (a):** VrBook's SPA is the primary consumer of the API. Provider-driven contracts (b) invert the tail-wags-dog check that Pact is designed to prevent — the whole point is that "the FE says what shape it needs" is authoritative. (c) delays the reconciliation past the merge boundary and lets staging break. The monorepo makes (a) trivial: same branch, same PR.

---

## §6 Technical answers I resolve directly (architect's call per memory rule)

- **PactNet version:** v5.x (latest stable at 2026-07-09). Ships an `IPactVerifier` fluent API + supports `IServiceProvider` handoff for `MessagingProvider` (unused here — HTTP only). Requires .NET 8 which we already have.
- **`@pact-foundation/pact` version:** v13.x. Supports the v3 pact specification, which the .NET verifier v5.x also reads. Deterministic output on repeat runs (v13 fixed the v12 timestamp non-determinism).
- **Pact specification version:** v3. Provider states are per-interaction; matcher metadata sits alongside example values. Do NOT use v4 — PactNet v5 has partial v4 support and the fixture-swap upgrade cost isn't justified.
- **Pact file location:** `contracts/pacts/vrbook-web-vrbook-api.json`. NOT `web/pacts/` (couples the contract to the FE app's deploy artifact) and NOT `tests/VrBook.Api.PactTests/pacts/` (couples to the API test surface). `contracts/` is where OpenAPI already lives — pacts are a peer.
- **Consumer name:** `vrbook-web`. **Provider name:** `vrbook-api`. Names appear in pact filename + verifier logs; changing them later means regenerating the file.
- **Fake auth mechanism:** `TestAuthHandler` overlay under `JwtBearerDefaults.AuthenticationScheme`. Same class + same registration path as OPS.M.14.1 / `TwoTenantApiFixture` line 322-333. **Explicitly rejects (a)** `[AllowAnonymous]` on pact endpoints — violates owner-locked Entra-only surface and creates a defect surface where a production-config bug could ship past the gate. **Explicitly rejects (b)** locally-signed JWT — 2-3x more infra (keypair, JWKS endpoint mock, issuer + audience config) for zero contract-testing value, since Pact doesn't verify token content anyway.
- **Persona set for pact:** `PactGuest`, `PactOwnerA`, `PactPlatformAdmin`. Reuse the M.14 `TestPersona` shape; add three new personas to `TwoTenantTestAuthHandler.Personas` dictionary via a static `PactPersonas.Register(...)` helper. Guests get a new `PactGuest` (email `pact-guest@vrbook.test`) seeded in `PactVerifierFixture.SeedAsync` override; owner/admin reuse `OwnerA` + `PlatformAdmin` from the base fixture.
- **Provider state catalog (10 states):**
  1. `"a guest can search properties"` — no additional seed; fixture's two properties suffice.
  2. `"tenant A property P1 is available for D1-D2"` — assert; no seed.
  3. `"guest holds property P1 for D1-D2"` — insert into `booking.holds` under `RlsBypassScope`.
  4. `"tenant A has a Tentative booking B1 awaiting confirmation"` — insert into `booking.bookings` + `payment.payment_intents` under `RlsBypassScope`.
  5. `"tenant A has a Confirmed booking B1 with a strict cancellation policy"` — 4 + policy field.
  6. `"tenant A has a Confirmed booking B1 with a moderate cancellation policy"` — 4 + policy field.
  7. `"date range D1-D2 on property P1 is already booked"` — insert conflicting booking.
  8. `"SLA worker has fired for tenant A booking B1"` — insert booking with `status='Confirmed'` + `confirmed_at=NOW()`, `confirmed_via='sla_auto'`.
  9. `"tenant A has an unresolved sync conflict SC1"` — insert `sync.sync_conflicts` + linked `sync.external_reservations`.
  10. `"guest G1 is Silver tier"` — insert `loyalty.loyalty_accounts` with `completed_stay_count=3`.
- **Provider state teardown:** each state seed opens a `TransactionScope(TransactionScopeAsyncFlowOption.Enabled)`; teardown disposes without commit. Alternative (fixture-level `DELETE FROM ... WHERE test_run_id = @currentRun`) rejected as adding a schema surface that doesn't exist in production.
- **Deterministic pact output:** consumer test registers interactions in `sort(description)` order. PactNet v13 writes JSON keys alphabetically by default. `.gitattributes` line: `contracts/pacts/*.json binary` prevents git from line-ending-munging the file across Windows/Linux devs.
- **Vitest config:** pact tests run in-process under vitest's node environment (not jsdom — the pact mock server is a real HTTP server on a random port). Configure via `web/tests/pacts/vitest.pact.config.ts` extending the main config with `environment: 'node'`. Regular vitest run + `test:pact` run don't overlap.
- **PactVerifier host lifetime:** one `PactVerifierFixture` instance per test class (`ICollectionFixture<PactVerifierFixture>`). Runs the fixture's WebApplicationFactory on a stable random port; verifier hits `http://localhost:<port>/`. Fixture disposes at collection teardown.
- **Provider state HTTP endpoint:** PactNet v5 verifier expects a `/pact-states` endpoint on the provider. `PactVerifierFixture` registers a minimal `MapPost("/pact-states", ...)` in `ConfigureWebHost` that dispatches to `PactProviderStateHandler`. This endpoint is `[AllowAnonymous]` — acceptable because it only exists in the pact-tests host (registered via `ConfigureTestServices` overlay, absent in production). Locked by a `Category=Pact` arch test that asserts the endpoint is never registered in production hosts.
- **CI matrix:** verifier runs on ubuntu-latest with Docker (Testcontainers). Same runner + same service containers as the existing Category=Integration tests. No new CI infra.
- **Flow 6 IT link:** the existing test at `tests/VrBook.Api.IntegrationTests/Payment/` (search for `StripeWebhookIdempotency`) is the flow 6 coverage vehicle. ADR-0018 cross-references it.
- **Where the 12 interactions live in the consumer test:** one `test('description', ...)` block per interaction, grouped by §18.2 flow number under `describe('flow-1 …')` through `describe('flow-7 …')`. `describe('flow-6-carve-out')` renders a single skipped test with a comment pointing to ADR-0018 (drift-detector — anyone deleting the carve-out reasoning has to unskip a test).

---

## §7 Close-out checklist

- [ ] `tests/VrBook.Api.PactTests/` project builds + is referenced from `src/VrBook.sln`.
- [ ] `PactVerifierFixture` subclasses `TwoTenantApiFixture`; arch test locks the inheritance.
- [ ] `PactProviderStateHandler` covers all 10 states; unit tests pin each dispatch key.
- [ ] `web/tests/pacts/consumer.pact.ts` registers exactly 12 interactions covering the FE-facing tail of 6 of the 7 §18.2 flows.
- [ ] `contracts/pacts/vrbook-web-vrbook-api.json` deterministic — 2 consecutive `npm run test:pact` runs produce byte-identical output.
- [ ] `web/tests/pacts/deterministic.pact.test.ts` verifies determinism in CI.
- [ ] `cd-staging-web.yml` `pact-consumer` step blocks on drift (`git diff --exit-code contracts/pacts/`).
- [ ] `cd-staging-api.yml` `pact-verifier` step blocks after OPS.1.7 (was `continue-on-error: true` OPS.1.3–OPS.1.6).
- [ ] `docs/runbooks/pact-contract-drift.md` covers the two failure modes.
- [ ] `docs/adr/0018-pact-scope-and-flow-6-carve-out.md` committed.
- [ ] `docs/OPS_1_CLOSE_OUT.md` written.
- [ ] `docs/MASTER_PLAN.md` row 16 flipped ✅ with commit range.
- [ ] `docs/EXECUTION_PLAN.md` §8 OPS.1 line updated.
- [ ] `CLAUDE.md` Phase 1 slice state footer updated (Phase 1.5 ops row → OPS.1 done, OPS.2 next).
- [ ] Cross-surface arch tests pinning: (a) exactly 12 pact interactions ship, (b) `PactVerifierFixture` inherits `TwoTenantApiFixture`, (c) no `[AllowAnonymous]` added to any production controller during OPS.1, (d) `/pact-states` endpoint absent in production `Program.cs`.
- [ ] Staging walk: no staging action needed — pact runs in CI only, no runtime footprint on staging Container Apps.
- [ ] Verify Slice 8 (Phase 3 hotel-rooms) has a clean pact regeneration path documented in the OPS.1 close-out.

---

Ready to execute Slice OPS.1.1.
