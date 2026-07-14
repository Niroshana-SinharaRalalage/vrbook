# VrBook — Claude briefing

Repo-scoped working context. This file is the **entry point + the invariants + the CI traps**. It does not hold the plan — the plan is the spec set under [`docs/`](docs/). Product: VrBook, a commission-free multi-tenant direct-booking platform (vacation rentals + hotels); guests search/book, tenants manage listings + confirm/reject + sync iCal + report, platform admin manages tenants.

## ⛔ MANDATORY BOOTSTRAP — every fresh session, in order, before any code

Full detail: [`docs/AGENT-PLAYBOOK.md`](docs/AGENT-PLAYBOOK.md) §1. In brief:
1. **`git pull --rebase`** on `develop`.
2. Read **this file** (invariants + traps), **[`docs/AGENT-PLAYBOOK.md`](docs/AGENT-PLAYBOOK.md)** (how to work), **[`docs/ENGINEERING-RULES.md`](docs/ENGINEERING-RULES.md)** (what good work is).
3. Read **[`docs/architecture/CURRENT-STATE.md`](docs/architecture/CURRENT-STATE.md)** — the as-built system.
4. Open **[`docs/stories/BOARD.md`](docs/stories/BOARD.md)** — the **single source of truth for story state**.
5. Identify your **lane** (kickoff prompt or [`docs/plan/EXECUTION-PLAN.md`](docs/plan/EXECUTION-PLAN.md)); if none was given, ask the owner — don't free-lance.
6. **Claim a story on the board** (first-push-wins lock), then read it + every `blocked-by` in full.
7. Execute under **TDD**, **in-lane only**, keep the **API suite green**; on finish, **self-heal** the board + docs.

**Do not free-lance. Do not skip the claim. Do not edit another lane's files.** The board is the lock; [`.github/CODEOWNERS`](.github/CODEOWNERS) is the backstop. A story whose code merged but whose board row still says `CLAIMED` is **not done**.

## Where the work is now

The 2026-07 planning program is **complete**. A full agent-consumable spec set exists and **supersedes `BookingApp_Proposal.md` and `docs/MASTER_PLAN.md`** as the working plan. Current execution = the **85-story backlog** (86 with the API-suite story), tracked on **[`docs/stories/BOARD.md`](docs/stories/BOARD.md)** and organised into waves/lanes toward launch.

> ⚠️ Phase 1 + 1.5 shipped to staging and the OPS.* launch-hardening slices (Pact, Playwright, INFRA) are **historical** — their close-outs + the old `MASTER_PLAN.md` live in [`docs/archive/`](docs/archive/). **Do NOT resume "OPS.2 / Playwright" as the next work.** The next work is the highest-priority `TODO` in your lane on the board.

**Spec-set map (read as the task needs):**
| Read | For |
|---|---|
| [`docs/architecture/CURRENT-STATE.md`](docs/architecture/CURRENT-STATE.md) | the whole as-built system in one read |
| [`docs/ops/CONFIG-INVENTORY.md`](docs/ops/CONFIG-INVENTORY.md) · [`docs/ops/CURRENT-GAPS.md`](docs/ops/CURRENT-GAPS.md) | every config/secret · the P0/P1/P2 defect register |
| [`docs/product/PRD.md`](docs/product/PRD.md) · [`OPEN-QUESTIONS.md`](OPEN-QUESTIONS.md) | requirements · locked product/design decisions |
| [`docs/architecture/PHASE-3-4-DESIGN.md`](docs/architecture/PHASE-3-4-DESIGN.md) (§0.5 authoritative) | post-launch rooms / cross-business cart / OTA design |
| [`docs/stories/INDEX.md`](docs/stories/INDEX.md) → the 6 `EPIC-*.md` | the 85 TDD-first stories + gap/correction traceability |
| [`docs/plan/EXECUTION-PLAN.md`](docs/plan/EXECUTION-PLAN.md) · [`docs/plan/AGENT-PROMPTS.md`](docs/plan/AGENT-PROMPTS.md) | lanes, file ownership, copy-paste kickoff prompts |
| [`docs/ops/CONFIG-MATRIX.md`](docs/ops/CONFIG-MATRIX.md) · [`docs/ops/GO-LIVE-RUNBOOK.md`](docs/ops/GO-LIVE-RUNBOOK.md) | per-env config · executable cutover |
| [`docs/TEST-STRATEGY.md`](docs/TEST-STRATEGY.md) | the API contract suite (VRB-300) — tooling, fixtures, auth |

Design principle (locked): *standardize the framework/machinery, localize the values per-property.* The cross-business cart + OTA bundling are **designed, not built** — Phase 3/4, post-launch.

**Environment**: staging web `https://ca-vrbook-web-staging.icydesert-abf3fa4e.eastus2.azurecontainerapps.io` + API `ca-vrbook-api-staging.icydesert-abf3fa4e.eastus2.azurecontainerapps.io`. Prod not deployed yet — the go-live epic (VRB-301–313) wires it. All CI green on `develop`.

## Owner-locked policies (invariant — do NOT re-derive, do NOT re-ask)

Policies the owner has locked. Assume they hold; don't propose alternatives unless the owner explicitly reopens them.

- **Auth: admins vs guests IdP surface** (2026-07-05).
  - **Platform Admin + Tenant Admin** → Entra-local email + password ONLY. NEVER Google / Microsoft / Facebook / Apple / any social IdP. Enforced by ADR-0016 + `ProvisionOrLinkUserHandler` Layer 1 + `AdminSocialIdpRejectionMiddleware` Layer 2.
  - **Guest** → email + password OR any social IdP.
  - When wiring MSAL / Entra user flows / social IdP config: admin flow gets email-only; guest flow gets email + socials. Do not merge into one flow. Do not add social buttons on the admin surface. See [`docs/adr/0016-admin-vs-social-idp-surface-split.md`](docs/adr/0016-admin-vs-social-idp-surface-split.md).
- **Admin accounts must be operator-pre-seeded before first sign-in** (2026-07-07). Guests self-serve; admins do NOT. OPS.M.22 is the slice that ships the pre-seed shape. Until then, "sign-in-first + manual promote via SQL / API" is the working shim.

## Stack

- **Backend:** .NET 8 modular monolith, MediatR, EF Core 8, Postgres 16 with per-context schemas. Modules: `Identity`, `Catalog`, `Booking`, `Pricing`, `Payment`, `Sync`, `Reviews`, `Messaging`, `Notifications`, `Loyalty`, `Admin`, `Reports`. See [`docs/adr/0001-modular-monolith.md`](docs/adr/0001-modular-monolith.md).
- **Frontend:** Next.js 14 App Router + React 18 + Tailwind + Vitest, `web/` folder. See [`docs/adr/0006-nextjs-app-router.md`](docs/adr/0006-nextjs-app-router.md).
- **Auth:** Entra External ID (CIAM) + MSAL Browser 3.x + JwtBearer. Global roles via Entra App Roles → `ClaimTypes.Role`; per-tenant roles via `identity.tenant_memberships` → `ICurrentUser.HasTenantRole(tid, role)`. See [`docs/adr/0012-entra-external-id-over-b2c.md`](docs/adr/0012-entra-external-id-over-b2c.md) + [`docs/adr/0014-app-roles-global-db-per-tenant.md`](docs/adr/0014-app-roles-global-db-per-tenant.md) + [`docs/adr/0016-admin-vs-social-idp-surface-split.md`](docs/adr/0016-admin-vs-social-idp-surface-split.md).
- **Infra:** Bicep, Azure Container Apps + Container App Jobs, ACR, Key Vault, App Insights + Log Analytics, Postgres Flexible Server, ACS Email. See `infra/main.bicep`.
- **CI/CD:** GitHub Actions. `cd-staging-api.yml` (backend + infra + workers + migrator + smoke) and `cd-staging-web.yml` (Next.js + Docker + smoke). Both deploy to Azure Container Apps in `rg-vrbook-staging`.

## History (archived — do NOT resume as active work)

Phase 1 (product, Slices 0–7) + Phase 1.5 (multi-tenancy + Entra External ID auth, OPS.M.0–22 + INFRA.1) shipped to staging. The launch-hardening OPS.1 / OPS.2 / INFRA.3 slices (Pact contract tests, Playwright E2E suite, scale-to-zero-aware deploy convergence) are eng-complete. Their plans + close-outs + the old `MASTER_PLAN.md` now live under [`docs/archive/`](docs/archive/) for provenance — the launch is now driven by the **go-live epic (VRB-301–313)**, which still consumes `docs/OPS_LAUNCH_COMPLETION_PLAN.md` for the OPS.1–8 critical path (that one stays live until VRB-3xx fully absorbs it). Two facts those slices settled are still load-bearing and repeated here so they don't get lost:

1. **Owner-locked policies** (above) — admin-vs-social IdP split (ADR-0016) + admin pre-seed (ADR-0017).
2. **Role authority shape** (below) — frozen post-M.21.

Post-M.21 role authority shape (frozen):
- **Global:** `identity.users.is_platform_admin` boolean → materialized as `ClaimTypes.Role="PlatformAdmin"` by `UserProvisioningMiddleware`.
- **Per-tenant:** `identity.tenant_memberships.role` string (`"tenant_admin"` today; `"tenant_member"` reserved) → materialized as `ICurrentUser.MembershipRoles` dictionary + `HasTenantRole(tenantId, role)`.
- **NO** `IsOwner`/`IsAdmin` accessors, DTO fields, DB columns, extension_* claim reads, or `[Authorize(Roles="Owner,Admin")]` decorators anywhere.

## Working pattern

**The operating model is [`docs/AGENT-PLAYBOOK.md`](docs/AGENT-PLAYBOOK.md) (how to pick up + hand off work) + [`docs/ENGINEERING-RULES.md`](docs/ENGINEERING-RULES.md) (what good work is).** Those are authoritative; the reminders below are the load-bearing ones.

- **Architect consult before any multi-module / architectural / sequencing plan.** Commit the architect's plan as a doc for owner review BEFORE executing. [`feedback_consult_architect_for_planning`](../.claude/projects/c--Work-BookingApp/memory/feedback_consult_architect_for_planning.md).
- **Technical questions are architect's call, not owner's** — owner directive 2026-07-06. Adopt architect recommendations directly; only product/policy questions go to the owner. [`feedback_technical_decisions_are_architect_call`](../.claude/projects/c--Work-BookingApp/memory/feedback_technical_decisions_are_architect_call.md).
- **User pushback = more coverage, not less.** When the owner challenges a deferral or partial scope, default to FULL scope. Architect verifies feasibility/order; owner sets breadth/depth.
- **Scope deferral is an architect consult** BEFORE the deferral — not a close-out decision.
- **RED-then-GREEN arch tests when useful.** Land arch tests intentionally RED with a documented expected-failure count; each GREEN commit flips one or more facts.
- **Ship complete vertical slices.** Never call a feature "done" without UI. Backend-shipped-without-UI is NOT done: UI → API → DB → deploy → UX.
- **Stay in lane, claim on the board, keep the API suite green, self-heal the docs on finish** (playbook §2/§5/§6).

## CI + local-vs-CI gotchas

Load-bearing traps captured as reference memories; ignore at your own peril:

- **CI Docker analyzer stricter than local** — `mcr.microsoft.com/dotnet/sdk:8.0` fires `CA1822` as error where local SDK only fires `S2325`. `dotnet build`/`dotnet test` locally miss it; `dotnet publish -c Release` catches it. Also `dotnet format --verify-no-changes` treats Sonar `S3878` (redundant array-creation in params-friendly method) as error. Pre-push:
    ```
    dotnet format src/VrBook.sln --verify-no-changes --no-restore
    dotnet publish src/VrBook.Api/VrBook.Api.csproj -c Release
    dotnet test --filter "Category!=Integration"
    ```
- **KV secret bind before Bicep deploy** — Container App `secretRef` binds resolve KV secrets at revision-provision time. Any NEW secret referenced from `infra/main.bicep` MUST be seeded to Key Vault BEFORE the deploy, or the whole `main.bicep` fails atomically. Pre-seed with `pending-identity-setup` via `az keyvault secret set` before push. Also add the seed line to `infra/scripts/10-store-secrets.ps1` for durable RG bootstrap.
- **Container App manual revision-bump image trap** — `az containerapp update --revision-suffix` inherits the parent template's image (Bicep placeholder is `mcr.microsoft.com/azuredocs/containerapps-helloworld:latest`). Always pass `--image <current-healthy-tag>` explicitly.
- **`Category!=Integration` filter includes NoCategory tests.** CI runs the Category-not-Integration filter. Local `Category=Unit` filter is narrower; may skip failing tests. Match the CI filter locally before push.
- **Doc-only commits don't trigger CI.** `cd-staging-api.yml` path filters exclude `docs/**` + `*.md`. Don't `gh run watch` for a doc-only push; the previous code commit's run is the gate.
- **After every push: `gh run watch <run-id> --exit-status`.** Wait for green before writing close-outs. Watch API + Web CI separately when a commit touches both.

## Local dev

- **Repo root:** `c:\Work\BookingApp`. Windows Git Bash + PowerShell environment.
- **Solution:** `src/VrBook.sln`.
- **Backend build:** `dotnet build src/VrBook.sln -c Release --no-restore`.
- **Unit tests (safe locally without Docker):** `dotnet test src/VrBook.sln --filter Category=Unit --nologo`.
- **Integration tests:** require Docker (Testcontainers spins Postgres). Not runnable when Docker is off.
- **Web dev:** `cd web && npm install && npm run dev`. Vitest via `npx vitest run`. TypeScript via `npx tsc --noEmit`.
- **Migrations:** `dotnet ef migrations add <Name> --context <Context>DbContext --project src/Modules/VrBook.Modules.<Mod>/VrBook.Modules.<Mod>.csproj --startup-project src/VrBook.Migrator/VrBook.Migrator.csproj`. The migrator's `HostAbortedException` on run is expected (design-time host pattern).

## Staging environment

- **Resource group:** `rg-vrbook-staging`.
- **Postgres:** `psql-vrbook-staging-v2.postgres.database.azure.com`, DB `vrbook`, user `vrbook_admin`. Public-network + IP-firewalled. Owner-Home-Office IP `174.104.204.213` + `AllowAzureServices` + CAE-Outbound `135.18.171.52` on the firewall. `postgres` (the built-in system DB on the same server) is NOT the app DB; every connection string must set `Database=vrbook`. INFRA.1 shipped with `Database=postgres` by accident and cost a full session to unwind on 2026-07-08 — the Bicep `vrbook` DB child resource + `10-store-secrets.ps1` placeholder guard against a repeat.
- **API:** `ca-vrbook-api-staging` Container App. Image tag = commit SHA.
- **Web:** `ca-vrbook-web-staging` Container App. Image tag = commit SHA.
- **ACR:** `crvrbookstaging.azurecr.io`.
- **KV:** `kv-vrbook-staging`. `postgres-cs` holds full connection string. `entra-*` secrets hold Entra tenant + client IDs + user-flow authority URLs. Social IdP secrets (Google + Facebook + Apple) documented in [`docs/runbooks/social_idp_setup.md`](docs/runbooks/social_idp_setup.md); seeded as `pending-identity-setup` until an operator fills them.

## Where the plans live

- **[`docs/stories/BOARD.md`](docs/stories/BOARD.md)** — story state (SoT). **First stop for "what's next."**
- **[`docs/stories/INDEX.md`](docs/stories/INDEX.md) → `docs/stories/EPIC-*.md`** — the 85 stories (each is TDD-first, with its own DoD).
- **[`docs/plan/EXECUTION-PLAN.md`](docs/plan/EXECUTION-PLAN.md) + [`docs/plan/AGENT-PROMPTS.md`](docs/plan/AGENT-PROMPTS.md)** — waves, lanes, file ownership, kickoff prompts.
- **[`docs/AGENT-PLAYBOOK.md`](docs/AGENT-PLAYBOOK.md) + [`docs/ENGINEERING-RULES.md`](docs/ENGINEERING-RULES.md)** — the operating model + engineering standards.
- `docs/adr/<N>-<slug>.md` — architectural decisions. ADR-0012/0014/0016/0017 are the authorization axis; ADR-0001 is the modular monolith.
- `docs/runbooks/*.md` — operator procedures.
- `docs/archive/*` — completed OPS/slice plans + close-outs + the old `MASTER_PLAN.md` (provenance only; not active).

## What NOT to do

- **Don't resume the OPS.* slices or `MASTER_PLAN.md` as the plan** — they're archived. The plan is the board + epics.
- **Don't start work without a `CLAIMED` row on [`docs/stories/BOARD.md`](docs/stories/BOARD.md), and don't edit files outside your lane.** The board is the lock; CODEOWNERS is the backstop.
- **Don't add `IsOwner` / `IsAdmin` / `Owner,Admin` role literals anywhere.** Arch tests `OpsM15_*` + `OpsM17_*` + `SiteHeaderNav-noLegacyDtoReads` fail loud on regression. Use `HasTenantRole(tid, "tenant_admin")` for tenant-scoped writes and `IsPlatformAdmin` for cross-tenant operator surface.
- **Don't delete the Entra `Owner` / `Admin` App Role definitions on `vrbook-api-<env>` app registration** without a further ADR amendment. Post-M.21 they're safe to delete (no code reads them), but leaving them in place is the low-risk default.
- **Don't hand-edit EF migration `Designer.cs` / `IdentityDbContextModelSnapshot.cs`.** Regenerate via `dotnet ef migrations add`.
- **Don't add new `[Authorize(Roles=...)]` decorators unless the role is `PlatformAdmin`.** The M.15 pattern is `[Authorize]` on the controller + `HasTenantRole` at the handler where role-string is load-bearing.
- **Don't skip CI hooks (`--no-verify`, `--no-gpg-sign`) unless explicitly asked.** If a hook fails, investigate and fix the underlying issue.
- **Don't commit secrets.** Even `pending-identity-setup` placeholders should live only in KV, not source. `.env.example` files contain shape-only placeholders.
