# Runbook — AirBnB iCal feed stale

> Alert: `last_success_at < now - 2h` on any `channel_feed`. Severity: **Sev3** → email.
> Owner: A6 (Sync).

## Symptom

A property's iCal feed hasn't been polled successfully for ≥ 2 × `poll_interval_minutes`.
Risk: an AirBnB reservation made in that window may not be reflected → potential
double-book. The tentative-window (default 24h) is the primary mitigation; this runbook
exists for the case where the gap exceeds that.

## First 5 minutes

1. Open `/admin/sync` dashboard. Identify which property/properties.
2. Click "Sync now" — does it succeed?
3. If yes → likely transient; clear alert, no further action. Log incident.
4. If no → continue below.

## Diagnostic queries

```kusto
customEvents
| where timestamp > ago(6h)
| where name == "sync.poll.attempt"
| extend property = tostring(customDimensions.propertyId)
| where property == "<id>"
| project timestamp, customDimensions.status, customDimensions.error
| order by timestamp desc
```

## Likely causes

- AirBnB feed URL invalid / rotated → owner updated AirBnB account.
- AirBnB rate limiting (rare, but documented).
- Network egress restriction on Container App.
- DNS failure for `airbnb.com`.

## Remediation

1. Owner verifies the inbound URL in AirBnB host calendar settings; copy current URL into `/admin/channel-feeds/{id}`.
2. If URL is correct → `curl` the URL from inside the Container App (`az containerapp exec`).
3. If network reachable → manual `sync-now` and watch logs.
4. If repeated failures across multiple properties → likely platform issue; escalate to infra.

## Escalation

- Persistent failure ≥ 24h on a single property → notify property owner.
- Failure across ≥ 50% of properties → engineering lead + client immediately.
