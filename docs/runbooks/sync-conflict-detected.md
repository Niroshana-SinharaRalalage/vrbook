# Runbook — Sync conflict detected

> Severity: **Sev3** → dashboard banner + owner email.
> Owner: A6 (Sync).

## Symptom

An AirBnB reservation overlaps a direct booking. `SyncConflictDetected` event fired.

## First 5 minutes

1. Open `/admin/sync` → Conflicts tab.
2. Confirm overlap dates; check both bookings.
3. Decide: keep direct, cancel direct (refund guest), or manual override.

See proposal §8.3 for the resolution semantics.

## Decision tree

- AirBnB cancelled their booking and feed is lagging → `keep_direct` (rare).
- AirBnB has the legitimate first booking → `cancel_direct`. Direct guest refunded 100% (within tentative window) or per policy.
- Owner is calling guest to negotiate move → `manual_override`, follow up within 24h.

## Escalation

- > 5 unresolved conflicts on dashboard → page lead.
