# contracts/pacts/ — SPA↔API contract files

Slice OPS.1 (`docs/OPS_1_PACT_PLAN.md`) delivers consumer-driven contract tests between
the Next.js SPA (`vrbook-web`) and the .NET 8 API (`vrbook-api`) using
[Pact](https://docs.pact.io/) v3 specification.

## What lives here

- **`vrbook-web-vrbook-api.json`** (lands in OPS.1.2) — the single pact file. Contains
  the 12 interactions the SPA expects the API to honour, covering the FE-facing tail
  of the 7 `BookingApp_Proposal.md` §18.2 flows. Only Stripe-webhook flow #6 is out of
  scope for Pact (see ADR-0018 — Stripe is not a Pact consumer we control).

## How to regenerate

```bash
cd web
npm run test:pact
```

The vitest-integrated consumer test writes this directory. `git diff --exit-code
contracts/pacts/` is a CI gate in `cd-staging-web.yml`; if the file is not up to date,
CI fails with a link to `docs/runbooks/pact-contract-drift.md`.

## How the provider verifies

The `VrBook.Api.PactTests` xUnit project (`tests/VrBook.Api.PactTests/`) runs a
`PactVerifierFixture` (subclass of `TwoTenantApiFixture`) which spins up a
`WebApplicationFactory` on a random port and replays every interaction in this file
against the real API host. Provider states (e.g. `"tenant A owner has a Tentative
booking B1 awaiting confirmation"`) are dispatched to `PactProviderStateHandler` before
each interaction. Fake auth is via the M.14 `TestAuthHandler` — no production auth
surface is loosened.

## Why we don't use a Pact Broker (yet)

Locked in `OPS_1_PACT_PLAN.md` §5-Q2: VrBook is a monorepo. FE + API commits are
already atomic, so the Broker's `can-i-deploy` API adds no value until Phase 2 partner
integrations bring cross-repo consumers. This directory is the MVP — one file, git-
committed, always up-to-date with `main`. Broker migration path is documented in
ADR-0018 for Phase 2 traceability.

## Format notes

- Pact spec version: **v3**. PactNet 5.x + `@pact-foundation/pact` 13.x both write and
  read v3. Do NOT upgrade to v4 without also upgrading PactNet.
- Interactions are sorted **alphabetically by `description`** so `npm run test:pact`
  produces byte-identical output across runs.
- `.gitattributes` marks `*.json` under this directory as `binary` so git doesn't
  line-ending-munge the file across Windows/Linux developers.

## Runbook

Drift triage: [`docs/runbooks/pact-contract-drift.md`](../../docs/runbooks/pact-contract-drift.md) (lands in OPS.1.7).
