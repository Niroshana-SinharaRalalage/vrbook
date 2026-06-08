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

| Runbook | Triggering alert | Sev | State (2026-06-08) | Owner |
|---|---|---|---|---|
| [`payment-webhook-failure.md`](./payment-webhook-failure.md) | `alert-vrbook-{env}-webhook-fail` Stripe 5xx burst | Sev2 | ✅ Live, JSON-Serilog KQL | A5 |
| [`api-5xx-spike.md`](./api-5xx-spike.md) | `alert-vrbook-{env}-5xx-spike` >1% over 10m | Sev2 | ✅ Live, JSON-Serilog KQL | A0 (infra) |
| [`postgres-cpu-high.md`](./postgres-cpu-high.md) | `alert-vrbook-{env}-postgres-cpu` >80% sustained 10m | Sev2 | ✅ Live | A0 (infra) |
| [`redis-evictions.md`](./redis-evictions.md) | `alert-vrbook-{env}-redis-evictions` >0 in 5m | Sev3 | ✅ Live (deployRedis) | A0 (infra) |
| [`booking-sla-worker-silent.md`](./booking-sla-worker-silent.md) | `alert-vrbook-{env}-sla-silent` 6h | Sev3 | 🚧 Placeholder alert disabled | A4.1 |
| [`sync-feed-stale.md`](./sync-feed-stale.md) | `alert-vrbook-{env}-sync-stale` last_success > 2x interval | Sev3 | 🚧 Placeholder alert disabled | A6 |
| [`sync-conflict-detected.md`](./sync-conflict-detected.md) | `SyncConflictDetected` event (no alert; admin dashboard) | Sev3 | 🚧 Awaiting A6 | A6 |
| [`stripe-dispute-opened.md`](./stripe-dispute-opened.md) | `charge.dispute.created` webhook | Sev3 | ✅ Live (manual evidence) | A5 |

(There is no Sev1 in §17.5 because Phase 1 has no Sev1-class subsystems — every
critical failure mode is Sev2 max.)

Runbook structure is enforced by review (proposal §17.5):
1. Symptom
2. First 5 minutes (containment / dampening before diagnosis)
3. Diagnostic queries — use `docs/observability/queries.kql` as the source of truth
4. Likely causes
5. Remediation steps
6. Escalation

Alerts that fire run KQL against `ContainerAppConsoleLogs_CL` (Serilog
CompactJsonFormatter — see EXECUTION_PLAN.md §4.2 A0.1.2) or `AppRequests`
(App Insights). Old runbook KQL referencing `requests`/`traces`/`customEvents`
table names is from the pre-A0.1 era; the live runbooks above have been
migrated.
