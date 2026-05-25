# 8. Bicep for Infrastructure as Code

- Status: Accepted
- Date: 2026-01-15
- Deciders: Solutions Architecture
- Tags: infrastructure, iac, bicep, azure, ci-cd

## Context and Problem Statement

The platform deploys to a single Azure subscription with three environments (dev, staging, prod) per §15.2. The §15.1 inventory lists 15 distinct Azure resource types — Container Apps Environment, multiple Container Apps and Jobs, PostgreSQL Flexible Server, Redis Cache, Service Bus, SignalR Service, Storage Account, Key Vault, App Insights, Log Analytics, Container Registry, Front Door, plus an external AD B2C tenant — all of which must be reproducible, version-controlled, and reviewable in PRs.

Section 15.3 lays out the desired Bicep tree:

```
/infra
  /modules
    container-apps-env.bicep
    container-app.bicep            # parameterized: name, image, env, scale rules
    container-app-job.bicep        # parameterized: name, image, schedule, env
    postgres-flexible.bicep
    redis.bicep
    service-bus.bicep
    signalr.bicep
    storage.bicep
    key-vault.bicep                # + secret seeding from pipeline variables
    app-insights.bicep
    front-door.bicep
    network.bicep                  # VNet + subnets + NSGs + private DNS zones
    private-endpoint.bicep
  /environments
    dev.bicepparam
    staging.bicepparam
    prod.bicepparam
  main.bicep                        # orchestrator
```

The §16.2 CI/CD pipeline runs Bicep `what-if` in CI and applies in CD: "Bicep `what-if` then deploy to staging." The §21 R8 risk explicitly addresses drift: "Bicep IaC drift over 8 weeks of frequent infra change — `bicep what-if` gate on PR; weekly drift detection cron." The §23.5 appendix includes a working Bicep snippet for the API Container App, confirming the proposal expects Bicep concretely, not a generic "IaC of your choice."

## Decision Drivers

- **Azure-only target** (§15) — no multi-cloud requirement.
- **First-class Azure preview support** — new Azure resource features should be available on day one, not after a community provider catches up.
- **`what-if` semantics** — a deploy preview that shows what *will* change before it changes, run as a PR gate (§16.2, §21 R8).
- **No state file to operate** — IaC state lives in Azure's resource manager, not in a tool-managed remote state we have to back up, lock, and secure.
- **Cheap onboarding** — a new agent must be able to deploy a dev environment from a clean machine in under an hour.
- **Reviewable diffs** — PRs touching infra must show the actual ARM-shape diff so reviewers can reason about what changes.
- **Module reuse across environments** — one parameterised module per resource type, one bicepparam per environment.

## Considered Options

- **Bicep** — Microsoft-authored Azure-native IaC DSL that transpiles to ARM templates.
- **Terraform (with AzureRM provider)** — HashiCorp's general-purpose IaC tool with broad provider ecosystem.
- **Pulumi (with Azure Native provider)** — IaC in real programming languages (TypeScript/C#/Python).
- **ARM JSON templates** — The pre-Bicep substrate; what Bicep compiles to.
- **Azure Resource Manager via CLI scripts** — Imperative `az` commands in shell scripts.

## Decision Outcome

Chosen option: **"Bicep"**, because (a) Azure-native parity with the underlying resource manager (no provider lag), (b) `what-if` is a first-class command that gives reviewers a real diff against live state, (c) no state file to operate — the canonical state lives in Azure and we read it on demand, (d) `bicepparam` per environment is the exact module-plus-parameters shape §15.3 calls for, and (e) the §23.5 appendix already exists in Bicep, so writing the rest of the tree is a continuation, not a starting decision.

### Positive Consequences

- New Azure preview features (e.g., Container Apps workload profiles, when they were preview) are available the day Microsoft ships them — no waiting on a community provider.
- `az deployment group what-if --resource-group rg-vrbook-staging --template-file infra/main.bicep --parameters infra/environments/staging.bicepparam` is the PR-gate command. Output shows exactly which resources are `Create`, `Modify`, `Delete`, `Unchanged`.
- No remote state to back up. No `terraform.tfstate` to lock. No `pulumi backend init`. The source of truth is what's actually in Azure; the source of intent is what's in `/infra`.
- Module structure (§15.3) maps one-to-one onto Bicep modules — `container-app.bicep` takes name/image/env/scale and is reused for the API, the workers, and the migrator.
- `bicepparam` files (typed parameter files) are first-class — `dev.bicepparam`, `staging.bicepparam`, `prod.bicepparam` are tiny, readable, and reviewable. No HCL `tfvars` magic.
- Key Vault secret seeding via `@secure()` parameters fed from pipeline variables — secrets are never in the repo, never in plain bicepparam.
- Cost: zero. Bicep is part of the Azure CLI.

### Negative Consequences / Trade-offs

- **Azure-only.** If we ever needed AWS, GCP, or third-party SaaS resources (e.g., a Cloudflare DNS record), Bicep cannot help. Mitigated by the §1 architecture being explicitly Azure-only for Phase 1; if a non-Azure resource appears (e.g., SendGrid sender authentication DNS records), it goes in a separate small Terraform or a manual runbook step. The §22 Phase 2 roadmap also stays Azure-only.
- **State lives in Azure.** This is mostly a positive (no state-file ops) but it means `what-if` requires a live Azure read. CI must have an authenticated identity scoped to read resource state. Mitigated by an OIDC-federated GitHub Actions service principal with `Reader` on the subscription and `Contributor` only on the per-environment resource group.
- **No first-class drift detection.** Bicep doesn't have a `terraform plan` against state; drift is detected by running `what-if` against the live state and seeing unexpected changes. Mitigated by §21 R8's "weekly drift detection cron" — a scheduled GitHub Actions workflow that runs `what-if` against each environment and posts a Slack alert if any non-empty diff is found.
- **Smaller community than Terraform.** Fewer Stack Overflow answers, fewer ready-made modules. Acceptable for our scope; Microsoft's first-party module gallery (Azure Verified Modules) covers our needs.

## Pros and Cons of the Options

### Bicep

- Good, because Azure-native — every Azure resource is supported on day one of its GA (and most preview).
- Good, because `what-if` shows the actual change set as a PR gate.
- Good, because no state file to operate.
- Good, because `bicepparam` matches §15.3 structure exactly.
- Good, because the §23.5 sample is already in Bicep — momentum exists.
- Bad, because Azure-only.
- Bad, because smaller community than Terraform.

### Terraform (AzureRM provider)

- Good, because multi-cloud, multi-provider — could handle Stripe, SendGrid, Cloudflare if we needed them later.
- Good, because the largest IaC community by far.
- Good, because `terraform plan` is the canonical preview command, well understood.
- Bad, because AzureRM provider lags Azure GA by weeks to months for some preview features.
- Bad, because state file operations (remote state, locking, backup) are real ongoing work — particularly painful with parallel agents.
- Bad, because HCL is its own language with its own corner cases; engineers must learn it. Bicep reads more like ARM with syntax.
- Bad, because Terraform's licence change (BSL) in 2023 added supply-chain anxiety we don't need to inherit.

### Pulumi (Azure Native)

- Good, because IaC in real languages (TypeScript or C#) — type system catches errors at author time.
- Good, because the Azure Native provider is auto-generated from Azure resource manager schemas — provider lag is minimal, similar to Bicep.
- Bad, because state file operations again — Pulumi Service or self-hosted backend.
- Bad, because "infra in a real language" tempts cleverness that infra should not have — loops over arrays of resource definitions become hard to review.
- Bad, because pricing model (Pulumi Cloud) is a recurring cost. Self-hosted backend works but is extra ops.
- Bad, because the community is smaller than both Bicep's and Terraform's.

### ARM JSON templates

- Good, because the canonical Azure substrate — no transpilation.
- Bad, because the JSON is hostile to humans — verbose, hard to diff, hard to refactor. The reason Bicep exists.
- Bad, because no `what-if` improvement over Bicep — same underlying command.

### Azure CLI scripts

- Good, because trivial to write the first ten lines.
- Bad, because imperative scripts are not declarative — there is no notion of "what should exist," only "what to do next." Drift detection becomes "did the script run?" not "does reality match intent?"
- Bad, because not reproducible — order-of-operations bugs and partial failures leave the environment in an unknown state.
- Bad, because no `what-if` — review is impossible without running the script.

## Module Structure Details

Per §15.3, every Bicep module is parameterised on `(env, location, …resource-specific)`. The orchestrator `main.bicep` composes the modules and wires outputs into inputs (subnet IDs into Postgres, Key Vault URI into Container Apps secret refs, etc.).

Per-environment files are `bicepparam` (typed parameter files), not plain JSON:

```bicep
// infra/environments/staging.bicepparam
using '../main.bicep'

param env             = 'staging'
param location        = 'eastus2'
param postgresSku     = 'GP_Standard_D2ds_v5'
param postgresHa      = false
param apiMinReplicas  = 1
param apiMaxReplicas  = 5
param signalrUnits    = 1
param frontDoorEnabled = true
```

The §15.2 environment matrix maps directly into these parameter files. A reviewer changing `apiMaxReplicas` from `5` to `10` sees a one-line PR diff that `what-if` will show landing as a Container App scale-rule update.

## CI Gates

- **`infra-pr.yml`** — On every PR touching `/infra/**`:
  1. `bicep build infra/main.bicep` (syntax check, lint).
  2. `bicep lint infra/main.bicep` (best-practices linter).
  3. `az deployment group what-if --resource-group rg-vrbook-dev --template-file infra/main.bicep --parameters infra/environments/dev.bicepparam` — posted to PR as a comment.
  4. PR cannot merge if `what-if` shows `Delete` on any resource without a `delete-confirmed` label.

- **`infra-drift-weekly.yml`** — Scheduled, weekly:
  1. `what-if` against each environment with no PR changes applied.
  2. Any non-empty diff fails the workflow and posts a Slack alert.
  3. Investigation outcome: either the diff is a manual change that must be promoted to Bicep, or someone made an unauthorised portal change that must be reverted.

- **`cd-staging.yml` / `cd-prod.yml`** — On merge / approved promotion:
  1. `what-if` (final preview).
  2. `az deployment group create` (apply).
  3. Output deployment ID logged to App Insights.

## No-Drift Tolerance Policy

§21 R8 calls drift over an 8-week build "M / M" — likelihood medium, impact medium. We treat any detected drift as a Sev3 unless trivially explainable (e.g., a tag auto-applied by Azure Policy). The investigation outcome is documented in `/docs/runbooks/bicep-drift-detected.md`. Quarterly review of the drift log informs whether we need to tighten portal-access controls.

## Links

- [Proposal §15 Azure Infrastructure Topology](../../BookingApp_Proposal.md)
- [Proposal §15.3 Bicep Module Structure](../../BookingApp_Proposal.md)
- [Proposal §16.2 Pipelines (Bicep what-if then deploy)](../../BookingApp_Proposal.md)
- [Proposal §21 R8 Bicep drift risk](../../BookingApp_Proposal.md)
- [Proposal §23.5 Sample Bicep — Container App for the API](../../BookingApp_Proposal.md)
- [Bicep documentation](https://learn.microsoft.com/azure/azure-resource-manager/bicep/)
- [Azure Verified Modules](https://aka.ms/avm)
