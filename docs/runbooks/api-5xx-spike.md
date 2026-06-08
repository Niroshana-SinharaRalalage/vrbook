# Runbook — API 5xx spike

> Alert: `alert-vrbook-{env}-5xx-spike` — API 5xx rate > 1% over 10 min.
> Severity: **Sev2** → on-call.

## Symptom

API container is returning 5xx responses at > 1% of total volume sustained for 10
minutes. The browser may show "Internal Server Error" or "Failed to fetch" toasts.
Health checks (`/health/live`, `/health/ready`) may still pass since they bypass
most middleware.

## First 5 minutes

1. Confirm in App Insights `requests` chart — is it a single endpoint or system-wide?
2. Check Container App revision history. Recent deploy? → rollback.
3. Check Postgres + Redis status (dependencies in App Insights).

## Diagnostic queries

Use `docs/observability/queries.kql` → `api-failures-by-endpoint` and
`api-5xx-last-15m`. Inline versions for direct paste:

```kusto
// Which endpoints are failing right now
AppRequests
| where TimeGenerated > ago(15m) and ResultCode startswith "5"
| summarize count_=count() by Url, bin(TimeGenerated, 1m)
| render timechart

// Drill into a specific failing endpoint with exception details
let parsed = ContainerAppConsoleLogs_CL
  | where TimeGenerated > ago(15m)
  | where Log_s startswith '{"@t"'
  | extend e = parse_json(Log_s);
parsed
| where tostring(e['@l']) == "Error"
| extend handler = tostring(e.RequestName), msg = tostring(e['@m']),
         source = tostring(e.SourceContext), trace = tostring(e.TraceId)
| project TimeGenerated, handler, source, trace, msg
| order by TimeGenerated desc
```

Reminder: `DomainExceptionLogFilter` suppresses framework-level Error logs for
known domain exceptions (BRV / NotFound / Forbidden / Conflict / FluentValidation).
If the alert fires, every line above is a *real* unhandled exception worth chasing.

## Likely causes & fixes

- **Bad deploy** → rollback revision (`az containerapp revision activate --revision <prev>`).
- **Postgres saturated** → see [`postgres-cpu-high.md`](./postgres-cpu-high.md).
- **Redis evictions** → see [`redis-evictions.md`](./redis-evictions.md).
- **Stripe upstream** → check Stripe status page.

## Escalation

- Unresolved in 15 min → engineering lead.
- Confirmed data corruption → freeze deploys, engage Lead immediately.
