# Runbook — k6 booking-funnel load test (OPS.3)

One-time pre-launch load test. Gate: **50 RPS sustained 5 min, P95 < 1s, <1% errors** on the anonymous booking funnel (search → detail → quote). Compressed from a CI gate to a manual run per [`OPS_LAUNCH_COMPLETION_PLAN.md`](../OPS_LAUNCH_COMPLETION_PLAN.md) §2.

## ⚠️ Target sizing (non-negotiable)

Staging runs **B1ms Burstable Postgres + scale-to-zero** (OPS.INFRA.2). A run against it is **not representative** and will fail/mislead. Run against a **prod-sized target**:

- **Preferred:** the provisioned prod stack, before the Entra cutover flips real traffic to it.
- **Or:** temporarily upsize staging for the run — Postgres → General Purpose (e.g. `Standard_D2ds_v5`), API `minReplicas>=1` — via a Bicep param override, run, then revert. Record the exact sizing in the evidence.

## How to run

**CI (preferred):** Actions → `perf-k6` → *Run workflow* → set `base_url` to the prod-sized API FQDN (leave blank only for a throwaway staging shape check — it will warn). Download the `k6-summary` artifact.

**Local:**
```bash
k6 run -e BASE_URL=https://<prod-sized-api-fqdn> \
       -e PROPERTY_SLUG=e2e-smoke-property \
       -e PROPERTY_ID=e2e00000-0000-0000-0000-000000000001 \
       perf/k6/booking-funnel.js
```

The script (`perf/k6/booking-funnel.js`) drives the anonymous funnel at a 50 RPS constant arrival rate for 5 min with thresholds `http_req_duration p(95)<1000` + `http_req_failed rate<0.01`. It needs the seed property (`SeedE2EBackfill`) present on the target.

## Reading the result

- **Pass** = k6 exits 0 (both thresholds met). Attach `k6-summary.json` to the go-live checklist and record the target sizing.
- **Fail** = a threshold breached. Triage: DB CPU (`postgres-cpu-high` runbook), missing indexes, N+1 in the quote/search handlers, or under-provisioned replicas. Fix, re-run.

## Go-live gate

Go/No-Go: one archived passing run against a documented prod-sized target. Post-launch backlog: wire this as a nightly/pre-release job + App Insights P95 alerts (the compensating control while it's not CI-gated).
