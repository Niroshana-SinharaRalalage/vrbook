# VrBook — Claude briefing

Repo-scoped context so a fresh session doesn't have to re-derive everything. Not a design doc — a working briefing. Full design lives in [`docs/MASTER_PLAN.md`](docs/MASTER_PLAN.md) + [`docs/adr/`](docs/adr/) + `OPS_M_*_PLAN.md` / `OPS_M_*_CLOSE_OUT.md`.

## Stack

- **Backend:** .NET 8 modular monolith, MediatR, EF Core 8, Postgres 16 with per-context schemas. Modules: `Identity`, `Catalog`, `Booking`, `Pricing`, `Payment`, `Sync`, `Reviews`, `Messaging`, `Notifications`, `Loyalty`, `Admin`, `Reports`. See [`docs/adr/0001-modular-monolith.md`](docs/adr/0001-modular-monolith.md).
- **Frontend:** Next.js 14 App Router + React 18 + Tailwind + Vitest, `web/` folder. See [`docs/adr/0006-nextjs-app-router.md`](docs/adr/0006-nextjs-app-router.md).
- **Auth:** Entra External ID (CIAM) + MSAL Browser 3.x + JwtBearer. Global roles via Entra App Roles → `ClaimTypes.Role`; per-tenant roles via `identity.tenant_memberships` → `ICurrentUser.HasTenantRole(tid, role)`. See [`docs/adr/0012-entra-external-id-over-b2c.md`](docs/adr/0012-entra-external-id-over-b2c.md) + [`docs/adr/0014-app-roles-global-db-per-tenant.md`](docs/adr/0014-app-roles-global-db-per-tenant.md) + [`docs/adr/0016-admin-vs-social-idp-surface-split.md`](docs/adr/0016-admin-vs-social-idp-surface-split.md).
- **Infra:** Bicep, Azure Container Apps + Container App Jobs, ACR, Key Vault, App Insights + Log Analytics, Postgres Flexible Server, ACS Email. See `infra/main.bicep`.
- **CI/CD:** GitHub Actions. `cd-staging-api.yml` (backend + infra + workers + migrator + smoke) and `cd-staging-web.yml` (Next.js + Docker + smoke). Both deploy to Azure Container Apps in `rg-vrbook-staging`.

## Phase 1 slice state (as of 2026-07-06)

All slices shipped to staging; MASTER_PLAN is the authoritative index. Key recent close-outs:

- **OPS.M.12** — Social IdPs (Google + Microsoft consumer + Facebook + Apple) via `GuestSignUpSignIn` + admin-vs-social surface split. Owner-locked policy: admins Entra-local only. Two-layer defence (REFUSE-AT-PROVISIONING + middleware belt). See [`docs/OPS_M_12_CLOSE_OUT.md`](docs/OPS_M_12_CLOSE_OUT.md) + [`docs/runbooks/social_idp_setup.md`](docs/runbooks/social_idp_setup.md).
- **OPS.INFRA.1** — Staging Postgres public-access rebuild (V1 → V2 blue/green). V2 = `psql-vrbook-staging-v2.postgres.database.azure.com`. See [`docs/OPS_INFRA_1_STAGING_POSTGRES_PUBLIC_REBUILD_PLAN.md`](docs/OPS_INFRA_1_STAGING_POSTGRES_PUBLIC_REBUILD_PLAN.md).
- **OPS.M.15** — App-role legacy claim reads + `[Authorize(Roles="Owner,Admin")]` drop. 7 sub-commits; 15 arch facts. Close-out at [`docs/OPS_M_15_CLOSE_OUT.md`](docs/OPS_M_15_CLOSE_OUT.md).
- **OPS.M.16** — Turnover-aware completion. Property `TurnoverHours` + `CompletionDueAt` snapshot + sweep predicate. Close-out at [`docs/OPS_M_16_CLOSE_OUT.md`](docs/OPS_M_16_CLOSE_OUT.md).
- **OPS.M.17** — Handler-level `HasTenantRole` guards on 4 tenant-scoped admin surfaces (Notifications retry, SyncConflicts resolve, ChannelFeeds CRUD, Reviews moderation). Closes M.15 §3 medium-medium intra-tenant exposure.
- **OPS.M.18** — M.16 polish: calendar `awaitingTurnover` overlay + [`docs/runbooks/turnover_walk.md`](docs/runbooks/turnover_walk.md).
- **OPS.M.19** — `RespondToReviewHandler` property-ownership guard (owner-response endpoint; NOT tenant_admin bypass — different semantics).
- **OPS.M.20** — `TurnoverAwareCompletionTests` integration test pack (6 scenarios, `Category=Integration`, Postgres testcontainer).
- **OPS.M.21** — M.15 App Roles cleanup finalization (3 atomic sub-commits): SPA nav reshape → `UserDto.IsOwner`/`IsAdmin` drop → DB column drop. ADR-0014 amendment #2 marks the closure. Rollback runbook at [`docs/OPS_M_15_APP_ROLES_CLEANUP_FOLLOWUP_ROLLBACK.md`](docs/OPS_M_15_APP_ROLES_CLEANUP_FOLLOWUP_ROLLBACK.md).

Post-M.21 role authority shape (frozen):
- **Global:** `identity.users.is_platform_admin` boolean → materialized as `ClaimTypes.Role="PlatformAdmin"` by `UserProvisioningMiddleware`.
- **Per-tenant:** `identity.tenant_memberships.role` string (`"tenant_admin"` today; `"tenant_member"` reserved) → materialized as `ICurrentUser.MembershipRoles` dictionary + `HasTenantRole(tenantId, role)`.
- **NO** `IsOwner`/`IsAdmin` accessors, DTO fields, DB columns, extension_* claim reads, or `[Authorize(Roles="Owner,Admin")]` decorators anywhere.

## Working pattern

- **Architect consult before any multi-module / architectural / sequencing plan.** Commit the architect's plan as `docs/OPS_M_*_PLAN.md` for owner review BEFORE executing. Rule captured at [`feedback_consult_architect_for_planning`](../.claude/projects/c--Work-BookingApp/memory/feedback_consult_architect_for_planning.md).
- **Technical questions in a plan's §5 are architect's call, not owner's** — owner directive 2026-07-06. Adopt architect recommendations directly; consult architect again if unsure. [`feedback_technical_decisions_are_architect_call`](../.claude/projects/c--Work-BookingApp/memory/feedback_technical_decisions_are_architect_call.md).
- **User pushback = more coverage, not less.** When owner challenges a deferral or partial scope, default to FULL scope. Architect verifies feasibility/order; owner sets breadth/depth.
- **Scope deferral is an architect consult.** Deferring a non-trivial subset of a locked slice plan's steps requires architect consultation BEFORE the deferral; §11 close-out is for documenting what shipped.
- **RED-then-GREEN arch tests when useful.** Land arch tests intentionally RED on develop with a documented expected-failure count in the commit message; each subsequent GREEN commit flips one or more facts. See OPS.M.15.1 for the pattern.
- **Ship complete vertical slices.** Never call a feature "done" without UI. Backend-shipped-without-UI is NOT done. Enterprise-architect scope: UI → API → DB → deploy → UX.

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
- **Postgres:** `psql-vrbook-staging-v2.postgres.database.azure.com`, DB `postgres` (yes, not `vrbook`), user `vrbook_admin`. Public-network + IP-firewalled. Owner-Home-Office IP `174.104.204.213` + `AllowAzureServices` + CAE-Outbound `135.18.171.52` on the firewall.
- **API:** `ca-vrbook-api-staging` Container App. Image tag = commit SHA.
- **Web:** `ca-vrbook-web-staging` Container App. Image tag = commit SHA.
- **ACR:** `crvrbookstaging.azurecr.io`.
- **KV:** `kv-vrbook-staging`. `postgres-cs` holds full connection string. `entra-*` secrets hold Entra tenant + client IDs + user-flow authority URLs. Social IdP secrets (Google + Facebook + Apple) documented in [`docs/runbooks/social_idp_setup.md`](docs/runbooks/social_idp_setup.md); seeded as `pending-identity-setup` until an operator fills them.

## Where the plans live

- `docs/MASTER_PLAN.md` — the phase table + slice status. First stop for "where are we."
- `docs/OPS_M_<N>_PLAN.md` — pre-slice architect brief for slice `<N>`. Read before executing.
- `docs/OPS_M_<N>_CLOSE_OUT.md` — post-slice retrospective. What shipped + divergences + follow-ups + rollback.
- `docs/adr/<N>-<slug>.md` — architectural decisions. ADR-0012/0014/0016 are the authorization axis; ADR-0001 is the modular monolith.
- `docs/runbooks/*.md` — operator procedures. `social_idp_setup.md` (M.12) + `turnover_walk.md` (M.16 polish) are the recent ones.

## What NOT to do

- **Don't add `IsOwner` / `IsAdmin` / `Owner,Admin` role literals anywhere.** Arch tests `OpsM15_*` + `OpsM17_*` + `SiteHeaderNav-noLegacyDtoReads` fail loud on regression. Use `HasTenantRole(tid, "tenant_admin")` for tenant-scoped writes and `IsPlatformAdmin` for cross-tenant operator surface.
- **Don't delete the Entra `Owner` / `Admin` App Role definitions on `vrbook-api-<env>` app registration** without a further ADR amendment. Post-M.21 they're safe to delete (no code reads them), but leaving them in place is the low-risk default.
- **Don't hand-edit EF migration `Designer.cs` / `IdentityDbContextModelSnapshot.cs`.** Regenerate via `dotnet ef migrations add`.
- **Don't add new `[Authorize(Roles=...)]` decorators unless the role is `PlatformAdmin`.** The M.15 pattern is `[Authorize]` on the controller + `HasTenantRole` at the handler where role-string is load-bearing.
- **Don't skip CI hooks (`--no-verify`, `--no-gpg-sign`) unless explicitly asked.** If a hook fails, investigate and fix the underlying issue.
- **Don't commit secrets.** Even `pending-identity-setup` placeholders should live only in KV, not source. `.env.example` files contain shape-only placeholders.
