# 2. Monorepo Layout

- Status: Accepted
- Date: 2026-01-15
- Deciders: Solutions Architecture
- Tags: repo-strategy, build, ci, contracts

## Context and Problem Statement

The Phase 1 codebase contains, at minimum, a .NET 8 modular-monolith API, three .NET background workers, a Next.js 14 web application, a Bicep IaC tree, an OpenAPI specification, a shared `VrBook.Contracts` library, end-to-end Playwright tests, k6 load tests, and runbooks. These artifacts evolve together: a contracts change in `VrBook.Contracts` must, in the same commit, regenerate the OpenAPI, regenerate the TypeScript client used by the web app, update the integration tests, and update any handlers that publish or subscribe to the affected event.

Section 1 of the proposal calls out the working model: "Execution uses **a monorepo and parallel Claude Code agents** working in git worktrees. The work breakdown defines 9 agents that can run with minimal merge conflict surface once Week 1 establishes shared contracts (DTOs, domain events, OpenAPI spec, ADR baseline)." Section 20.3 makes the contracts coupling explicit: "Contracts are sacred. Any change to `VrBook.Contracts` or `/contracts/openapi.yaml` requires a fast Slack ping + a PR labelled `contracts-change`. CI fails if a contracts change merges without bumping `/contracts/CHANGELOG.md`."

The team is 9 parallel agents working in their own git worktrees on feature branches off a shared `develop`. The optimization target is *atomic cross-cutting changes* (one PR touches API + web + tests + contracts + docs and either all of it merges or none of it does) and *low coordination overhead* (one CI pipeline, one PR template, one issue tracker).

## Decision Drivers

- Single source of truth for the OpenAPI specification and the `VrBook.Contracts` library shared by backend and frontend.
- Atomic cross-cutting refactors (contract rename = one PR, not a coordinated series).
- Parallel agent worktrees off a single repo (§1, §20.3).
- One CI pipeline that can validate the whole world — OpenAPI diff, .NET tests, web build, Bicep what-if, Playwright.
- Modular monolith (ADR-0001) already collapses 11 contexts into 1 deploy unit — splitting the *repo* would re-introduce the coordination cost we just avoided.
- Phase 2 mobile (React Native, §22.1) is expected to share the same TypeScript API client — a workspace package, not a separate repo.

## Considered Options

- **Monorepo** — One Git repository for backend, frontend, contracts, infra, and tests.
- **Multirepo (per-bounded-context or per-component)** — Separate repos for API, web, contracts, infra.
- **Hybrid (backend monorepo + separate frontend repo)** — Common in teams with separate web and platform groups.

## Decision Outcome

Chosen option: **"Monorepo"**, because the dominant Phase 1 cost is keeping the OpenAPI contract, the `VrBook.Contracts` library, and the consumer code in sync across 9 parallel agents — and a monorepo collapses that cost to a normal PR review. The §20.3 coordination rule ("Contracts are sacred … CHANGELOG required") is mechanically enforceable in a monorepo (path-based CODEOWNERS, CI check on the `/contracts` path) and only enforceable by social convention in a multirepo.

### Positive Consequences

- One `git clone` gives an agent the entire system they need to make a change.
- Contracts changes are *atomic*: rename a field in `VrBook.Contracts`, the same PR updates the OpenAPI, the TypeScript client, the React component, and the integration test. No "API merged, frontend forgot to pick up the version bump" class of bug.
- One CI workflow validates the whole change set. OpenAPI diff vs. `main` (§16.2) sees both sides of every contract change in the same PR.
- One issue tracker, one PR template, one set of branch protections — onboarding a new agent is one repo.
- Git worktrees (§20.3 "Worktree per agent. `git worktree add ../vrbook-A2 feat/a2-catalog`") work natively against a monorepo — each agent gets a checked-out branch in a sibling directory sharing one `.git`.
- Cross-cutting refactors (e.g., promoting `BookingStatus` from a string enum to a tagged union) become one PR instead of a coordinated multi-repo dance with version-pin updates.
- A single semver tag on `main` (§16.1: "Tags `v*.*.*` on `main`") versions the *whole platform*, including infra and contracts — no version-skew between API v1.4.0 and contracts v1.3.7.

### Negative Consequences / Trade-offs

- Larger checkout — but with `~50 MB` of source for Phase 1, well below any practical concern.
- CI becomes the long pole: every PR runs the full pipeline (backend tests, frontend build, Bicep what-if). Mitigated with path filters on jobs (frontend-only PRs skip backend tests) and aggressive caching (NuGet, npm).
- Single point of repo-level access control. Mitigated by GitHub CODEOWNERS for sensitive paths (`/infra`, `/contracts`).
- Build tools and CI workflows must understand multiple languages (C# + TypeScript + Bicep). Mitigated by per-language jobs in one workflow; this is well-trodden ground for GitHub Actions.
- A push to `main` that breaks any one component blocks deploys for *all* components. We accept this — it forces healthy CI hygiene and matches the §1 "single deploy unit" reality.

## Pros and Cons of the Options

### Monorepo

- Good, because contract changes are atomic across producer and consumer.
- Good, because §20.3 coordination rules (contracts-change label, CHANGELOG required, worktree-per-agent) are first-class workflows in a monorepo.
- Good, because one CI pipeline = one place to look when something is broken.
- Good, because the OpenAPI spec at `/contracts/openapi.yaml` can be both produced by the .NET project (`dotnet swagger tofile`) and consumed by the Next.js project's codegen step in the same `make` target.
- Good, because Phase 2 mobile (§22.1) can drop in as a workspace alongside the web app, sharing the same TypeScript types and API client.
- Bad, because all CI runs on all changes by default — path filters required.
- Bad, because a single careless push to `main` blocks every component's release pipeline until reverted.

### Multirepo (per-bounded-context or per-component)

- Good, because each repo's CI runs only on its own changes.
- Good, because per-repo access control is fine-grained.
- Bad, because the §20.3 "contracts are sacred" rule becomes a coordination protocol across repos — contracts repo PR merges first, then dependent repos bump the dependency and re-test, possibly discovering breaks days later.
- Bad, because cross-cutting refactors become multi-PR ceremonies with version-pin coordination — exactly the friction the parallel-agent model is designed to avoid.
- Bad, because semantic versioning of the platform fragments — there is no single artifact called "v1.4.0 of the platform."
- Bad, because the modular-monolith deploy (§3.2) already lives in one repo's worth of code — splitting the repo gains nothing operational and loses everything coordinational.

### Hybrid (backend monorepo + separate frontend repo)

- Good, because it appeals to teams with strong frontend/backend organizational separation.
- Bad, because we have no such organizational separation — agents work across the full stack as needed.
- Bad, because it retains the worst of the multirepo trade-off (contract changes are not atomic) without the multirepo upside (per-component CI isolation is partial at best).
- Bad, because the TypeScript types generated from OpenAPI become a versioned npm package that the web repo pins — same skew risk as multirepo.

## Repo Layout

```
/                              # monorepo root
  /src                         # .NET 8 backend solution
    /VrBook.Api                # API host (modular monolith composition root)
    /VrBook.Module.Identity    # bounded context module (one per §4)
    /VrBook.Module.Catalog
    /VrBook.Module.Pricing
    /VrBook.Module.Booking
    /VrBook.Module.Payment
    /VrBook.Module.Sync
    /VrBook.Module.Messaging
    /VrBook.Module.Reviews
    /VrBook.Module.Loyalty
    /VrBook.Module.Notifications
    /VrBook.Module.Admin
    /VrBook.Workers.Booking
    /VrBook.Workers.Notifications
    /VrBook.Workers.Sync
    /VrBook.Migrator
    /VrBook.Contracts          # shared DTOs and event payloads — sacred
  /web                         # Next.js 14 app
  /contracts                   # OpenAPI yaml + CHANGELOG
  /infra                       # Bicep modules + per-env params
  /tests                       # cross-cutting integration + e2e + load
  /docs                        # ADRs, runbooks, security
  /.github/workflows           # ci.yml, cd-staging.yml, cd-prod.yml
```

## Worktree Workflow

```bash
# Lead clones once
git clone <repo-url> vrbook-main
cd vrbook-main

# Each agent gets its own worktree on its feature branch
git worktree add ../vrbook-A2-catalog   feat/a2-catalog
git worktree add ../vrbook-A4-booking   feat/a4-booking
git worktree add ../vrbook-A5-payments  feat/a5-payments
# ... up to 9 worktrees, one per agent
```

All worktrees share one `.git` directory, so a fetch in any of them updates refs for all. This is the model assumed by §20.3 rule 5.

## CI Path Filters

A monorepo's main hazard is "every PR re-runs every test." Path filters in `.github/workflows/ci.yml` keep the loop tight:

- Jobs that run on every PR: `lint`, `openapi-diff`, `security-scan`.
- `backend-tests` runs only if `/src/**` or `/contracts/**` changed.
- `web-build` and `web-tests` run only if `/web/**` or `/contracts/**` changed.
- `infra-what-if` runs only if `/infra/**` changed.
- `e2e-playwright` runs on PRs labelled `e2e-required` and on every merge to `develop`.
- `load-k6` runs nightly against staging, not per PR.

The combination gives most PRs a 4–6 minute CI cycle; full-stack PRs (which touch contracts) get the full 12–15 minute cycle and a contracts-change reviewer.

## Contracts as the Inter-Agent Protocol

`VrBook.Contracts` is the only assembly that every module references. Per §20.3 rule 1, any change to it requires:

1. A PR labelled `contracts-change`.
2. An entry in `/contracts/CHANGELOG.md` (CI fails if the label is present without a CHANGELOG bump).
3. Review by the lead in addition to the normal reviewer.
4. A coordinated update of every consumer in the same PR (the OpenAPI regeneration, the TypeScript client regeneration, the handler implementations).

In a monorepo this is one PR. In multirepo it is N PRs across N repos with version-pin coordination. The cost difference is the decision.

## Links

- [Proposal §1 Executive Summary](../../BookingApp_Proposal.md)
- [Proposal §16 CI/CD Pipeline](../../BookingApp_Proposal.md)
- [Proposal §20 Parallel Agent Work Breakdown](../../BookingApp_Proposal.md)
- ADR-0001 Modular Monolith for Phase 1
- ADR-0009 Branch Strategy
- [Git worktree documentation](https://git-scm.com/docs/git-worktree)
