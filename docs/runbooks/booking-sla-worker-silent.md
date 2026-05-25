# Runbook — Booking SLA worker silent

> Alert: no `BookingConfirmed` via SLA path in 6 hours. Severity: **Sev3** → email.
> Owner: A4 (Booking).

## Symptom

The Booking Worker should auto-confirm tentative bookings whose `tentative_until` has
elapsed without owner action. Six hours of silence implies either: (a) genuinely no
bookings hit the threshold, or (b) the worker is dead/wedged.

## Diagnostic queries

```kusto
// Worker heartbeat
traces
| where timestamp > ago(8h)
| where cloud_RoleName == "vrbook-workers-booking"
| summarize last_seen=max(timestamp)

// Tentative bookings past SLA but still Tentative
// (run against booking schema, via az postgres flexible-server execute)
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
