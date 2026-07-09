# 18. Pact contract scope — flow 6 (Stripe webhook) carved out to integration tests

- **Status**: Accepted
- **Date**: 2026-07-09 (owner-locked policy per OPS.1 plan §5-Q3 architect recommendation; owner directive 2026-07-09 adopted architect answers directly)
- **Deciders**: Solutions Architecture (OPS.1 planning consult), Owner (blanket adoption of architect §5 answers)
- **Tags**: testing, contract-testing, pact, stripe, launch-hardening

## Context

Slice OPS.1 ships consumer-driven contract tests between the Next.js SPA (`vrbook-web`) and the .NET API (`vrbook-api`) using Pact. The `BookingApp_Proposal.md` §18.2 "≥ 7 critical flows" requirement mandates coverage of:

1. End-to-end booking (search → hold → book → Stripe webhook → tentative → owner confirm → confirmed).
2. SLA auto-confirm.
3. iCal conflict resolution.
4. Cancellation refund calculation across policies × timing buckets.
5. Concurrent booking rejection (409).
6. **Stripe webhook idempotency** (same event delivered twice → state mutated once).
7. Loyalty tier promotion on `BookingCompleted`.

Pact is a **synchronous request/response contract test between two identifiable services** — a "consumer" that we control + a "provider" that we control. It records what the consumer sends and asserts the provider agrees.

Flow 6 has no such consumer we control. The caller is Stripe. Stripe is not a Pact consumer we can generate a pact file for; VrBook has no leverage over Stripe's request shape and no reason to. Forcing Pact around this flow is a category error.

## Decision

**Flow 6 is out of Pact scope.** It stays covered by the existing integration test at `tests/VrBook.Api.IntegrationTests/Payment/StripeWebhookIdempotencyTests.cs` (Category=Integration), which:

- Constructs a Stripe-signed webhook payload (production signature verification path).
- Sends it to `POST /api/v1/stripe/webhook` via the standard `WebApplicationFactory` client.
- Sends it AGAIN with the identical event id + payload.
- Asserts the DB state changed exactly once (idempotency table + downstream mutations both single-hit).

This satisfies §18.2 flow #6 as a verifiable behaviour check even though it doesn't produce a pact file. The coverage floor is "each flow has a CI-gated test somewhere," not "every flow gets a pact interaction."

## Consequences

### Positive

- **Pact stays honest**: the pact file describes what the SPA (`vrbook-web`) expects from the API (`vrbook-api`). No mocked-Stripe fictions in it.
- **Flow 6 coverage is stronger than a synthetic pact**: the integration test uses Stripe's real SDK to sign the payload + verify signature — no divergence between test and prod code paths.
- **Zero Broker/mock service infra**: adding Stripe as a synthetic pact consumer would require a Stripe-shape stub, a Broker to hold its pacts, and a verifier that runs both. All rejected in plan §5-Q2.
- **§18.2 coverage floor met**: all 7 flows have CI-gated tests (6 pact + 1 integration).

### Negative

- **Slight ceremony to explain**: readers of the pact file see 6 flows and might ask "where's the 7th?" This ADR + `contracts/pacts/README.md` (referencing this ADR) is the answer.
- **The consumer test file has a `describe('flow-6-carve-out')` block with a single skipped drift-detector test** (lands OPS.1.6) pointing at this ADR. Anyone deleting the skip has to also delete the block, which forces them to re-read this ADR.

### Neutral

- **Not a permanent decision**: if Stripe ever ships a first-party Pact SDK OR if VrBook grows an internal Stripe-emulator layer, the calculus changes. Nothing in this ADR forecloses that future.
- **No impact on Layer 1 (M.12) or admin-preseed (M.22) invariants**: this ADR is purely about the pact scope boundary for OPS.1.

## Alternatives considered

**A. Add a synthetic pact interaction that treats Stripe as a Pact consumer.**  
Rejected. Requires:
1. Generating a "Stripe" consumer pact file we mock ourselves — a category error (we don't control Stripe's shape).
2. Maintaining that fake shape as Stripe's real API evolves — testing our own mock, not Stripe.
3. Loading it into the Broker (Broker deferred per plan §5-Q2).

The existing `StripeWebhookIdempotencyTests` uses `Stripe.EventUtility.ConstructEvent(...)` with a locally-signed payload — that's the production signature verification path exercised end-to-end. No abstraction is missing.

**B. Ship OPS.1 with 6 flow coverage; open OPS.1.9 to cover flow 6 by some other means.**  
Rejected. Creates a phantom follow-up. `StripeWebhookIdempotencyTests` is the "other means." Coverage exists TODAY.

**C. Rewrite `StripeWebhookIdempotencyTests` as a pact test with a hand-rolled provider state that fires the endpoint twice.**  
Rejected. Pact's contract is "consumer says this shape, provider verifies it." A test that hits the same endpoint twice is not that shape. Pact is the wrong tool.

## References

- [`OPS_1_PACT_PLAN.md`](../OPS_1_PACT_PLAN.md) §5-Q3 — owner-locked answer.
- [`contracts/pacts/README.md`](../../contracts/pacts/README.md) — cross-references this ADR from the pact file location.
- `tests/VrBook.Api.IntegrationTests/Payment/StripeWebhookIdempotencyTests.cs` — the flow 6 coverage vehicle.
- [`OPS_M_5_PLAN.md`](../OPS_M_5_PLAN.md) — Stripe Connect Express slice; the webhook path this test exercises.
- Pact docs — <https://docs.pact.io/getting_started/what_is_pact> (consumer-driven contract testing definition).
