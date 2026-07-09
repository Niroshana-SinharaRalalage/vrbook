# Slice OPS.1 — Close-Out

**Status:** shape-complete + verifier deferred (5 of 8 sub-commits landed 2026-07-09; 3 doc sub-commits close the slice with follow-ups filed).
**Slice plan:** [`OPS_1_PACT_PLAN.md`](OPS_1_PACT_PLAN.md).
**Predecessors:** [`OPS_M_22_CLOSE_OUT.md`](OPS_M_22_CLOSE_OUT.md) (shape template + admin-flow marker that pact provider states have to respect), OPS.M.10 Wave 2 (`TwoTenantApiFixture` — the pact-verifier fixture subclasses it), OPS.M.14 (`TestAuthHandler` — fake-auth overlay pact reuses).
**ADR:** [`0018-pact-scope-and-flow-6-carve-out.md`](adr/0018-pact-scope-and-flow-6-carve-out.md).

---

## 1. What shipped

Slice OPS.1 lands the **shape** of consumer-driven contract testing between the Next.js SPA (`vrbook-web`) and the .NET API (`vrbook-api`):

- Project skeleton + docs (`tests/VrBook.Api.PactTests/`, `contracts/pacts/README.md`, `web/tests/pacts/README.md`).
- Consumer harness (@pact-foundation/pact@13.2.0) with 1 working interaction (`GET /api/v1/properties?limit=5` — public, AllowAnonymous).
- CI drift gate on the web side (`git diff --exit-code contracts/pacts/` blocks pushes that update the SPA consumer expectations but forget to commit the regenerated pact file).
- Provider verifier scaffolding — `PactVerifierFixture` subclassing `TwoTenantApiFixture`, `PactProviderStateHandler` dispatch table (3 states registered), `POST /pact-states` endpoint via minimal APIs, PactNet 5.0.0 wiring.
- CI provider-verifier step (`cd-staging-api.yml` new `Pact provider verification` step with `continue-on-error: true` for OPS.1.3–OPS.1.6; blocking at OPS.1.7 per plan §5-Q1).
- ADR-0018 locking the flow 6 (Stripe webhook idempotency) carve-out.
- Runbook `docs/runbooks/pact-contract-drift.md` covering both failure modes.

### Sub-commit map

| # | Commit | Scope | Status |
|---|---|---|---|
| OPS.1.1 | `f8877f8` | Project skeleton + 2 READMEs | ✅ shipped |
| OPS.1.2 | `202c00a` | Consumer harness + first interaction + CI drift gate | ✅ shipped |
| OPS.1.3 | `3e7e7b9` | Provider verifier fixture + dispatch + informational CI step | ✅ shipped (verifier call skipped) |
| OPS.1.4 | `1f4034a` | Provider-state pre-registration + PactV3 mock-server issue documented | ✅ shipped (interactions deferred) |
| OPS.1.5 | — | WAF-Kestrel adapter + PactV3 refactor + 11 additional interactions | ⏭ deferred (follow-up **OPS.1.9**) |
| OPS.1.6 | (this range) | ADR-0018 flow 6 carve-out | ✅ shipped |
| OPS.1.7 | (this range) | Drift runbook | ✅ shipped |
| OPS.1.8 | (this range) | Close-out + MASTER_PLAN row flip | ✅ shipped |

---

## 2. Deviations from the plan

### 2.1 OPS.1.5 deferred to a follow-up slice (**OPS.1.9**)

Plan §1 promised: WAF-Kestrel adapter + provider-state seed for the base flow + verifier unskipped + flows 2/3/7 landing in a single sub-commit. Actual: two independent technical blockers surfaced during OPS.1.3/1.4:

1. **WAF vs Kestrel**: PactNet's verifier requires a real HTTP endpoint. `WebApplicationFactory` uses `TestServer` (in-process); binding Kestrel to a fixed port inside the fixture without duplicating `Program.cs`'s startup is non-trivial. Three candidate solutions (per OPS_1_PACT_PLAN §5-Q1 rationale — none prototyped yet):
    - (a) Force Kestrel via a `Program`-level hook.
    - (b) Run a duplicate host on a real port.
    - (c) Adopt PactNet's HttpClient adapter if v5 exposes one (uncertain).
2. **PactV3 mock-server lifecycle**: Sharing a single `PactV3` instance across multiple `executeTest` calls in vitest's node pool produces "Worker exited unexpectedly" — root cause in Pact's Rust core cleanup. Two fix candidates:
    - (i) Give each interaction its own `PactV3` instance + explicit `finalize()`.
    - (ii) Migrate to the newer PactV4 API (decouples MockServer lifecycle from `executeTest`).

Both blockers are engineering discovery, not policy questions. **OPS.1.9** (filed against MASTER_PLAN Slice OPS.M.23 candidates) will resolve.

### 2.2 Only 1 pact interaction lands instead of 12

Plan §0 promised 12 interactions. Actual: 1 (flow-0 skeleton). The additional 11 land in OPS.1.9 alongside the WAF-Kestrel + PactV3 refactors — bundling makes sense because each new interaction is a paired consumer + provider-state addition.

The one shipped interaction is real, deterministic, CI-verified, and drift-gated. The pattern is proven.

### 2.3 Provider verifier is informational (per plan) but never blocks anything

Because the actual verify call is `[Fact(Skip=...)]` in OPS.1.3, the `Pact provider verification` CI step passes trivially. Plan §5-Q1's OPS.1.7 blocking-flip target date changes to "after OPS.1.9 unskips the fact." OPS.1.7 in the current slice range ships the drift runbook but leaves the CI step at `continue-on-error: true`.

### 2.4 `TwoTenantApiFixture` unsealed

Plan §2 said "PactVerifierFixture subclasses TwoTenantApiFixture." Actual: base class was `sealed` when OPS.1.3 tried to inherit. Removed the `sealed` keyword + added a comment linking to the pact-tests reason. No behavioural change to the base class; only sub-classing surface added.

---

## 3. Follow-ups

- **OPS.1.9 — pact verifier live + 11 remaining interactions**: WAF-Kestrel adapter selected from candidates (a)/(b)/(c) + `VerifyPacts` unskipped + all flow-1/2/3/4/5/7 interactions authored. Blocking-flip of the `pact-verifier` CI step. Session budget: 1 session. Owner-lock questions surface here if the WAF-Kestrel choice implies auth-surface risk (candidate b duplicates the host).
- **OPS.M.23 candidates** (already tracked in MASTER_PLAN header) — Slice 6/7 polish items batch cleanly with OPS.1.9.

---

## 4. Rollback

OPS.1 is additive. Rollback path:

1. `git revert 1f4034a..b1f9c8e` — restores the pre-OPS.1 state.
2. Delete `contracts/pacts/vrbook-web-vrbook-api.json`.
3. `TwoTenantApiFixture` stays unsealed; no need to re-seal — future test projects benefit from subclassability.
4. `.github/workflows/cd-staging-{web,api}.yml` step removals happen automatically via the revert.

Verifier CI step is `continue-on-error: true` so a stale revert doesn't block CI. Blocking-flip only happens after OPS.1.9.

---

## 5. References

- [`OPS_1_PACT_PLAN.md`](OPS_1_PACT_PLAN.md) — pre-slice architect brief + owner-locked answers.
- [`adr/0018-pact-scope-and-flow-6-carve-out.md`](adr/0018-pact-scope-and-flow-6-carve-out.md) — flow 6 IT carve-out decision.
- [`runbooks/pact-contract-drift.md`](runbooks/pact-contract-drift.md) — triage runbook.
- `contracts/pacts/vrbook-web-vrbook-api.json` — the pact file (1 interaction, deterministic).
- `web/tests/pacts/consumer.pact.test.ts` — consumer.
- `tests/VrBook.Api.PactTests/PactVerifierFixture.cs` — provider verifier scaffold.
