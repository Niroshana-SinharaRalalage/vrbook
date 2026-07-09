# web/tests/pacts/ — consumer-side Pact tests

Slice OPS.1 (`docs/OPS_1_PACT_PLAN.md`) lands the SPA's contract-consumer expectations
for the `.NET` API. This directory holds the vitest-integrated Pact consumer tests
that generate `contracts/pacts/vrbook-web-vrbook-api.json`.

## What lands here

| File | Lands in | Purpose |
|---|---|---|
| `README.md` | OPS.1.1 | This file. |
| `consumer.pact.ts` | OPS.1.2 | The consumer test. Registers all 12 interactions, one `test()` block per interaction, grouped under `describe('flow-N …')` blocks per §18.2 flow. |
| `matchers.ts` | OPS.1.2 | Shared `Matchers.like` / `eachLike` / `iso8601DateTime` helpers so per-flow tests stay declarative. |
| `vitest.pact.config.ts` | OPS.1.2 | Vitest config overriding `environment: 'node'` — the Pact mock server is a real HTTP server, not a jsdom stub. |
| `deterministic.pact.test.ts` | OPS.1.2 | Sanity test that runs the consumer twice and asserts byte-identical output — catches CI-flap-inducing non-determinism. |

## Running locally

```bash
cd web
npm run test:pact
```

Regenerates `../../contracts/pacts/vrbook-web-vrbook-api.json`. If the file changes,
commit both `web/tests/pacts/consumer.pact.ts` (your change) and the regenerated pact
file in the SAME commit — CI blocks on drift (`git diff --exit-code contracts/pacts/`).

## Why vitest and not a separate test framework

Vitest is already the SPA's test runner; adding a second runner (jest / mocha) would
double the CI matrix + double the local-dev cost. Pact's node bindings
(`@pact-foundation/pact@13`) integrate cleanly with vitest via a per-file
`vitest.pact.config.ts`.

## Env-var overlay

`web/vitest.setup.ts` stubs `NEXT_PUBLIC_API_BASE_URL` for the regular vitest run. The
consumer test overrides it inside each `test()` block to point at the Pact mock
server's random port. This means `web/src/lib/api/client.ts`'s `apiFetch` calls get
intercepted by the mock without any production code change.

## What is NOT tested here

- **Rendering / component behaviour** — that's the existing vitest suite next to each
  component.
- **Cross-page navigation flows** — that's Slice OPS.2 (Playwright E2E).
- **Load / performance** — Slice OPS.3 (k6).
- **Auth token content** — Pact doesn't verify token payloads; that's for integration
  tests + the M.12 admin/social gate tests.

Contract testing here is ONLY about request shape → response shape. Pact answers "if
the SPA sends this exact JSON to `/api/v1/…` with these headers, does the API respond
with a shape matching these matchers?"

## References

- Plan: [`docs/OPS_1_PACT_PLAN.md`](../../../docs/OPS_1_PACT_PLAN.md)
- Pact file location + regen: [`contracts/pacts/README.md`](../../../contracts/pacts/README.md)
- Provider verifier: `tests/VrBook.Api.PactTests/`
- Drift triage: `docs/runbooks/pact-contract-drift.md` (lands OPS.1.7)
