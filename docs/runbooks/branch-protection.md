# Runbook — Branch protection (makes the parallel-agent guardrails real)

**Why:** the story-claim protocol ([`../stories/BOARD.md`](../stories/BOARD.md)) and the lane guard ([`../../.github/CODEOWNERS`](../../.github/CODEOWNERS)) are **advisory until branch protection enforces them.** Out of the box `develop` and `main` have no protection — an agent can push straight to either, bypassing the claim/PR/review flow. This runbook turns the guardrails on. It's a ~5-minute operator action; VRB-301 also wires it as part of the prod pipeline.

**The PR gate exists:** [`../../.github/workflows/ci.yml`](../../.github/workflows/ci.yml) runs on every `pull_request` into `develop`/`main` (build + lint + unit/arch tests + web lint/typecheck/vitest/build; integration is informational until VRB-300 flips it). Its two check contexts — **`backend (.NET 8)`** and **`frontend (Next.js)`** — are what you require below. (The `cd-staging-*.yml` workflows trigger on *push* and *deploy*; they are not PR checks.)

**Trade-off to accept first:** enabling "require a PR" means **no more direct pushes to the protected branch** — including yours. All changes land via PR. For a single operator that's slightly more ceremony; for N unattended agents it's the whole point (it's what stops collisions and enforces code-owner review on shared surfaces). Decide per branch: you may want full enforcement on `main` and PR-only on `develop`, or looser on `develop` if you still push directly.

## Recommended settings

| Setting | Applied | Effect |
|---|---|---|
| Require a pull request before merging | ✅ | No direct pushes; every change is a PR (claim → branch → PR → merge). **This is the core enforcement.** |
| Require status checks to pass (`backend (.NET 8)`, `frontend (Next.js)`) | ✅ | A red build/lint/unit/arch or web check blocks the merge — enforces "keep the suite green." |
| Require branches up to date (`strict`) | ✅ | Forces rebase-on-target before merge (matches the playbook). |
| Require conversation resolution | ✅ | Review threads must be resolved. |
| Require review from Code Owners | **non-blocking** | CODEOWNERS still **auto-requests** the owner on a shared-surface PR (the visible stop-sign), but does not hard-block. See the note below on *why not blocking*. |
| Required approving reviews | 0 | In-lane PRs merge on green with no human — supports "launch agents, walk away." Tighten to 1 if you want a human gate on everything. |
| Include administrators (`enforce_admins`) | false | **Escape hatch:** the owner can still direct-push / bypass if `ci.yml` misbehaves on its first PR run. Flip to `true` once CI is proven green on a PR. |

**Why code-owner review is non-blocking (a deliberate, corrected choice):** CODEOWNERS must name an owner with **write access**. `@architects` was a placeholder **team** — and this is a **user repo, which cannot have teams**, so every CODEOWNERS line was an "Unknown owner" error that would have *deadlocked* every shared-surface PR on an unsatisfiable reviewer. Fixed by pointing CODEOWNERS at `@Niroshana-SinharaRalalage`. Even so, hard-blocking on code-owner review has two problems here: (1) GitHub forbids approving your own PR, so an owner-token agent's shared-surface PR could never self-satisfy; (2) a lane that legitimately owns a shared surface for its wave (e.g. PAY owns `StripeGateway.cs`) would be blocked on its own in-lane work. So the guard is **visibility (auto-request) + green-CI + PR-required**, not a hard reviewer lock. If you add a second maintainer identity, flip `require_code_owner_reviews` to `true` for a hard cross-surface gate.

## Apply with `gh` (run once per branch)

Replace `OWNER/REPO` = `Niroshana-SinharaRalalage/vrbook`. Adjust the `contexts` array to the exact CI check names shown on a recent PR (`gh pr checks` or the Actions tab). Example for `develop`:

The nested-field form via `gh api -f` is fiddly; the reliable way is a JSON body piped to `gh api --input -`:

This is the exact body applied to **both** `develop` and `main` (already live — recorded here for reproducibility / disaster recovery):

```bash
gh api -X PUT repos/Niroshana-SinharaRalalage/vrbook/branches/develop/protection --input - <<'JSON'
{
  "required_status_checks": { "strict": true, "contexts": ["backend (.NET 8)", "frontend (Next.js)"] },
  "enforce_admins": false,
  "required_pull_request_reviews": { "require_code_owner_reviews": false, "required_approving_review_count": 0, "dismiss_stale_reviews": true },
  "required_conversation_resolution": true,
  "restrictions": null
}
JSON
```

Verify:

```bash
gh api repos/Niroshana-SinharaRalalage/vrbook/branches/develop/protection \
  --jq '{pr:.required_pull_request_reviews.require_code_owner_reviews, checks:.required_status_checks.contexts}'
```

> The exact JSON shape for `required_pull_request_reviews` can be fiddly via `gh api -f`; if a field is rejected, set it in the GitHub UI (Settings → Branches → Add rule) using the table above — same result. The point is: **PR-required + code-owner-review + status-checks green**, which is what makes the board and CODEOWNERS enforced rather than hoped-for.

## After enabling

The agent flow becomes: claim on the board → `story/VRB-NNN` branch → push → **open a PR** → CI green + code-owner review → merge → mark `DONE`. Update [`../AGENT-PLAYBOOK.md`](../AGENT-PLAYBOOK.md) is already written to this flow (§2/§5/§6); no doc change needed — only the switch.
