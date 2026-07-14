# /docs

Living documentation for the VrBook platform.

## ▶ Specification set (agent-consumable, 2026-07 planning program) — START HERE

The current authoritative, agent-consumable spec set. **A new agent reads these first** (supersedes the older `BookingApp_Proposal.md` as the working spec). **Bootstrap order is fixed — see [`../CLAUDE.md`](../CLAUDE.md) ⛔ Mandatory Bootstrap.**

| # | Read | Purpose |
|---|---|---|
| 0 | [`../CLAUDE.md`](../CLAUDE.md) · [`AGENT-PLAYBOOK.md`](AGENT-PLAYBOOK.md) · [`ENGINEERING-RULES.md`](ENGINEERING-RULES.md) | **How to work:** bootstrap, claim-a-story protocol, TDD/lane/DoD rules |
| 1 | [`architecture/CURRENT-STATE.md`](architecture/CURRENT-STATE.md) | The whole as-built system in one read |
| 2 | [`ops/CONFIG-INVENTORY.md`](ops/CONFIG-INVENTORY.md) · [`ops/CURRENT-GAPS.md`](ops/CURRENT-GAPS.md) | Every config/secret · the P0/P1/P2 defect register |
| 3 | [`product/PRD.md`](product/PRD.md) · [`product/COMPETITIVE-RESEARCH.md`](product/COMPETITIVE-RESEARCH.md) · [`../OPEN-QUESTIONS.md`](../OPEN-QUESTIONS.md) | Requirements · cited market research · locked decisions |
| 4 | [`architecture/PHASE-3-4-DESIGN.md`](architecture/PHASE-3-4-DESIGN.md) (+ [`-REVIEW`](architecture/PHASE-3-4-DESIGN-REVIEW.md)) | Phase 3/4 design (§0.5 corrections authoritative) |
| 5 | [`stories/BOARD.md`](stories/BOARD.md) — **story state (SoT)** → [`stories/INDEX.md`](stories/INDEX.md) → the 6 `stories/EPIC-*.md` | **86 TDD-first stories** (VRB-101…512 + VRB-300 API suite) + gap/correction traceability. **Claim on the board first.** |
| 6 | [`plan/EXECUTION-PLAN.md`](plan/EXECUTION-PLAN.md) · [`plan/AGENT-PROMPTS.md`](plan/AGENT-PROMPTS.md) | Parallel lanes + file ownership + copy-paste kickoff prompts |
| 7 | [`TEST-STRATEGY.md`](TEST-STRATEGY.md) · [`ops/CONFIG-MATRIX.md`](ops/CONFIG-MATRIX.md) · [`ops/GO-LIVE-RUNBOOK.md`](ops/GO-LIVE-RUNBOOK.md) · [`OPS_LAUNCH_COMPLETION_PLAN.md`](OPS_LAUNCH_COMPLETION_PLAN.md) | API contract suite · per-env config · executable cutover · launch-hardening roadmap |

**How a new agent starts:** follow the [`../CLAUDE.md`](../CLAUDE.md) mandatory bootstrap — `git pull --rebase`; read #0 + #1; **claim the highest-priority `TODO` story in your lane on [`stories/BOARD.md`](stories/BOARD.md)** (first-push-wins); read that story + its `blocked-by` in its epic file; write the failing tests it names → implement → **write API tests for any endpoint you touch (keep the VRB-300 suite green)** → satisfy the global + story DoD → **self-heal the board + docs**. Conventions: TDD-first; `superpowers`/`frontend-design`/review skills; architect-consult for multi-module plans; lanes own non-overlapping files (CODEOWNERS enforces); `gh run watch` to green after every push (CI gotchas in [`../CLAUDE.md`](../CLAUDE.md)).

---

The rest of this directory tracks decisions, runbooks, and security artifacts.

| Directory | What lives here |
|---|---|
| [`adr/`](./adr/) | Architecture Decision Records (MADR format). One file per decision; index in [`adr/README.md`](./adr/README.md). |
| [`runbooks/`](./runbooks/) | On-call playbooks for Sev2 + Sev3 alerts (proposal §17.4). |
| [`security/`](./security/) | Threat model, OWASP compliance notes, security review checklist. |

> Note: identity uses **Entra External ID (CIAM)**, not Azure AD B2C (ADR-0012). There is no `b2c/` directory.

Add new documents next to their peers. Cross-link liberally; runbooks should link to relevant ADRs and Bicep modules. Completed-slice plans/close-outs (`OPS_*`, `SLICE*`, `MASTER_PLAN.md`, the legacy `EXECUTION_PLAN.md`) are **historical** — they carry a superseded banner and are kept for provenance + ADR back-links; the active plan is the story board.
