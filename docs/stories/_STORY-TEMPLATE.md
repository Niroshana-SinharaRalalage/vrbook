# Story template (copy this block for every new story)

Copy the block below into the right `EPIC-*.md`, replace `VRB-NNN` + fields, add a row to [`BOARD.md`](BOARD.md) and [`INDEX.md`](INDEX.md). The **Definition of Ready** and **Definition of Done** checklists are embedded so the rules travel with the story — an agent that has the story open has the rules open. They are the per-story copy of the global checklist in [`../ENGINEERING-RULES.md`](../ENGINEERING-RULES.md); keep them in sync.

---

### VRB-NNN — <short title>
- **Epic:** <epic> · **Priority:** Must/Should/Could · **Estimate:** S/M/L · **Lane:** <LANE> · **Wave:** <n>
- **Narrative:** As a <role>, I want <capability>, so that <outcome>.
- **Acceptance criteria (Given/When/Then):**
  - **Given** … **when** … **then** …
  - (auth) **Given** an anonymous request, **then** 401 on authed routes.
  - (isolation) **Given** the wrong tenant, **then** RLS/`HasTenantRole` denies it.
- **TDD plan:** the exact Unit / Integration / E2E tests to write first (failing → minimal → refactor). Name them.
- **Technical notes:** files to touch (all owned by this lane), existing abstractions to reuse, gotchas.
- **UI/UX:** (if any UI) states, a11y (WCAG 2.2 AA), which design-system primitives it consumes.
- **Configuration:** new keys/secrets/flags, per env (dev/staging/prod). Cross-check `../ops/CONFIG-MATRIX.md`.
- **Rollout:** how it ships (flag? migration? inert until…), and the rollback trigger.
- **Observability:** the metric/log/alert that proves it works in prod.
- **Definition of Ready (tick before the first test):**
  - [ ] Story is `CLAIMED` by me on [`BOARD.md`](BOARD.md) with my branch, pushed.
  - [ ] I've read this story + every `blocked-by`/`related`; all `blocked-by` are `DONE`.
  - [ ] I read `../architecture/CURRENT-STATE.md` for this area **and grepped for an existing implementation** of what I'm about to build.
  - [ ] Every file I'll touch is owned by my lane; no out-of-lane edit is required.
- **Definition of Done (tick before marking `DONE`):**
  - [ ] **Did I search for an existing implementation before creating a new one?** (reuse > reinvent)
  - [ ] Every test the TDD plan named is written and green (unit + integration + any E2E).
  - [ ] **Every endpoint I added/changed has a `RouteMatrix` row (or `[ExemptFromCrossTenantMatrix]`) + `Contract/*` tests, and the API suite (VRB-300) is green.**
  - [ ] All acceptance criteria met; UI used `frontend-design` + meets WCAG 2.2 AA.
  - [ ] Owner-locked policies honoured; no `IsOwner`/`IsAdmin`/`Owner,Admin` literals; no committed secrets.
  - [ ] Pre-push CI-parity checks pass locally (playbook §8); pushed; `gh run watch` green; `superpowers:requesting-code-review` run + findings addressed.
  - [ ] **Self-healed:** board row `DONE` + rollup bumped; CONFIG-MATRIX/CURRENT-GAPS/epic updated for any fact I changed; ADR written for any decision.
  - [ ] Nothing left that blocks the next cold agent (no dangling `CLAIMED`, no undocumented contract, no TODO-in-code standing in for a missing story).
- **Dependencies:** blocked-by: <ids or none> · blocks: <ids> · related: <ids>. (These drive the [`INDEX.md`](INDEX.md) spine — an agent reads its neighbours, not all 86.)
- **Parallelisation:** Lane = <LANE>. Owns `<exact paths>`. Sequenced-with / exclusive-hold: <shared surface, if any>.

---

**Escalation triggers (when the story hits one, act — see [`../AGENT-PLAYBOOK.md`](../AGENT-PLAYBOOK.md) §7):** multi-module/cross-cutting/schema/contract change or unclear-root-cause bug → consult the `Plan` (system-architect) agent; product/policy question → owner; technical question → adopt the architect's call; any UI → `frontend-design`; before PR → `superpowers:requesting-code-review`.
