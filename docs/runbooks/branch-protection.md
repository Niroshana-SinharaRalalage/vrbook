# Runbook — Branch protection (makes the parallel-agent guardrails real)

**Why:** the story-claim protocol ([`../stories/BOARD.md`](../stories/BOARD.md)) and the lane guard ([`../../.github/CODEOWNERS`](../../.github/CODEOWNERS)) are **advisory until branch protection enforces them.** As of this writing `develop` and `main` have **no protection** — an agent can push straight to either, bypassing the claim/PR/review flow. This runbook turns the guardrails on. It's a ~5-minute operator action; VRB-301 also wires it as part of the prod pipeline, but you can (and for multi-agent runs, should) apply it now.

**Trade-off to accept first:** enabling "require a PR" means **no more direct pushes to the protected branch** — including yours. All changes land via PR. For a single operator that's slightly more ceremony; for N unattended agents it's the whole point (it's what stops collisions and enforces code-owner review on shared surfaces). Decide per branch: you may want full enforcement on `main` and PR-only on `develop`, or looser on `develop` if you still push directly.

## Recommended settings

| Setting | `develop` | `main` | Effect |
|---|---|---|---|
| Require a pull request before merging | ✅ | ✅ | No direct pushes; every change is a PR (claim → branch → PR → merge). |
| Require review from Code Owners | ✅ | ✅ | A PR touching a CODEOWNERS path (shared surfaces) needs the owner's review — the lane stop-sign. |
| Required approving reviews | 1 | 1 | At least one approval. For unattended agents, this is your gate — or use a review agent. |
| Require status checks to pass | ✅ (CI: build, unit, arch, web checks, integration) | ✅ | A red API suite / arch test blocks the merge — enforces "keep the suite green." |
| Require branches up to date | ✅ | ✅ | Forces rebase-on-`develop` before merge (matches the playbook). |
| Require conversation resolution | ✅ | ✅ | Review threads must be resolved. |
| Include administrators | optional | ✅ | If off, you retain a manual override; on `main` prefer on. |

## Apply with `gh` (run once per branch)

Replace `OWNER/REPO` = `Niroshana-SinharaRalalage/vrbook`. Adjust the `contexts` array to the exact CI check names shown on a recent PR (`gh pr checks` or the Actions tab). Example for `develop`:

```bash
gh api -X PUT repos/Niroshana-SinharaRalalage/vrbook/branches/develop/protection \
  -H "Accept: application/vnd.github+json" \
  -f 'required_pull_request_reviews[require_code_owner_reviews]=true' \
  -F 'required_pull_request_reviews[required_approving_review_count]=1' \
  -f 'required_status_checks[strict]=true' \
  -f 'required_status_checks[contexts][]=build-and-test' \
  -f 'required_status_checks[contexts][]=playwright-smoke' \
  -F 'enforce_admins=false' \
  -F 'restrictions=null' \
  -f 'required_conversation_resolution[enabled]=true'
```

Then the same for `main` with `enforce_admins=true`. Verify:

```bash
gh api repos/Niroshana-SinharaRalalage/vrbook/branches/develop/protection \
  --jq '{pr:.required_pull_request_reviews.require_code_owner_reviews, checks:.required_status_checks.contexts}'
```

> The exact JSON shape for `required_pull_request_reviews` can be fiddly via `gh api -f`; if a field is rejected, set it in the GitHub UI (Settings → Branches → Add rule) using the table above — same result. The point is: **PR-required + code-owner-review + status-checks green**, which is what makes the board and CODEOWNERS enforced rather than hoped-for.

## After enabling

The agent flow becomes: claim on the board → `story/VRB-NNN` branch → push → **open a PR** → CI green + code-owner review → merge → mark `DONE`. Update [`../AGENT-PLAYBOOK.md`](../AGENT-PLAYBOOK.md) is already written to this flow (§2/§5/§6); no doc change needed — only the switch.
