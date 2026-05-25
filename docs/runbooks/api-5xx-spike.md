# Runbook — API 5xx spike

> Alert: API 5xx rate > 1% over 10 min. Severity: **Sev2** → on-call.

## First 5 minutes

1. Confirm in App Insights `requests` chart — is it a single endpoint or system-wide?
2. Check Container App revision history. Recent deploy? → rollback.
3. Check Postgres + Redis status (dependencies in App Insights).

## Diagnostic queries

```kusto
requests
| where timestamp > ago(15m) and resultCode startswith "5"
| summarize count() by tostring(customDimensions.endpoint), bin(timestamp, 1m)
| render timechart
```

## Likely causes & fixes

- **Bad deploy** → rollback revision (`az containerapp revision activate --revision <prev>`).
- **Postgres saturated** → see [`postgres-cpu-high.md`](./postgres-cpu-high.md).
- **Redis evictions** → see [`redis-evictions.md`](./redis-evictions.md).
- **Stripe upstream** → check Stripe status page.

## Escalation

- Unresolved in 15 min → engineering lead.
- Confirmed data corruption → freeze deploys, engage Lead immediately.
