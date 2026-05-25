# Runbook — Postgres CPU > 80% sustained 10 min

> Severity: **Sev2** → on-call.

## First 5 minutes

1. Azure portal → Postgres Flexible Server → Metrics → CPU percent.
2. Query Insights → top slow queries.
3. Active connections (`SELECT count(*) FROM pg_stat_activity WHERE state='active'`).

## Diagnostic queries

```sql
-- Top long-running queries
SELECT now() - query_start AS runtime, state, query
FROM pg_stat_activity
WHERE state != 'idle'
ORDER BY runtime DESC
LIMIT 20;

-- Locks
SELECT * FROM pg_locks WHERE NOT granted;
```

## Likely causes & fixes

- Missing index on a hot table (e.g., `bookings(property_id, checkin_date, checkout_date)`) → add index in next migration; consider emergency `CREATE INDEX CONCURRENTLY`.
- Long-running report query → kill with `pg_terminate_backend(pid)`.
- Connection storm from a misbehaving worker → check Container App replica count, scale down.

## Escalation

- If sustained > 20 min and no clear cause → scale up SKU temporarily (`az postgres flexible-server update --sku-name`).
