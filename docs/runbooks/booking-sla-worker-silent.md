# Runbook — Booking SLA worker silent

> Alert: no `BookingConfirmed` via SLA path in 6 hours. Severity: **Sev3** → email.
> Owner: A4 (Booking).

## Symptom

The Booking Worker should auto-confirm tentative bookings whose `tentative_until` has
elapsed without owner action. Six hours of silence implies either: (a) genuinely no
bookings hit the threshold, or (b) the worker is dead/wedged.

## Diagnostic queries

```kusto
// Booking Worker heartbeat — Container Apps stamps ContainerAppName_s
ContainerAppConsoleLogs_CL
| where TimeGenerated > ago(8h)
| where ContainerAppName_s == "ca-vrbook-bookingworker-staging"
   or ContainerAppName_s == "ca-vrbook-bookingworker-prod"
| summarize last_seen = max(TimeGenerated), count_ = count() by ContainerAppName_s

// Same worker via handler trace (after A4.1 ships the SLA sweep)
let parsed = ContainerAppConsoleLogs_CL
  | where TimeGenerated > ago(8h)
  | where Log_s startswith '{"@t"'
  | extend e = parse_json(Log_s);
parsed
| where tostring(e.SourceContext) startswith "VrBook.Workers.Booking"
| summarize count_ = count() by bin(TimeGenerated, 30m)
| render timechart
```

```sql
-- Tentative bookings past SLA but still Tentative
SELECT id, reference, property_id, tentative_until
FROM booking.bookings
WHERE status = 'Tentative' AND tentative_until < now() - interval '1 hour'
ORDER BY tentative_until;
```

```sql
SELECT id, reference, property_id, tentative_until
FROM booking.bookings
WHERE status = 'Tentative' AND tentative_until < now() - interval '1 hour'
ORDER BY tentative_until;
```

## Likely causes & fixes

- Worker replica count = 0 (KEDA scale rule misconfigured) → bump min replicas; investigate trigger.
- Worker crashed and not restarting → check Container App logs; restart revision.
- Service Bus topic empty (no `BookingPlaced` events) → check booking-place flow.

## Remediation

1. Restart worker: `az containerapp revision restart -n booking-worker-vrbook-prod`.
2. Manually trigger SLA reprocessing for stale rows by re-publishing an internal "SLA sweep" message.
3. File post-incident ticket if any guest-visible delay > 1h.
