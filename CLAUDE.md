# VrBook — Claude briefing

Repo-scoped context so a fresh session doesn't have to re-derive everything. Not a design doc — a working briefing. Full design lives in [`docs/MASTER_PLAN.md`](docs/MASTER_PLAN.md) + [`docs/adr/`](docs/adr/) + `OPS_M_*_PLAN.md` / `OPS_M_*_CLOSE_OUT.md`.

## Project overview

**Product**: VrBook — direct-booking vacation rental platform. Guests search + book properties; property owners (tenants) manage listings, confirm/reject bookings, chat with guests, sync iCal feeds, run reports; platform admin manages tenants. Multi-tenant SaaS on Azure. Full product spec at [`docs/BookingApp_Proposal.md`](docs/BookingApp_Proposal.md).

**Where we are (2026-07-09)**: 🎉 Phase 1 (product) complete + Phase 1.5 (multi-tenancy + Entra auth) complete. Every slice ✅ on staging with CI green. Launch-hardening in progress: OPS.1 shape-complete (Pact), OPS.2 in progress (Playwright). Currently ~17 of ~23 tracked slices shipped.

**Path to production** (per Option A sequencing locked 2026-06-27 in MASTER_PLAN §2):

| Phase | Slices | State | What "done" means |
|---|---|---|---|
| **Phase 1** — product | Slices 0–7 | ✅ | Booking lifecycle + notifications + reviews/loyalty + chat/pricing + reports/realtime |
| **Phase 1.5** — multi-tenancy | OPS.M.0–22 + INFRA.1 | ✅ | Tenant aggregate + Entra External ID + admin/social auth split + admin pre-seed + Stripe Connect + RLS |
| **Phase 1 launch-ready** | OPS.1–8 (7 slices) | 🚧 in progress | Pact + Playwright + k6 load + ZAP + Trivy + Stripe key rotation + DKIM. **After OPS.8: prod launch is unblocked.** |
| **Phase 3** — hotel rooms | Slices 8+9 | ⏭ post-launch | Rooms-within-property + multi-unit cart (one guest books N rooms in one Stripe checkout) |
| **Phase 4** — OTA bundling | Slice 10 | ⏭ post-launch | Cross-tenant itineraries (Stay + Flight + Car + Activity) with FX + per-leg cancellation |

See [`docs/MASTER_PLAN.md`](docs/MASTER_PLAN.md) §1 for the row-per-slice table (with commit ranges + close-out doc links) and §3 for the full "what done means" per phase.

**Environment**: staging live at `https://ca-vrbook-web-staging.icydesert-abf3fa4e.eastus2.azurecontainerapps.io` (web) + `ca-vrbook-api-staging.icydesert-abf3fa4e.eastus2.azurecontainerapps.io` (API). Prod not deployed yet — cutover blocked on OPS.1–8. All CI green on develop.

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

## Phase 1 slice state (as of 2026-07-09)

🎉 **Phase 1 complete** (Slices 0–7 all ✅ on staging). Phase 1.5 shipped through OPS.M.22. MASTER_PLAN is the authoritative index; key recent close-outs below.

**2026-07-09 milestone**: Slices 5 + 6 + 7 all confirmed ✅ via gap analysis — every deliverable in each slice's plan had already been delivered incrementally through the OPS.M.* stack (mostly M.13.6, M.16, Slice 4 V2, and the Slice 6/7 commits themselves). Phase 1 finished quietly during Phase 1.5 execution. Follow-ups filed as **Slice OPS.M.23 candidates**: ETag on `/threads`, `ThreadByBookingFilterTests`, 4 report handler integration tests, `NegotiateEndpointTests`, `useTentativeBookingPush.test.ts`. None block core functionality; all are polish/coverage.

**Slice OPS.1 — Pact contract tests (shape-complete 2026-07-09)**: 5 of 8 sub-commits landed. Consumer harness (@pact-foundation/pact@13.2.0) + 1 working interaction + CI drift gate + provider verifier fixture scaffold (`PactVerifierFixture : TwoTenantApiFixture`) + ADR-0018 flow 6 carve-out + drift runbook. `contracts/pacts/vrbook-web-vrbook-api.json` git-committed (1 interaction, deterministic sha256-verified). Close-out at [`docs/OPS_1_CLOSE_OUT.md`](docs/OPS_1_CLOSE_OUT.md); ADR at [`docs/adr/0018-pact-scope-and-flow-6-carve-out.md`](docs/adr/0018-pact-scope-and-flow-6-carve-out.md); runbook at [`docs/runbooks/pact-contract-drift.md`](docs/runbooks/pact-contract-drift.md). Two engineering-discovery follow-ups filed as **OPS.1.9**: (a) WAF-vs-Kestrel adapter for PactNet's verifier (WebApplicationFactory uses TestServer, in-process only); (b) PactV3 mock-server "Worker exited unexpectedly" when a single provider const is reused across multiple `executeTest` calls (root cause: Pact's Rust core cleanup). OPS.1.9 will land the actual verifier call + 11 remaining pact interactions.

**Slice OPS.2 — Playwright E2E suite (in progress 2026-07-09)**: architect plan committed as [`docs/OPS_2_PLAYWRIGHT_PLAN.md`](docs/OPS_2_PLAYWRIGHT_PLAN.md). All §5 policy questions locked to architect recommendation per owner directive 2026-07-09. 8 sub-commits planned; OPS.2.1 shipped (`21f5075`) — plan doc + `web/tests/e2e/` directory skeleton. OPS.2.2 shipped 2026-07-09 — base infra: `fixtures/personas.ts` (3 personas, env-var passwords, never logged) + `fixtures/auth.fixture.ts` (re-injects sessionStorage since MSAL uses `cacheLocation:'sessionStorage'`, which Playwright `storageState` omits) + `global-setup.ts` (setup-project pattern, one real Entra-CIAM MSAL redirect sign-in per persona → `.auth/<persona>.storageState.json` + `.session.json`) + base POMs (`BasePage`/`HomePage`) + `support/{testTenant,stripeTestCards}.ts` + `playwright.config.ts` reshape (retired blanket `e2e`; added `setup` + 3 `<persona>-authed` projects, Chromium-only, `webServer` undefined → deployed staging). Backend/infra: `is_e2e` nullable col on `identity.tenants` (migration `OpsM2_TenantsAddIsE2eColumn`, mapped as EF shadow property — no domain behaviour) + `VrBook.Migrator.SeedE2EBackfill` (idempotent; seeds isolated `e2e-tenant` + pre-seeded `e2e-owner` tenant_admin + `e2e-platform-admin`; guest lazy-provisions, NOT seeded; gated on `Bootstrap:E2e:Enabled`, staging-only) + Bicep param `bootstrapE2eTenantEnabled` (staging true / prod false) → migrator env `Bootstrap__E2e__Enabled` + three `e2e-*-password` KV placeholders in `10-store-secrets.ps1`. **Live MSAL sign-in unverified in-session** — the three Entra CIAM personas + real KV passwords are operator-provisioned (OPS.2.8 §7 walk); global-setup selectors (`loginfmt`/`passwd`/`idSIButton9`) validate on first live run.

OPS.2.3 shipped 2026-07-10 — anonymous smoke suite (5 `*.smoke.spec.ts` under `anonymous/`: home renders, property search shell, property detail by slug, unauthenticated quote auto-calc, web `/api/health`) + blocking `playwright-smoke` job in `cd-staging-web.yml` after the curl `smoke` (warms both web + API origins first since staging scales to zero, then `npx playwright test --project=smoke`, Chromium only). Architect consult (2026-07-10) resolved the smoke-fixture strategy: `SeedE2EBackfill` extended (OPS.2.3a) to also seed ONE deterministic public property (`slug='e2e-smoke-property'`, fixed GUID `e2e00000-…-001`, `is_active=true` to clear the Catalog public-read RLS carve-out) + a USD pricing plan whose `tenant_id` matches (load-bearing for the quote's RLS-scoped plan read); no booking/availability rows needed. Playwright config got cold-start-tolerant timeouts. TS/C# fixture constants mirrored (`support/testTenant.ts` ↔ `SeedE2EBackfill.cs`).

**Next unshipped work: OPS.2.4** — guest authed flows (~10 `guest/*.spec.ts`, `guest-authed` project, nightly-informational). Then OPS.2.5 owner (~9) + OPS.2.6 platform-admin + auth-edge (~6); OPS.2.7 nightly workflow + runbook + arch tests; OPS.2.8 close-out + ADR-0019 + MASTER_PLAN row 17 flip. Authed scenarios depend on the operator provisioning the three Entra CIAM personas + KV passwords (OPS.2.8 §7 walk) before they can go green.

- **OPS.M.12** — Social IdPs (Google + Microsoft consumer + Facebook + Apple) via `GuestSignUpSignIn` + admin-vs-social surface split. Owner-locked policy: admins Entra-local only. Two-layer defence (REFUSE-AT-PROVISIONING + middleware belt). See [`docs/OPS_M_12_CLOSE_OUT.md`](docs/OPS_M_12_CLOSE_OUT.md) + [`docs/runbooks/social_idp_setup.md`](docs/runbooks/social_idp_setup.md).
- **OPS.INFRA.1** — Staging Postgres public-access rebuild (V1 → V2 blue/green). V2 = `psql-vrbook-staging-v2.postgres.database.azure.com`. See [`docs/OPS_INFRA_1_STAGING_POSTGRES_PUBLIC_REBUILD_PLAN.md`](docs/OPS_INFRA_1_STAGING_POSTGRES_PUBLIC_REBUILD_PLAN.md).
- **OPS.M.15** — App-role legacy claim reads + `[Authorize(Roles="Owner,Admin")]` drop. 7 sub-commits; 15 arch facts. Close-out at [`docs/OPS_M_15_CLOSE_OUT.md`](docs/OPS_M_15_CLOSE_OUT.md).
- **OPS.M.16** — Turnover-aware completion. Property `TurnoverHours` + `CompletionDueAt` snapshot + sweep predicate. Close-out at [`docs/OPS_M_16_CLOSE_OUT.md`](docs/OPS_M_16_CLOSE_OUT.md).
- **OPS.M.17** — Handler-level `HasTenantRole` guards on 4 tenant-scoped admin surfaces (Notifications retry, SyncConflicts resolve, ChannelFeeds CRUD, Reviews moderation). Closes M.15 §3 medium-medium intra-tenant exposure.
- **OPS.M.18** — M.16 polish: calendar `awaitingTurnover` overlay + [`docs/runbooks/turnover_walk.md`](docs/runbooks/turnover_walk.md).
- **OPS.M.19** — `RespondToReviewHandler` property-ownership guard (owner-response endpoint; NOT tenant_admin bypass — different semantics).
- **OPS.M.20** — `TurnoverAwareCompletionTests` integration test pack (6 scenarios, `Category=Integration`, Postgres testcontainer).
- **OPS.M.21** — M.15 App Roles cleanup finalization (3 atomic sub-commits): SPA nav reshape → `UserDto.IsOwner`/`IsAdmin` drop → DB column drop. ADR-0014 amendment #2 marks the closure. Rollback runbook at [`docs/OPS_M_15_APP_ROLES_CLEANUP_FOLLOWUP_ROLLBACK.md`](docs/OPS_M_15_APP_ROLES_CLEANUP_FOLLOWUP_ROLLBACK.md).
- **OPS.M.12.9 (2026-07-08)** — Google IdP wired end-to-end on staging. Google Cloud OAuth 2.0 client provisioned; `google-oauth-client-id` + `google-oauth-client-secret` in KV; Entra CIAM `GuestSignUpSignIn` user flow created with Email + Google + Microsoft; both `vrbook-web-staging` (SPA) + `vrbook-api-staging` (API) app registrations assigned to the flow; `email` + `verified_primary_email` optional claims added to both apps (ID + Access); legacy `extn.isAdmin` / `extn.isOwner` optional claims removed from Access token; MSAL `apiScopes` bumped to include `email`; `UserProvisioningMiddleware` gains `verified_primary_email` as email-fallback claim ahead of `preferred_username`. `AdminSignUpSignIn` flow NOT yet created (owner-lock preserved because admin authority currently email+password only via the existing tenant-default flow; explicit split flow lands with OPS.M.22).
- **OPS.M.12.10 (2026-07-08)** — Role badge in site header. `SiteHeaderRoleBadge` chip shows "Platform Admin" (solid brand-orange) for PlatformAdmin sessions or "Tenant Admin — {tenantName}" (outlined brand-maroon) for `tenant_admin` membership sessions; renders nothing for guests. Data sources match `SiteHeaderNav` (post-M.21 ADR-0014 shape): `useMe().isPlatformAdmin` + `useMyTenants().memberships`. PlatformAdmin trumps any `tenant_admin` membership; picks `isPrimary` membership first else first found. 8 unit tests pin every render state.
- **Known gap — Google guest access token missing `idp` claim.** ID token has `idp: google.com` correctly; access token does not, so `UserProvisioningMiddleware` classifies federated Google users as `provider=entra` in `identity.user_identities`. `idp` isn't in Entra CIAM's Optional Claims UI — needs a Manifest edit or a resource-app-side hook. Filed as follow-up **OPS.M.12.11** (small, ~half day). Doesn't block guest usage; only matters if a guest is later promoted to admin because Layer 1 REFUSE-AT-PROVISIONING gates on `provider` string.
- **OPS.M.22 (2026-07-08 → 2026-07-09)** — Admin pre-seed slice shipped end-to-end. 8 sub-commits (`b1f9c8e` RED arch tests → M.22.8 close-out). Ships `identity.users.pre_seeded_at` column + `SeedAdminUserCommand` + `POST /api/v1/admin/platform/users/seed` + middleware admin-gate (`AdminAccountNotProvisionedException` 401 with `admin_account_not_provisioned` problem type) + SPA `/auth/admin-not-provisioned` page + `vrbook-admin.ps1` operator cmdlet + Bicep-declarative backfill (`seedPlatformAdmins` array param → migrator's `SeedPlatformAdminsBackfill` service). Legacy `grant-self-admin.ps1` deleted (post-M.15 dead). See [`docs/OPS_M_22_CLOSE_OUT.md`](docs/OPS_M_22_CLOSE_OUT.md) + [`docs/adr/0017-admin-preseed-required.md`](docs/adr/0017-admin-preseed-required.md). Two known follow-ups: OPS.M.22.10 (strong /me contract for admin-not-provisioned, currently heuristic on 401/403), OPS.M.22.11 (operator staging walk runbook).

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
- **Postgres:** `psql-vrbook-staging-v2.postgres.database.azure.com`, DB `vrbook`, user `vrbook_admin`. Public-network + IP-firewalled. Owner-Home-Office IP `174.104.204.213` + `AllowAzureServices` + CAE-Outbound `135.18.171.52` on the firewall. `postgres` (the built-in system DB on the same server) is NOT the app DB; every connection string must set `Database=vrbook`. INFRA.1 shipped with `Database=postgres` by accident and cost a full session to unwind on 2026-07-08 — the Bicep `vrbook` DB child resource + `10-store-secrets.ps1` placeholder guard against a repeat.
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
