# ADR-0015 — Number intentionally skipped (tombstone)

- **Status:** N/A (tombstone)
- **Date:** 2026-07-14

## Context

The ADR sequence jumped 0014 → 0016 with no 0015. A spec audit flagged the gap as an integrity smell: a reader can't tell whether an ADR is missing, unmerged, or was never assigned.

## Decision

**ADR number 0015 was never assigned to a decision.** It is a numbering gap from authoring, not a lost or withdrawn record. This tombstone reserves the number so the index stays contiguous and no one hunts for a decision that does not exist.

No decision content belongs here. If a future decision needs an ADR, it takes the **next free number**, not 0015 — reusing a tombstoned number would re-introduce the ambiguity this file exists to remove.

## Consequences

- The `docs/adr/` sequence reads 0001–0019 with 0015 explicitly accounted for.
- The tenant-isolation / RLS design (interceptor + per-tenant GUC) is documented in `CURRENT-STATE.md` and the OPS.M implementation history, not in a dedicated ADR; if it ever warrants one, it gets a new number.
