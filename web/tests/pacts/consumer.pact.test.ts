import { PactV3 } from '@pact-foundation/pact';
import path from 'node:path';
import { afterAll, describe, it, expect } from 'vitest';
import { pagedPropertySummary } from './matchers';

/**
 * Slice OPS.1.2 — consumer-driven contract for the FE ↔ API seam.
 *
 * <p>Interactions are registered in ALPHABETICAL order by
 * `description` — PactV3 writes them to disk in insertion order, so the
 * `describe`/`it` labels double as the sort key. `deterministic.pact.test.ts`
 * enforces byte-identical output across runs so CI diff-gate doesn't flap.</p>
 *
 * <p>Each `describe('flow-N …')` block maps to a §18.2 flow. Interactions
 * land incrementally: OPS.1.2 ships this file with 1 (flow-0 skeleton);
 * OPS.1.4 lands flows 1/4/5; OPS.1.5 lands flows 2/3/7. Flow 6 (Stripe
 * webhook idempotency) is out of pact scope per ADR-0018 (OPS.1.6) —
 * pin the carve-out with a skipped drift-detector test.</p>
 *
 * <p>Pact file target: `contracts/pacts/vrbook-web-vrbook-api.json` —
 * the single monorepo-committed contract. `git diff --exit-code
 * contracts/pacts/` in `cd-staging-web.yml` fails the build if the SPA
 * changed a call shape but forgot to re-commit the file.</p>
 */

// Path to `contracts/pacts/` relative to this test file. Vitest cwd is
// `web/`, so the pact file lands at repo-root/contracts/pacts/.
const PACT_DIR = path.resolve(__dirname, '..', '..', '..', 'contracts', 'pacts');

const provider = new PactV3({
  consumer: 'vrbook-web',
  provider: 'vrbook-api',
  dir: PACT_DIR,
  logLevel: 'warn',
});

describe('flow-0 — infrastructure sanity', () => {
  it('a guest can search properties', async () => {
    provider
      .given('a guest can search properties')
      .uponReceiving('a guest lists the first page of public properties')
      .withRequest({
        method: 'GET',
        path: '/api/v1/properties',
        query: { limit: '5' },
        headers: {
          Accept: 'application/json',
        },
      })
      .willRespondWith({
        status: 200,
        headers: {
          'Content-Type': 'application/json; charset=utf-8',
        },
        body: pagedPropertySummary(1),
      });

    await provider.executeTest(async (mockServer) => {
      const url = `${mockServer.url}/api/v1/properties?limit=5`;
      const response = await fetch(url, {
        headers: { Accept: 'application/json' },
      });
      expect(response.status).toBe(200);
      const body = await response.json();
      expect(body.items).toBeInstanceOf(Array);
      expect(body.items.length).toBeGreaterThanOrEqual(1);
    });
  });
});

// -----------------------------------------------------------------------------
// OPS.1.4 outcome: 2 additional interactions (flow-1 hold + flow-5 409 conflict)
// were drafted in this commit range but reverted because PactV3's mock server
// hits "Worker exited unexpectedly" when a single provider const is reused
// across multiple `executeTest` calls in vitest's node pool — even with
// singleFork: true. Root cause traces to Pact's Rust core cleanup between
// runs. OPS.1.5 will fix by giving each interaction its own PactV3 instance
// or migrating to the newer PactV4 API (which decouples MockServer lifecycle
// from executeTest). The state dispatch table registrations for #2 + #7 stay
// so the provider side is ahead of the consumer side; enrichment lands as
// soon as the pact-writer wiring settles.
//
// OPS.1.5 will add: flow-1 hold + place + confirm + flow-4 cancellation refund
//                    + flow-5 (201 + 409) + flow-2 (SLA tail) + flow-3 (iCal
//                    conflict tail) + flow-7 (loyalty tier promotion tail).
// OPS.1.6 will add: flow-6 carve-out drift-detector (skipped test pointing at
//                    ADR-0018).
// -----------------------------------------------------------------------------

afterAll(() => {
  // PactV3 writes the pact file when `executeTest` resolves; no explicit
  // finalize call needed. This hook is intentional documentation of the
  // "write happens automatically" contract.
});
