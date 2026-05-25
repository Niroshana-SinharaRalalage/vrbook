# Runbook — Redis evictions > 0

> Severity: **Sev3** → email.

Redis stores booking holds + distributed locks + cached toggles. Evictions mean memory
pressure → cached items dropping early. Holds and locks have explicit TTLs so they
auto-clean, but eviction can cause unexpected misses.

## First 5 minutes

1. Azure portal → Redis → Metrics → Used memory %.
2. If > 90%, immediate concern.

## Likely causes & fixes

- Hot feature-toggle cache key with unbounded growth → review key pattern, add scoped TTLs.
- Hold flood from abusive client → check `vrbook:hold:*` count; consider IP-level rate limit.
- Cache key explosion from search result cache → reduce TTL or scope.

## Remediation

- Bump SKU temporarily (C1 → C2).
- Identify offending key pattern: `redis-cli --bigkeys` or `SCAN` for `vrbook:hold:*` count.
- Open follow-up to fix the root cause; bump back down.
