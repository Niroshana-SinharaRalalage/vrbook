# VrBook — Agent Playbook (the operating model)

**If you are an agent picking up work on VrBook, this file governs *how* you work.** [`ENGINEERING-RULES.md`](ENGINEERING-RULES.md) governs *what good work looks like*. Read both once, fully, before touching code. They exist so that several agents can run in parallel with no human in the loop and still not collide, drift, or leave the repo un-inheritable.

---

## 1. Mandatory bootstrap sequence (do these in order, every fresh session — no skipping)

1. **`git pull --rebase`** on `develop`. You always work off the latest `develop`.
2. Read **[`CLAUDE.md`](../CLAUDE.md)** (owner-locked policies + stack + CI traps) and **this file** + **[`ENGINEERING-RULES.md`](ENGINEERING-RULES.md)**.
3. Read **[`architecture/CURRENT-STATE.md`](architecture/CURRENT-STATE.md)** — the as-built system. Do not assume anything about the codebase you have not read.
4. Open **[`stories/BOARD.md`](stories/BOARD.md)** — the single source of truth for what is `TODO` / `CLAIMED` / `DONE`.
5. Identify your **lane** (from your kickoff prompt, or [`plan/EXECUTION-PLAN.md`](plan/EXECUTION-PLAN.md)). If no lane was given, STOP and ask the owner — do not free-lance across lanes.
6. **Claim a story** (§2). Then read that story in full in its `stories/EPIC-*.md` file, plus every story it is `blocked-by` / `related-to`.
7. Execute the story under TDD (§3) inside your lane's owned files only (§4). Keep the API suite green (§5).
8. On completion, **self-heal the docs** (§6) and mark the story `DONE`.

There is no valid path that skips 1, 4 (claim), or 6. If you find yourself writing code before you have a `CLAIMED` row on the board with your branch in it, you are off-protocol — stop and back up.

## 2. Claiming & releasing a story (collision-safe)

The board is the lock; the git remote arbitrates. Full protocol is in [`stories/BOARD.md`](stories/BOARD.md) §Claim protocol. In short:

- Pick the **highest-priority `TODO` story in your lane whose every `blocked-by` is `DONE`.** Never claim out of wave order; never claim another lane's story.
- Create branch `story/VRB-NNN`, set the row to `CLAIMED` with your branch, **commit + push before doing any real work.** First push wins — if yours is rejected and the row is now someone else's, pick another story.
- **One agent, one active story.** Finish or release before claiming the next.
- **Releasing:** if you must abandon, set the row back to `TODO`, note why in the commit, delete your branch. Never leave a story `CLAIMED` with no active branch — that deadlocks the lane.

## 3. TDD is non-negotiable

Every story's TDD plan names the Unit / Integration / E2E tests to write. **Write the failing test first**, watch it fail, write the minimal code to pass, refactor. Use `superpowers:test-driven-development`. A story is not done when the code "works" — it is done when the tests it named are green *and* the DoD checklist (§ in the story) is fully ticked. See [`ENGINEERING-RULES.md`](ENGINEERING-RULES.md) for the DoR/DoD that applies to *every* story; new stories are authored from [`stories/_STORY-TEMPLATE.md`](stories/_STORY-TEMPLATE.md), which embeds that checklist inline.

## 4. Staying in lane (no two agents edit one file)

- Your lane owns a fixed set of files/dirs ([`plan/EXECUTION-PLAN.md`](plan/EXECUTION-PLAN.md) + your story's `Parallelisation` field). **Touch only those.**
- A need for a file another lane owns is **not** permission to edit it. It is either a **dependency** (wait for that lane's story to be `DONE`) or a **contract** (that lane ships the interface first, you consume it). 
- The **load-bearing shared surfaces** (`Program.cs`/DI, `StripeGateway.cs`, the RLS core, design-system primitives, `VrBook.Contracts`) are held by exactly one lane per wave — see the ownership table in [`plan/EXECUTION-PLAN.md`](plan/EXECUTION-PLAN.md). `.github/CODEOWNERS` makes a cross-lane edit require the owner's review — that review flag is a *stop sign*, not a speed bump.
- **Enforcement caveat (be honest with yourself):** the claim protocol and CODEOWNERS are only *enforced* once branch protection is on — otherwise they're honor-based. Before launching unattended agents, apply [`runbooks/branch-protection.md`](runbooks/branch-protection.md) (require-PR + require-code-owner-review + required status checks on `develop`/`main`). Until then, follow the protocol anyway — the collision guarantees depend on every agent respecting it.
- If your story genuinely cannot proceed without out-of-lane changes, that is an **escalation** (§7), not a quiet edit.

## 5. Keep the API suite green (verification loop)

The endpoint contract test suite (**VRB-300**, [`TEST-STRATEGY.md`](TEST-STRATEGY.md)) is the platform's safety net. Every merge must leave it green.

- If your story adds or changes an endpoint, **you write its API tests as part of the story** (happy path · auth/authorization · validation · error-contract · idempotency). This is a DoD line, not optional.
- Before opening your PR, run the loop: **unit + integration green → full API suite green → then merge.** A red API suite blocks the merge, full stop.

## 6. Self-healing state (leave it inheritable)

The next cold agent inherits only what is written down. On finishing a story you MUST, in the same PR or an immediate follow-up commit:

- Flip the story row to `DONE` on [`stories/BOARD.md`](stories/BOARD.md) and bump the progress rollup.
- If you changed a contract, config key, or gap status, update the doc that owns that fact ([`ops/CONFIG-MATRIX.md`](ops/CONFIG-MATRIX.md), [`ops/CURRENT-GAPS.md`](ops/CURRENT-GAPS.md), the relevant `EPIC-*.md`).
- If you made an architectural decision, record it as an **ADR** (`docs/adr/NNNN-*.md`) — do not bury it in a commit message.
- If you discovered new work, add a story (or a follow-up row) rather than leaving a TODO in code.

A story whose code merged but whose board row still says `CLAIMED`, or whose new endpoint has no matching row in CONFIG-MATRIX, is **not done** — it has just handed the next agent a landmine.

## 7. When to escalate / consult (triggers, not vibes)

These are the *conditions*, not suggestions. When a condition holds, take the action.

| Condition | Action |
|---|---|
| Multi-module / cross-cutting / sequencing plan not already spelled out in a story | **Consult the `Plan` (system-architect) agent, commit the plan as a doc, get owner review** before executing (owner rule; see [`feedback_consult_architect_for_planning`]). |
| A story's §5-style question is **product/policy** (pricing, legal, what-the-owner-wants) | Ask the **owner**. |
| A story's question is **technical** (DTO shape, token, arch-lock) | Adopt the **architect's** recommendation directly — do *not* ask the owner (owner rule). |
| Evidence contradicts the current plan/story | **Re-consult the architect proactively** — don't burn time forcing the old plan. |
| You're tempted to defer / partially-scope a locked story | That deferral is an **architect consult first**; default to FULL scope on owner pushback. |
| Any UI work | Invoke **`frontend-design`** — intentional system, not templated defaults. |
| Before merge | Invoke **`superpowers:requesting-code-review`**. |
| A bug hunt | Invoke **`superpowers:systematic-debugging`**; cite logs/DB rows/HTTP bodies, never "the hypothesis makes sense" ([`feedback_evidence_not_hypothesis`]). |
| You need out-of-lane file changes to finish | Stop; raise it as a dependency/contract with the owning lane — never edit across lanes silently. |

## 8. Before every push (CI parity — the local checks that match CI)

Backend: `dotnet format src/VrBook.sln --verify-no-changes --no-restore` · `dotnet publish src/VrBook.Api/VrBook.Api.csproj -c Release` (catches CA1822/CHARSET the plain build misses) · `dotnet test --filter "Category!=Integration"` (the CI filter — wider than `Category=Unit`).
Web: `npm run lint && npm run typecheck && npm test && npm run check:e2e-suite`.
Then push and **`gh run watch <id> --exit-status` to green before you mark `DONE` or the next lane rebases.** Doc-only commits don't trigger the API workflow — don't wait on a run that won't start. All CI traps are in [`CLAUDE.md`](../CLAUDE.md).

---

**The one-line test of this playbook:** the owner launches N agents with nothing but their lane prompt in [`plan/AGENT-PROMPTS.md`](plan/AGENT-PROMPTS.md), walks away, and each one bootstraps here, claims the right story, stays in lane, keeps the suite green, and leaves the board + docs correct for the next agent. If anything you're about to do breaks that, it's wrong.
