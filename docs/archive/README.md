# docs/archive — historical, non-authoritative

Superseded planning-iteration docs, kept for provenance only. **None of these is the current plan.** The active plan is [`../stories/BOARD.md`](../stories/BOARD.md) + [`../plan/EXECUTION-PLAN.md`](../plan/EXECUTION-PLAN.md), bootstrapped via [`../../CLAUDE.md`](../../CLAUDE.md) + [`../AGENT-PLAYBOOK.md`](../AGENT-PLAYBOOK.md).

| File | Was | Superseded by |
|---|---|---|
| `REPLAN.md` | a mid-course re-plan | the spec set / story board |
| `SEQUENCING_OPS_M_VS_SLICES.md` | OPS.M-vs-slice sequencing analysis | EXECUTION-PLAN waves |
| `SEQUENCING_RE_EVALUATION_2026_06_27.md` | sequencing re-evaluation | EXECUTION-PLAN waves |
| `OtherDetails.md` | scratch notes | — |

## Why other completed docs are NOT relocated here

The `OPS_*`, `SLICE*`, `MASTER_PLAN.md`, and the legacy `EXECUTION_PLAN.md` remain at their original paths under `docs/` because the **immutable ADRs** (`docs/adr/*`) cite them as decision provenance (~60 back-links). Moving them would break the ADR record for no benefit. Instead each carries a **⛔ SUPERSEDED / HISTORICAL banner** at the top so no cold agent mistakes it for the current plan. Treat every one of them as archived-in-place.
