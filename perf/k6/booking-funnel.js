import http from 'k6/http';
import { check, sleep } from 'k6';

// Slice OPS.3 — booking-funnel load test. Exercises the highest-traffic public
// (anonymous) path — search → property detail → price quote — at a sustained
// 50 RPS for 5 minutes with P95 < 1s, per BookingApp_Proposal §18 / EXECUTION
// §8 OPS.3. Anonymous by design: it needs no MSAL token and hits the same
// read-heavy handlers a real guest's browse-and-quote loop drives.
//
// IMPORTANT (OPS_LAUNCH_COMPLETION_PLAN §2/§3): run against a PROD-SIZED target,
// NOT the B1ms-burstable + scale-to-zero staging (OPS.INFRA.2) — that would fail
// or mislead. Point BASE_URL at prod (pre-cutover) or a temporarily-upsized
// staging (General Purpose PG, minReplicas>=1), and record the target sizing in
// the evidence.
//
// Usage:
//   k6 run -e BASE_URL=https://<api-fqdn> \
//          -e PROPERTY_SLUG=e2e-smoke-property \
//          -e PROPERTY_ID=e2e00000-0000-0000-0000-000000000001 \
//          perf/k6/booking-funnel.js

const BASE_URL = (__ENV.BASE_URL || '').replace(/\/$/, '');
const PROPERTY_SLUG = __ENV.PROPERTY_SLUG || 'e2e-smoke-property';
const PROPERTY_ID = __ENV.PROPERTY_ID || 'e2e00000-0000-0000-0000-000000000001';

export const options = {
  scenarios: {
    booking_funnel: {
      executor: 'constant-arrival-rate',
      rate: 50, // 50 iterations/sec ≈ 50 RPS of funnel loops
      timeUnit: '1s',
      duration: '5m',
      preAllocatedVUs: 100,
      maxVUs: 300,
    },
  },
  thresholds: {
    // The proposal's launch gate: P95 < 1s and <1% errors.
    http_req_duration: ['p(95)<1000'],
    http_req_failed: ['rate<0.01'],
  },
};

const jsonHeaders = { headers: { 'Content-Type': 'application/json' } };

export default function () {
  if (!BASE_URL) {
    throw new Error('BASE_URL env is required, e.g. -e BASE_URL=https://<api-fqdn>');
  }

  // 1) Search (SSR-backed anonymous list).
  const search = http.get(`${BASE_URL}/api/v1/properties?destination=beach`);
  check(search, { 'search 200': (r) => r.status === 200 });

  // 2) Property detail by slug.
  const detail = http.get(`${BASE_URL}/api/v1/properties/${PROPERTY_SLUG}`);
  check(detail, { 'detail 200': (r) => r.status === 200 });

  // 3) Anonymous price quote (far-future 2-night range; deterministic).
  const quoteBody = JSON.stringify({ checkin: '2030-06-10', checkout: '2030-06-12', guests: 2 });
  const quote = http.post(`${BASE_URL}/api/v1/properties/${PROPERTY_ID}/quotes`, quoteBody, jsonHeaders);
  check(quote, { 'quote 200': (r) => r.status === 200 });

  sleep(0.1);
}
