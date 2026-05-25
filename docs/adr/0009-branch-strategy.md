# 9. Branch Strategy, Worktrees, and Release Versioning

- Status: Accepted
- Date: 2026-01-15
- Deciders: Solutions Architecture
- Tags: git, ci-cd, process, releases

## Context and Problem Statement

The Phase 1 build uses 9 parallel Claude Code agents working in git worktrees off a single monorepo (ADR-0002). The §16.1 branch strategy is short and prescriptive: "`main` → production (protected; PR + 1 review + green checks). `develop` → staging (auto-deploy on merge). Feature branches off `develop`; agents work in worktrees on branches like `feat/booking-state-machine`, `feat/sync-airbnb`. Tags `v*.*.*` on `main` cut a versioned release."

The §16.2 pipelines wire deployment to branches: every PR runs `ci.yml`; merge to `develop` triggers `cd-staging.yml`; merge to `main` triggers `cd-prod.yml` after manual approval and a 24-hour staging soak. The §20.3 coordination rules add the worktree convention ("`git worktree add ../vrbook-A2 feat/a2-catalog`") and the contracts-change PR labelling discipline.

The strategy must support: (a) high commit velocity from 9 parallel agents, (b) low merge-conflict surface, (c) atomic deploys to two environments, (d) a clear "what is in prod right now" answer at any moment, and (e) a Phase 2 hotfix posture without re-architecting the branching model.

## Decision Drivers

- **9 parallel agents** — many short-lived feature branches in flight at once. Long-lived branches are death.
- **Two long-lived environments** (staging, prod) — branch model must have a stable head pointer for each.
- **Auto-deploy to staging on merge to develop** (§16.2) — `develop` must always be deployable.
- **Manual-approval promotion to prod** (§16.2) — `main` is gated by a human after the staging soak.
- **Semver releases on main** (§16.1) — `v*.*.*` tags cut versioned releases for change-log and rollback purposes.
- **Conventional commits** — needed for changelog generation and for the contracts CHANGELOG (§20.3 rule 1).
- **Worktree-per-agent** (§20.3 rule 5) — branches must be conflict-free at the file level when agents work in their own bounded contexts.
- **Hotfix path** — production bugs must reach prod without going through a 24-hour staging soak.

## Considered Options

- **`main` + `develop` (GitFlow-lite)** — Two long-lived branches; features off `develop`; release via merge `develop → main`.
- **Trunk-based development (single `main`)** — One long-lived branch; features merged behind feature flags; every commit deployable.
- **Full GitFlow** — `main`, `develop`, `feature/*`, `release/*`, `hotfix/*` — heavyweight, designed for shrink-wrapped software.
- **GitHub Flow** — One long-lived `main`; feature branches; deploy on merge; environments managed by tags or labels.

## Decision Outcome

Chosen option: **"`main` + `develop` (GitFlow-lite)"**, because it is exactly the model the proposal already specifies (§16.1), it matches the two long-lived deploy environments (staging tracks `develop`, prod tracks `main`), it gives each agent a stable base branch (`develop`) without forcing them through prod's promotion gate on every change, and it supports semver tagging on `main` for clean release artifacts.

### Positive Consequences

- "What is in prod?" → `git show main` or the latest `v*.*.*` tag.
- "What will be in prod after the next promotion?" → `git log main..develop`.
- Agents work in isolation in `feat/aN-<topic>` branches and converge on `develop` via PR — no agent ever pushes directly to `develop` or `main`.
- Auto-deploy to staging on merge to `develop` (§16.2 cd-staging) gives the team a continuously deployed integration environment.
- Promotion to prod is `develop → main` (or a release branch off `develop` if a more controlled subset is needed) via PR, manually approved.
- Semver tags on `main` — `v0.1.0`, `v0.2.0`, etc. — generated automatically from conventional-commit history during cd-prod.
- Hotfixes branch off `main`, merge to `main` (via PR), then merge back to `develop` to avoid regression.

### Negative Consequences / Trade-offs

- **Two-branch overhead.** Engineers must remember the source branch for each PR. Mitigated by branch protection rules and PR templates that pre-fill the target branch.
- **Develop can drift from main between releases.** The 24-hour staging soak (§16.2) bounds drift in practice; we promote on a regular cadence (weekly during build phase, daily or per-feature near launch).
- **Merge-back of hotfixes.** Hotfix → main → cherry-pick / merge → develop is a two-step ceremony. Mitigated by a `hotfix.yml` workflow that opens the back-merge PR automatically.
- **Slightly slower feedback than trunk-based.** A feature flagged as "in develop, not yet in prod" lives in the gap longer than under trunk-based. Acceptable given the modular monolith doesn't have the flag infrastructure of a mature trunk-based shop.

## Pros and Cons of the Options

### `main` + `develop` (GitFlow-lite)

- Good, because matches §16.1 exactly.
- Good, because aligns with two deploy environments (one branch per environment).
- Good, because semver tags on `main` give a clean release artifact and rollback marker.
- Good, because feature branches off `develop` keep PRs small and reviewable.
- Bad, because two-branch overhead.
- Bad, because hotfix back-merge is a small but real ceremony.

### Trunk-based development

- Good, because shortest lead time from commit to prod.
- Good, because no merge-back ceremony.
- Bad, because requires mature feature-flag infrastructure to keep unfinished work safe in main — we are not building that in Phase 1.
- Bad, because every commit must be production-quality, which is the wrong bar for an 8-week build with 9 agents learning the codebase.
- Bad, because doesn't align with two-environment auto-deploy without complex tag-or-branch promotion layers.

### Full GitFlow

- Good, because supports complex release management (multiple versions in flight).
- Bad, because designed for shrink-wrapped software with fixed-cadence releases — we ship continuously.
- Bad, because `release/*` branches add a stage that gives no real benefit when staging is already gated by develop's CI.
- Bad, because the conceptual surface (5 branch types) is more than the team needs.

### GitHub Flow

- Good, because simplest possible model (one long-lived branch + features).
- Bad, because no built-in concept of two environments — staging vs prod becomes tag-based or label-based, which is ad-hoc.
- Bad, because every merge to `main` deploys to prod, which contradicts §16.2's "manual approval after staging soak."

## Branch Conventions

| Branch | Source | Target | Purpose |
|---|---|---|---|
| `main` | (release commits only) | (production) | Always represents production. |
| `develop` | `main` (initial), PR merges | (staging, via `cd-staging.yml`) | Integration branch; auto-deploys to staging. |
| `feat/aN-<topic>` | `develop` | `develop` (via PR) | Agent feature work. E.g., `feat/a4-booking-state-machine`. |
| `fix/<topic>` | `develop` | `develop` (via PR) | Non-urgent bugfix. |
| `hotfix/<topic>` | `main` | `main` (via PR), then back-merge to `develop` | Urgent production fix bypassing the soak. |
| `chore/<topic>` | `develop` | `develop` (via PR) | Build, tooling, dependency bumps. |
| `docs/<topic>` | `develop` | `develop` (via PR) | Documentation-only changes. |

## Conventional Commits

All commits use the conventional-commits format: `<type>(<scope>): <description>`. Types: `feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `perf`, `build`, `ci`. Scope is typically the module name (`booking`, `catalog`, `pricing`, `web`, `infra`, etc.) or `contracts` for `VrBook.Contracts` changes.

Examples:
- `feat(booking): add SLA timer worker for tentative-to-rejected transition`
- `fix(payment): handle Stripe webhook signature mismatch with 401 not 500`
- `feat(contracts): add BookingConflictDetected event payload`
- `docs(adr): add ADR-0007 EF Core migrations strategy`

The semantic-release tool reads conventional commits between the last `v*.*.*` tag and `main` HEAD to compute the next version bump and the changelog:
- `fix:` → patch
- `feat:` → minor
- `feat!:` or `BREAKING CHANGE:` in body → major

## Tagging and Release Versioning

- Cd-prod workflow tags every successful prod deploy with `v<major>.<minor>.<patch>` on the merge commit on `main`.
- The first prod tag at launch is `v1.0.0`.
- During the 8-week build, internal milestone tags are `v0.<sprint>.0` (e.g., `v0.4.0` at end of W4).
- Tags are signed (`tag.gpgsign = true`).
- Tag annotation contains the conventional-commits-generated changelog block.

## Branch Protection Rules

### `main`
- Require pull request before merging
- Require 1 approving review
- Require status checks: `ci`, `infra-pr`, `openapi-diff`, `security-scan`
- Require branches to be up to date before merging
- Require linear history (rebase or squash, no merge commits)
- Restrict force pushes
- Restrict deletions
- Require signed commits

### `develop`
- Require pull request before merging
- Require 1 approving review (can be from any code reviewer; lead-only review required if `contracts-change` label is present per §20.3 rule 1)
- Require status checks: `ci`, `infra-pr` (if `/infra/**` changed), `openapi-diff`
- Allow squash merge only
- Restrict force pushes
- Restrict deletions

### Other branches
- No protection — agents own their feature branches.

## Worktree Workflow (per §20.3 rule 5)

```bash
# Initial clone (lead, once)
git clone <repo-url> vrbook-main
cd vrbook-main
git fetch origin develop

# Each agent adds their own worktree on their own branch
git worktree add -b feat/a4-booking-state-machine ../vrbook-A4 origin/develop
cd ../vrbook-A4
# … work, commit, push …
gh pr create --base develop --title 'feat(booking): SLA timer worker'

# When done
cd ../vrbook-main
git worktree remove ../vrbook-A4
git branch -D feat/a4-booking-state-machine    # after merge
```

All worktrees share one `.git` directory in `vrbook-main` — a `git fetch` in any worktree updates refs for all.

## Hotfix Flow

1. `git worktree add -b hotfix/payment-webhook-401 ../vrbook-HF origin/main`
2. Fix, commit (`fix(payment): …`), push.
3. PR to `main`. Standard checks must pass; 1 review; no 24-hour soak gate.
4. Merge to `main`. `cd-prod.yml` deploys.
5. Auto-PR opened by `hotfix-backmerge.yml` workflow merging `main → develop` to keep develop ahead.

## Links

- [Proposal §16.1 Branch Strategy](../../BookingApp_Proposal.md)
- [Proposal §16.2 Pipelines](../../BookingApp_Proposal.md)
- [Proposal §20.3 Coordination Rules (worktree per agent)](../../BookingApp_Proposal.md)
- ADR-0002 Monorepo
- [Conventional Commits spec](https://www.conventionalcommits.org/)
- [Semantic Versioning](https://semver.org/)
