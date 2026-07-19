# VrBook — Story Board (SINGLE SOURCE OF TRUTH for story STATE)

**This file is the ONLY place story state lives.** An agent MUST update this file to claim and complete work. If it's not on the board, it isn't happening.

**States:** `TODO` · `CLAIMED` (an agent owns it, branch open) · `IN-REVIEW` (PR open) · `DONE` (merged to `develop`, CI green) · `BLOCKED` (a `blocked-by` is not `DONE`).

## Claim protocol (atomic — the git remote is the lock)
1. `git pull --rebase` on `develop`.
2. Pick the **highest-priority `TODO` story in your lane** whose every `blocked-by` (see [`INDEX.md`](INDEX.md)) is `DONE`. If none, your lane is idle — help review, or stop.
3. Create branch `story/VRB-NNN`.
4. Edit ONLY this story's row here → `CLAIMED`, put your branch in the Branch column. Commit `claim: VRB-NNN` and **`git push` immediately** (before any real work).
5. **If the push is rejected:** `git pull --rebase`. If the row is now `CLAIMED` by someone else → abandon, delete your branch, pick another story. Else re-push. *First push wins — this is the lock.*
6. Do the work on your branch. On completion: open a PR (`IN-REVIEW`), get CI green + review, merge to `develop`, then set the row `DONE` in a `done: VRB-NNN` commit.
7. **One agent = one active story.** Do not claim a second until yours is `DONE` or released back to `TODO`.

Edit only your own story's row to minimise conflicts. Full rules: [`../AGENT-PLAYBOOK.md`](../AGENT-PLAYBOOK.md).

---

## Wave 0 — Foundations (must be `DONE` before Wave 1 claims)

| ID | Lane | Priority | Status | Branch |
|---|---|---|---|---|
| VRB-300 | TEST | Must | DONE | story/VRB-300 |
| VRB-200 | CONFIG | Must (P0) | DONE | story/VRB-200 |
| VRB-201 | CONFIG | Must | DONE | story/VRB-201 |
| VRB-203 | CONFIG | Should | DONE | story/VRB-203 |
| VRB-202 | CONFIG | Should | DONE | story/VRB-202 |
| VRB-205 | CONFIG | Should | DONE | story/VRB-205 |
| VRB-DS  | DESIGN | Must | DONE | story/VRB-DS |
| VRB-301 | DEVOPS | Must (P0) | DEFERRED | (prod pipeline — postponed to pre-launch, owner 2026-07-14) |
| VRB-302 | DEVOPS | Must (P0) | DEFERRED | (blue-green rollback — postponed with VRB-301) |
| VRB-303 | DEVOPS | Must | DONE | story/VRB-303 |
| VRB-304 | DEVOPS | Must | IN-REVIEW | story/VRB-304 |
| VRB-306 | DEVOPS | Must | DONE | story/VRB-306 |

## Wave 1 — Launch features (claim after Wave 0 `DONE`)

| ID | Lane | Priority | Status | Branch |
|---|---|---|---|---|
| VRB-105 | PAY | Must | DONE | story/VRB-105 |
| VRB-104 | PAY | Must | DONE | story/VRB-104 |
| VRB-103 | PAY | Must | TODO | |
| VRB-102 | PAY | Must | IN-REVIEW | story/VRB-102 |
| VRB-113 | PAY | Should | TODO | |
| VRB-111 | PAY | Should | TODO | |
| VRB-112 | PAY | Should | TODO | |
| VRB-101 | CATALOG | Must | DONE | story/VRB-101 |
| VRB-106 | WEB-GUEST | Must | DONE | story/VRB-106 |
| VRB-107 | WEB-GUEST | Must | DONE | story/VRB-106 |
| VRB-108 | WEB-GUEST | Must | DONE | story/VRB-108 |
| VRB-109 | WEB-GUEST | Must | DONE | story/VRB-109 |
| VRB-110 | WEB-GUEST | Must | DONE | story/VRB-110 |
| VRB-110-followup | A11Y | Should | DONE | story/VRB-110-followup |
| settings-nav-gate | SETTINGS | Should | DONE | story/settings-nav-gate |
| VRB-210 | SETTINGS | Must | DONE | story/VRB-210 |
| VRB-211 | SETTINGS | Must | DONE | story/VRB-211 |
| VRB-212 | SETTINGS | Must | TODO | |
| VRB-213 | SETTINGS | Must | TODO | |
| VRB-214 | SETTINGS | Must | TODO | |
| VRB-215 | SETTINGS | Must | DONE | story/VRB-215 |
| VRB-216 | SETTINGS | Must | DONE | story/VRB-216 |
| VRB-216-web | SETTINGS | Must | CLAIMED | story/VRB-216-tiers-panel |
| VRB-217 | SETTINGS | Must | TODO | |
| VRB-218 | SETTINGS | Should | TODO | |
| VRB-219 | SETTINGS | Should | TODO | |
| VRB-220 | SETTINGS | Must | TODO | |
| VRB-206 | SETTINGS | P1 | DONE | story/VRB-206 |
| VRB-207 | SETTINGS | P1 | DONE | story/VRB-207 |
| VRB-208 | SETTINGS | P2 | DONE | story/VRB-208 |
| VRB-209 | SETTINGS | P2 | DONE | story/VRB-209 |

## Wave 2 — Launch-week (operator-gated)

| ID | Lane | Priority | Status | Branch |
|---|---|---|---|---|
| VRB-305 | DEVOPS | Must | TODO | |
| VRB-307 | DEVOPS | Must | DONE | story/VRB-307 |
| VRB-308 | DEVOPS | Must | TODO | |
| VRB-309 | DEVOPS | Must | TODO | |
| VRB-312 | DEVOPS | Must | TODO | |
| VRB-313 | DEVOPS | Must | TODO | |
| VRB-311 | COMPLIANCE | Must | DONE | story/VRB-311 |
| VRB-310 | COMPLIANCE | Must | TODO | |

## Wave 3 — Phase 3 (post-launch; strictly sequenced)

| ID | Lane | Priority | Status | Branch |
|---|---|---|---|---|
| VRB-400 | P3-FOUNDATION | Could | TODO | |
| VRB-401 | P3-FOUNDATION | Could | TODO | |
| VRB-402 | P3-FOUNDATION | Could | TODO | |
| VRB-403 | P3-FOUNDATION | Could | TODO | |
| VRB-404 | P3-FOUNDATION | Could | TODO | |
| VRB-405 | P3-ROOMS | Could | TODO | |
| VRB-406 | P3-ROOMS | Could | TODO | |
| VRB-407 | P3-ROOMS | Could | TODO | |
| VRB-408 | P3-ROOMS | Could | TODO | |
| VRB-409 | P3-ROOMS | Could | TODO | |
| VRB-410 | P3-ROOMS | Could | TODO | |
| VRB-411 | P3-ROOMS | Could | TODO | |
| VRB-412 | P3-ROOMS | Could | TODO | |
| VRB-420 | P3-CART | Could | TODO | |
| VRB-421 | P3-CART | Could | TODO | |
| VRB-422 | P3-CART | Could | TODO | |
| VRB-423 | P3-CART | Could | TODO | |
| VRB-424 | P3-CART | Could | TODO | |
| VRB-425 | P3-CART | Could | TODO | |
| VRB-426 | P3-CART | Could | TODO | |
| VRB-427 | P3-CART | Could | TODO | |
| VRB-428 | P3-CART | Could | TODO | |
| VRB-429 | P3-CART | Could | TODO | |
| VRB-430 | P3-CART | Could | TODO | |
| VRB-431 | P3-CART | Could | TODO | |

## Wave 4 — Phase 4 OTA (post-launch, after Wave 3)

| ID | Lane | Priority | Status | Branch |
|---|---|---|---|---|
| VRB-500 … VRB-512 | P4-OTA | Could | TODO | (13 stories — claim in ID order; all blocked-by Wave 3 cart) |

---

**Progress rollup:** 27 / 86 DONE. Update this count in your `done:` commit.

> **DEFERRED (owner decision 2026-07-14):** VRB-301 (cd-prod.yml) + VRB-302 (blue-green rollback) — build & validate on staging first, design prod infra/deploy later. Do not claim these until re-activated.
