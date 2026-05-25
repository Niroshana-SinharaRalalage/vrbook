# Runbooks

One file per alert. Every runbook follows the structure prescribed in proposal §17.5:

```
1. Symptom
2. First 5 minutes  (containment / dampening before diagnosis)
3. Diagnostic queries (Kusto / KQL against App Insights + Log Analytics)
4. Likely causes
5. Remediation steps
6. Escalation
```

| Runbook | Triggering alert | Severity |
|---|---|---|
| [`payment-webhook-failure.md`](./payment-webhook-failure.md) | Stripe webhook 5xx burst (>5 in 5 min) | Sev2 |
| [`sync-feed-stale.md`](./sync-feed-stale.md) | `last_success_at < now - 2h` for any property | Sev3 |
| [`api-5xx-spike.md`](./api-5xx-spike.md) | API 5xx rate > 1% over 10 min | Sev2 |
| [`postgres-cpu-high.md`](./postgres-cpu-high.md) | Postgres CPU > 80% sustained 10 min | Sev2 |
| [`booking-sla-worker-silent.md`](./booking-sla-worker-silent.md) | No `BookingConfirmed` via SLA path in 6h | Sev3 |
| [`redis-evictions.md`](./redis-evictions.md) | Redis evictions > 0 in 5 min | Sev3 |
| [`stripe-dispute-opened.md`](./stripe-dispute-opened.md) | Stripe dispute webhook | Sev3 |
| [`sync-conflict-detected.md`](./sync-conflict-detected.md) | Conflict detected (per property) | Sev3 |

A0 ships only the stubs below — each runbook will be fleshed out by the owning agent
when the corresponding system goes live (A5 owns payments, A6 owns sync, etc.).
