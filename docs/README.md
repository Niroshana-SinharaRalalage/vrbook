# /docs

Living documentation for the VrBook platform.

## ▶ Specification set (agent-consumable, 2026-07 planning program) — START HERE

The current authoritative, agent-consumable spec set. **A new agent reads these first** (supersedes the older `BookingApp_Proposal.md` as the working spec):

| # | Read | Purpose |
|---|---|---|
| 1 | [`architecture/CURRENT-STATE.md`](architecture/CURRENT-STATE.md) | The whole as-built system in one read |
| 2 | [`ops/CONFIG-INVENTORY.md`](ops/CONFIG-INVENTORY.md) · [`ops/CURRENT-GAPS.md`](ops/CURRENT-GAPS.md) | Every config/secret · the P0/P1/P2 defect register |
| 3 | [`product/PRD.md`](product/PRD.md) · [`product/COMPETITIVE-RESEARCH.md`](product/COMPETITIVE-RESEARCH.md) · [`../OPEN-QUESTIONS.md`](../OPEN-QUESTIONS.md) | Requirements · cited market research · locked decisions |
| 4 | [`architecture/PHASE-3-4-DESIGN.md`](architecture/PHASE-3-4-DESIGN.md) (+ [`-REVIEW`](architecture/PHASE-3-4-DESIGN-REVIEW.md)) | Phase 3/4 design (§0.5 corrections authoritative) |
| 5 | [`stories/INDEX.md`](stories/INDEX.md) → the 6 `stories/EPIC-*.md` | **85 TDD-first user stories** (VRB-101…512) + gap/correction traceability |
| 6 | [`plan/EXECUTION-PLAN.md`](plan/EXECUTION-PLAN.md) · [`plan/AGENT-PROMPTS.md`](plan/AGENT-PROMPTS.md) | Parallel lanes + file ownership + copy-paste kickoff prompts |
| 7 | [`ops/CONFIG-MATRIX.md`](ops/CONFIG-MATRIX.md) · [`ops/GO-LIVE-RUNBOOK.md`](ops/GO-LIVE-RUNBOOK.md) · [`OPS_LAUNCH_COMPLETION_PLAN.md`](OPS_LAUNCH_COMPLETION_PLAN.md) | Per-env config · executable cutover · launch-hardening roadmap |

**How a new agent starts:** read #1, open `stories/INDEX.md`, pick the next story in your lane (`plan/EXECUTION-PLAN.md`) or paste your lane prompt (`plan/AGENT-PROMPTS.md`), then write the failing tests the story names → implement → satisfy the DoD. Conventions: TDD-first; `superpowers`/`frontend-design`/review skills; architect-consult for multi-module plans; lanes own non-overlapping files; `gh run watch` to green after every push (CI gotchas in [`../CLAUDE.md`](../CLAUDE.md)).

---

The rest of this directory tracks decisions, runbooks, and security artifacts.

| Directory | What lives here |
|---|---|
| [`adr/`](./adr/) | Architecture Decision Records (MADR format). One file per decision; index in [`adr/README.md`](./adr/README.md). |
| [`runbooks/`](./runbooks/) | On-call playbooks for Sev2 + Sev3 alerts (proposal §17.4). |
| [`security/`](./security/) | Threat model, OWASP compliance notes, security review checklist. |
| [`b2c/`](./b2c/) | AD B2C tenant configuration, user-flow exports, custom policy notes. |

Add new documents next to their peers. Cross-link liberally — the proposal links into
here; runbooks should link to relevant ADRs and Bicep modules.
