# EPIC — Staging → Production Go-Live (VRB-3xx)

> **Every story here inherits the global [Definition of Ready + Definition of Done](../ENGINEERING-RULES.md#definition-of-ready-before-you-write-the-first-test).** Before code: **claim the story on [`BOARD.md`](BOARD.md)** (first-push-wins), read it + its `blocked-by`, and **grep for an existing implementation before building one**. TDD; **write API contract tests for every endpoint you touch and keep the VRB-300 suite green**; stay in your lane ([`../plan/EXECUTION-PLAN.md`](../plan/EXECUTION-PLAN.md)); on finish **self-heal the board + docs**. Operating model: [`../AGENT-PLAYBOOK.md`](../AGENT-PLAYBOOK.md). Each story's own DoD is *in addition to* the global one.

- **Epic owner:** Platform Enterprise Architect (agent) + Owner (operator-gated items).
- **Date:** 2026-07-13.
- **Status:** Backlog — stories written; execution follows `docs/OPS_LAUNCH_COMPLETION_PLAN.md` critical-path.
- **Scope:** everything required to take VrBook from "functionally complete on staging" to "live, monitored, revenue-taking production." This epic is the **story-level** decomposition of the launch. It does **not** re-plan the OPS.1–OPS.8 hardening slices — those are already sequenced in [`docs/OPS_LAUNCH_COMPLETION_PLAN.md`](../OPS_LAUNCH_COMPLETION_PLAN.md); this epic **references and consumes** them and fills the go-live-specific gaps (G23–G36) that plan flags but does not itself deliver as stories.
- **Sources of truth:** [`docs/OPS_LAUNCH_COMPLETION_PLAN.md`](../OPS_LAUNCH_COMPLETION_PLAN.md) · [`docs/architecture/CURRENT-STATE.md`](../architecture/CURRENT-STATE.md) §13 · [`docs/ops/CURRENT-GAPS.md`](../ops/CURRENT-GAPS.md) §D/§E · [`docs/product/PRD.md`](../product/PRD.md) §8–9.
- **Executable runbook:** [`docs/ops/GO-LIVE-RUNBOOK.md`](../ops/GO-LIVE-RUNBOOK.md) — the sequenced cutover procedure these stories feed.

---

## Why this epic exists

VrBook Phase 1 + 1.5 are functionally complete **on staging only**. Per `CURRENT-STATE.md` §13 the deployment reality is:

- **Only staging is live and pipelined.** `cd-staging-api.yml` and `cd-staging-web.yml` both hardcode `RESOURCE_GROUP: rg-vrbook-staging` (`cd-staging-api.yml:49`). **No `cd-prod.yml` exists** — a production deploy would be fully manual (gap **G23**, P0).
- **No tested rollback.** The reusable `_deploy-container-app.yml` deploys a single revision at 100% traffic and waits for `healthState==Healthy` (`_deploy-container-app.yml:72-88`). It declares `revision_suffix` and `traffic_weight` inputs (`:23-32`) **but the deploy step never uses `traffic_weight`** — blue/green is scaffolded, not wired (gap **G24**, P0).
- **Backups exist but restore is never exercised** — `infra/main.bicep` sets `pgBackupRetention = isProd ? 35 : 7` and `haEnabled: isProd`, but no restore drill has run (gap **G25**, P1).
- **Sandbox-only integrations:** Stripe TEST, ACS on the managed `*.azurecomm.net` domain (no custom DKIM), Entra `pending-identity-setup` placeholders (gap **G26**, P1).
- **Compliance surfaces absent:** no cookie consent, no Terms/Privacy/Cancellation pages, no GDPR/CCPA data-subject flow, no analytics/conversion tracking (gaps **G32/G34/G35**, P1).

The infra layer is **already prod-parameterized** — `infra/main.bicep` carries `isProd` ladders for Postgres SKU/tier/HA/backup, Container App replicas, Front Door + WAF (`frontDoorEnabled = isProd`, `:111`; `module fd ... if (frontDoorEnabled)`, `:640`). The gap is the **pipeline, the drills, the operator cutover, and the launch discipline** — that is what these stories deliver.

---

## Summary table

| ID | Title | Priority | Est. | Lane | Prereq or launch-week? |
|---|---|---|---|---|---|
| **VRB-300** | API contract test suite (foundation) + endpoint-coverage gate | Must | L | TEST | **WAVE 0 — lands first (safety net for every lane)** |
| **VRB-301** | Production deploy pipeline (`cd-prod.yml`) | Must | L | Pipeline | **PREREQUISITE — lands first** |
| **VRB-302** | Tested rollback / blue-green + post-deploy smoke | Must | M | Pipeline | **PREREQUISITE — with VRB-301** |
| **VRB-303** | Zero-downtime forward-only DB migration + tested rollback + seed data | Must | M | Data | **PREREQUISITE — with VRB-301** |
| **VRB-304** | Backup / restore / DR drill (RPO ≤1h, RTO ≤4h) | Must | M | Data | **PREREQUISITE (drill early)** |
| **VRB-305** | Custom domain, DNS, TLS, redirects, robots/sitemap/canonical | Must | M | Edge | Mid (DNS is a long pole) |
| **VRB-306** | Observability — dashboards + alert rules w/ thresholds + owner | Must | M | Observability | **PREREQUISITE (armed before cutover)** |
| **VRB-307** | Security hardening — WAF/rate-limit, dep+secret+image scan, checklist | Must | M | Security | Mid |
| **VRB-308** | Performance + load test (k6 50 RPS / P95<1s, prod-sized) + CWV budget | Should | M | Performance | Mid (needs prod-sized target) |
| **VRB-309** | Payments go-live — live Stripe keys + webhook + real-money E2E + refund | Must | M | Payments | **LATE — launch-week, operator-gated** |
| **VRB-310** | UAT with the actual property owner + sign-off criteria | Must | S | Product | **LATE — launch-week** |
| **VRB-311** | Analytics + consent + GDPR/CCPA + legal surfaces | Must | L | Compliance | Mid (analytics BEFORE launch) |
| **VRB-312** | Launch runbook — sequenced steps, owners, go/no-go, comms | Must | S | Ops | **LATE — assembles all above** |
| **VRB-313** | Post-launch hypercare + incident/on-call + first-week review | Must | M | Ops | **POST-LAUNCH (starts at cutover)** |

### Prerequisites vs launch-week — read this first

- **Land in Wave 0 (the safety net every other lane builds on):** **VRB-300** (API contract suite + endpoint-coverage gate) — it is not a go-live-week task, it lands *before* the feature lanes so their per-endpoint tests have a suite to plug into. Without it, "keep the API suite green" (the parallel-agent merge rule) has nothing to enforce.
- **Land early (nothing else is safe until these are green):** **VRB-301** (prod pipeline), **VRB-302** (rollback), **VRB-303** (migration strategy), **VRB-306** (observability armed), and the **VRB-304** restore drill. These are the load-bearing infrastructure of a safe cutover. A launch attempted without a tested rollback and a proven restore is not a launch, it is a gamble.
- **Land mid (have long lead times or need a near-final surface):** **VRB-305** (DNS/TLS propagation is hours-to-24h), **VRB-311** (analytics must be live *before* the first real visitor or launch data is lost — gap G35), **VRB-307** (security), **VRB-308** (load test against a prod-sized target).
- **Land late / launch-week (operator-gated, verified against the near-final prod stack):** **VRB-309** (real money — the hardest gate), **VRB-310** (owner UAT sign-off), **VRB-312** (the cutover itself), **VRB-313** (hypercare, begins the moment traffic flips).

**Operator-gated long-poles — kick off on Day 0 in parallel** (their latency, not the code, is the critical path): DKIM/SPF DNS for the ACS sender domain, Stripe LIVE keys + webhook secret, the Entra prod tenant/app-reg/user-flow cutover, and the prod-sized k6 target. See `GO-LIVE-RUNBOOK.md` §Operator long-poles.

---

## Stories

### VRB-300 — API contract test suite (foundation) + endpoint-coverage gate
- **Epic:** Go-Live · **Priority:** Must · **Estimate:** L · **Lane:** TEST · **Wave 0 — lands first, before any feature lane claims.**
- **Narrative:** As any agent shipping into a codebase many agents share, I want a single API contract test suite that exercises **every HTTP endpoint** through the real middleware pipeline against a real Postgres, plus a build-time gate that fails when an endpoint has no contract test, so that no lane can break another lane's endpoint silently and "the suite is green" is a real merge precondition, not a hope. Strategy + tooling: [`../TEST-STRATEGY.md`](../TEST-STRATEGY.md).
- **Acceptance criteria:**
  - **Given** the existing `tests/VrBook.Api.IntegrationTests` harness (`TwoTenantApiFixture` + `TwoTenantTestAuthHandler` — Testcontainers Postgres, all-module migrations, two-tenant persona seeds), **when** VRB-300 lands, **then** it **reuses that fixture** (no new harness) and organises endpoint contract tests by module/controller.
  - **Given** every endpoint currently exposed by the API, **when** the suite runs, **then** each has contract tests covering: **happy path · authentication (anonymous→401) · authorization/tenant-isolation (wrong tenant→403/404, PlatformAdmin-only routes reject owners) · input validation (→400 problem shape) · error contract (documented status + `problem+json`) · idempotency** where the endpoint promises it.
  - **Given** an **endpoint-coverage arch test**, **when** a controller action has zero contract tests referencing its route, **then** the build **fails** with the offending route named (coverage is enforced, not aspirational). New endpoints join the coverage map only *with* their tests — never as a bare exemption.
  - **Given** CI, **when** the suite runs, **then** it is a **blocking gate** on `develop` and is wired into `cd-prod.yml` (VRB-301) before the approval gate.
  - **Given** a developer with Docker up, **when** they run `dotnet test tests/VrBook.Api.IntegrationTests --filter Category=Integration`, **then** the full suite runs locally and green/red matches CI.
  - **Given** the tenant-isolation cases, **then** at least one test proves an `OwnerB` request for a `TenantA`-scoped resource is denied by RLS/`HasTenantRole`, not merely by a controller check.
- **TDD plan:** the suite **is** the tests — but bootstrap it TDD-style: (1) write the endpoint-coverage arch test first and watch it fail listing every currently-uncovered route; (2) drive each endpoint's contract tests until the coverage test goes green; (3) prove the gate by deleting one test and asserting the coverage arch test goes red. Seed any extra fixtures a scenario needs inside the test under `RlsBypassScope` using the real domain factories (per TEST-STRATEGY). Categorise every test `Category=Integration` so the CI `Category!=Integration` filter and the Docker-off local filter behave predictably.
- **Technical notes:** new test files under `tests/VrBook.Api.IntegrationTests/Contract/<Module>/`; the coverage arch test lives in `tests/VrBook.Architecture.Tests` (reflect over `ControllerBase` subclasses + `[Http*]`/route attributes, diff against discovered test coverage via a `[CoversEndpoint("METHOD /route")]` attribute or a naming convention). Assert error bodies as **status + problem `type`**, not custom detail fields (Hellang middleware strips them — [`reference_problem_details_strips_body`]). Do not add any test-only production bypass; `OpsOps2_AdminSurfaceAndTestBackdoorTests` guards that. Coordinate: this suite is the substrate every feature lane's per-endpoint tests plug into (ENGINEERING-RULES §3).
- **Configuration:** none new. CI: add/confirm the integration-test job runs this suite as blocking (it already runs `VrBook.Api.IntegrationTests`); ensure `cd-prod.yml` includes it pre-approval. dev/staging/prod: n/a (test-time only).
- **Rollout:** lands to `develop` as the first Wave-0 story; immediately blocking. No runtime/app change ships.
- **Observability:** CI publishes the suite's pass/fail + endpoint-coverage count as a run summary; a drop in covered-endpoint count is a red flag reviewers watch.
- **Definition of Done:** every current endpoint has contract tests across the six dimensions → endpoint-coverage arch test green (and proven to fail when a test is removed) → suite blocking on `develop` + wired into `cd-prod.yml` → [`../TEST-STRATEGY.md`](../TEST-STRATEGY.md) reflects the final shape → board row `DONE`.
- **Dependencies:** blocked-by nothing (foundation). **Blocks nothing hard, but every endpoint-touching story consumes it** — its per-endpoint tests (ENGINEERING-RULES §3) land against this suite. Closes the Part-A "no test-strategy / no API-suite story" gap.
- **Parallelisation:** Lane = TEST (Wave 0). Owns `tests/VrBook.Api.IntegrationTests/Contract/*` + the coverage arch test in `tests/VrBook.Architecture.Tests`. Runs parallel to CONFIG/DESIGN/DEVOPS-prereq; touches no feature code, so it never collides.

---

### VRB-301 — Production deploy pipeline
- **Epic:** Go-Live · **Priority:** Must · **Estimate:** L
- **Narrative:** As a release engineer, I want a `cd-prod.yml` pipeline that builds, tests, scans, deploys to staging, then promotes to production **only after a gated human approval**, so that production ships through one auditable, repeatable path instead of manual `az` commands.
- **Acceptance criteria:**
  - **Given** a commit on the release ref, **when** `cd-prod.yml` runs, **then** it executes stages in order: build → unit+arch tests → image build+push → Trivy image scan → deploy-to-staging → **staging smoke** → **manual approval gate (GitHub Environment `production` with required reviewers)** → deploy-to-prod (`rg-vrbook-prod`, `env=prod`) → migrate → prod smoke.
  - **Given** the approval gate, **when** no reviewer approves, **then** the prod deploy job stays pending and **no prod resource is touched** (verified: the run shows `Waiting` on the `production` environment).
  - **Given** a failing staging smoke, **when** the pipeline reaches the gate, **then** the gate is unreachable — the job fails before requesting approval.
  - **Given** the prod deploy job, **then** every `resource_group`/env value resolves to `rg-vrbook-prod`/`prod` (no `rg-vrbook-staging` literal anywhere in the prod path).
  - **Given** `infra/main.bicep`, **when** deployed with `env=prod`, **then** Front Door + WAF (`frontDoorEnabled=isProd`, `main.bicep:111,640`), HA Postgres (`haEnabled:isProd`, `:230`), 35-day backup (`:85`), and min-1 replicas (`:91`) are all provisioned.
- **TDD plan:** dry-run `cd-prod.yml` against `rg-vrbook-prod` **before** first real cutover using `workflow_dispatch` with a no-op/placeholder image, asserting the approval gate blocks; a smoke stage identical to staging's curl smoke (`cd-staging-api.yml` smoke steps) run against the prod FQDN; assert-no-staging-literal grep as a workflow lint step.
- **Technical notes:** new `.github/workflows/cd-prod.yml`; reuse `_deploy-container-app.yml` (already env-agnostic via `resource_group` input) and factor the staging build/test/image stages into a reusable `_build-and-test.yml` so staging and prod share them (DRY the two `cd-staging-*` files). Add a GitHub **Environment `production`** with required reviewers + a deployment branch rule. Bicep is already prod-ready; the pipeline passes `env=prod` + `resourceGroup=rg-vrbook-prod`. Federated-credential (OIDC) subject for the prod environment must be added to the Azure app registration.
- **Configuration:** GitHub Environment `production` (required reviewers = Owner + lead eng); repo/env secrets `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` scoped to the prod subscription; new env var `RESOURCE_GROUP=rg-vrbook-prod` in the prod job. dev: n/a · staging: unchanged · prod: new RG + environment.
- **Rollout:** ships to `develop` inert (prod pipeline only triggers on the release ref / dispatch); first exercised in a **dry run**, then the real cutover. Rollback trigger: if the pipeline itself is broken, staging remains the working path and the cutover is postponed — no partial-prod state.
- **Observability:** the pipeline publishes deploy annotations to App Insights (release marker) so VRB-306 dashboards correlate deploys with metric shifts.
- **Definition of Done:** dry-run green with gate proven blocking → review → staging path unaffected → **prod deploy job executes end-to-end on the real cutover** → deploy annotation visible in App Insights.
- **Dependencies:** blocks VRB-302, VRB-309, VRB-312. Blocked-by: prod Azure subscription/RG + OIDC federated credential (operator-gated). Closes **G23**.
- **Parallelisation:** Lane = Pipeline. Owns `.github/workflows/cd-prod.yml`, `_build-and-test.yml`. **PREREQUISITE — the first story to land.**

---

### VRB-302 — Tested rollback (blue-green + post-deploy smoke)
- **Epic:** Go-Live · **Priority:** Must · **Estimate:** M
- **Narrative:** As an on-call operator, I want prod deploys to run blue-green with a health-gated traffic shift and a one-command revert, so that a bad release is rolled back in minutes with zero downtime instead of a manual scramble.
- **Acceptance criteria:**
  - **Given** `_deploy-container-app.yml`, **when** called with `revision_suffix` + `traffic_weight`, **then** the deploy step actually **uses `traffic_weight`** (today it is defined at `:28-32` but ignored) — the new revision is created at 0% (or a canary %), health-gated, then promoted to 100% via `az containerapp ingress traffic set`.
  - **Given** a new prod revision that fails its health probe or post-deploy smoke, **when** the pipeline detects it, **then** traffic is **not** shifted and the old revision keeps 100% (verified by a deliberately-broken canary image in a drill).
  - **Given** a bad release already at 100%, **when** the operator runs the documented rollback command, **then** traffic returns to the last-known-good revision in **< 5 minutes** and a prod smoke passes against it — **this rollback is exercised in a drill, not assumed.**
  - **Given** post-deploy smoke, **then** it hits `/health/ready`, one anonymous public route (`GET /properties`), and one authed route, failing the deploy on any non-200.
- **TDD plan:** a **rollback drill** in staging first: deploy a known-bad image (returns 500 on `/health/ready`), assert traffic never shifts; then a **forward-rollback drill** — promote good→bad at 100%, run the revert command, assert the good revision serves and smoke is green; capture timing to prove < 5 min RTO for the app tier. Drill evidence archived under `docs/ops/drills/`.
- **Technical notes:** edit `_deploy-container-app.yml` deploy step to (1) `--revision-suffix` always in prod, (2) after health-gate, `az containerapp ingress traffic set --revision-weight <new>=<traffic_weight>`; add a `smoke` step to the reusable workflow. Document the revert one-liner (`az containerapp ingress traffic set --revision-weight <lastGood>=100`) in the runbook. Container Apps multi-revision mode must be enabled on the prod api+web apps (Bicep `configuration.activeRevisionsMode: 'Multiple'`).
- **Configuration:** Bicep `activeRevisionsMode=Multiple` for prod api+web; `traffic_weight` input default stays 100 for staging (single-revision) and is set to a canary value for prod. dev/staging: single-revision · prod: multiple-revision.
- **Rollout:** lands with VRB-301; first proven in the staging drill, then live in prod. Rollback trigger for the *feature itself*: if blue-green misbehaves, fall back to VRB-301's single-revision 100% deploy (current staging behavior) and postpone.
- **Observability:** deploy + traffic-shift events annotated in App Insights; an alert (VRB-306) fires if two revisions hold traffic > 30 min (stuck canary).
- **Definition of Done:** both drills green in staging → review → **rollback drill re-run against prod stack pre-launch** → revert command documented in `GO-LIVE-RUNBOOK.md` → monitored.
- **Dependencies:** blocked-by VRB-301. Blocks VRB-312. Closes **G24**.
- **Parallelisation:** Lane = Pipeline. Owns `_deploy-container-app.yml`, `docs/ops/drills/rollback-drill.md`. **PREREQUISITE.**

---

### VRB-303 — Zero-downtime forward-only DB migration + tested rollback + seed data
- **Epic:** Go-Live · **Priority:** Must · **Estimate:** M
- **Narrative:** As a release engineer, I want prod migrations to be forward-only, expand/contract-safe, and run without downtime, plus a proven recovery path and idempotent reference-data seeding, so that a schema change never takes prod down or corrupts tenant data.
- **Acceptance criteria:**
  - **Given** the migrator (`src/VrBook.Migrator`, ~75 migrations, forward-only per `CURRENT-STATE.md` §8), **when** it runs against prod as a pre-deploy Container App Job, **then** it migrates each context then runs the two idempotent backfills (`SeedPlatformAdminsBackfill`, and `SeedE2EBackfill` **disabled in prod** — Bicep `Bootstrap:E2e:Enabled=false`, `main.bicep:61`), and re-running it is a no-op.
  - **Given** the expand/contract discipline, **when** a column is added, **then** the migration follows add→backfill→NOT-NULL as separate steps (already the convention per §8) so the old app revision keeps working during the blue-green window — **no destructive change ships in the same release as the code that stops using the old shape.**
  - **Given** a migration fails mid-run, **when** the operator triggers recovery, **then** the documented path (PITR restore to pre-migration timestamp per VRB-304, or a forward-fix migration) restores a consistent DB — **the recovery is rehearsed in a staging drill.**
  - **Given** reference data (25 amenities seed, platform-admin backfill), **then** seeding is declarative (Bicep `seedPlatformAdmins` array → `SeedPlatformAdminsBackfill`) and idempotent across redeploys.
  - **Given** the cross-schema FK ordering trap, **then** identity migrations run first and any cross-schema reference is `IF EXISTS`-guarded (per the documented migration trap).
- **TDD plan:** a **migration drill** on a PITR-restored copy of prod-shaped data: run the pending migrations, assert zero downtime by keeping an old-revision smoke running throughout; a **forward-fix drill** simulating a failed migration; assert backfill idempotency by running the migrator twice and diffing row counts. `TurnoverAwareCompletionTests` + cross-tenant integration suite must stay green post-migration.
- **Technical notes:** no new code if the expand/contract discipline holds — this story is primarily a **documented strategy + drill + a pre-deploy migration gate** in `cd-prod.yml` (migrator Job runs and must exit 0 before the api/web revision promote). Add a migration-safety checklist to the deploy runbook (no `DROP`/`NOT NULL`-without-backfill in a code-coupled release). Verify migrator `HostAbortedException` on design-time host is handled (expected per CLAUDE.md).
- **Configuration:** prod migrator Job uses `postgres-cs` KV secret with `Database=vrbook` (never `postgres` — documented trap). `Bootstrap:E2e:Enabled=false` in prod. dev/staging: E2E backfill on · prod: off.
- **Rollout:** migrator runs **before** app revision promote in the prod pipeline; backward-compatible by construction (expand/contract). Rollback trigger: migration Job non-zero exit → pipeline halts before code deploy → operator runs recovery drill path.
- **Observability:** migration Job success/failure + duration surfaced as a pipeline check and an App Insights custom event; alert if the migrator Job fails (VRB-306).
- **Definition of Done:** migration + forward-fix + idempotency drills green in staging → review → **migrator runs clean against the prod stack in the dry run** → recovery path in runbook → monitored.
- **Dependencies:** blocked-by VRB-304 (PITR is the recovery substrate). Blocks VRB-312. Relates to **G30** (optimistic concurrency off — safe for single-actor, noted).
- **Parallelisation:** Lane = Data. Owns `src/VrBook.Migrator` (config only), `docs/ops/drills/migration-drill.md`, migration-safety checklist section of the runbook. **PREREQUISITE.**

---

### VRB-304 — Backup / restore / DR — RPO ≤1h, RTO ≤4h (tested restore drill)
- **Epic:** Go-Live · **Priority:** Must · **Estimate:** M
- **Narrative:** As the platform owner, I want Postgres point-in-time backups **and a restore that has actually been performed end-to-end**, so that a data-loss incident is recoverable within RPO ≤1h and RTO ≤4h instead of a hope.
- **Acceptance criteria:**
  - **Given** prod Postgres Flexible Server, **then** automated backups are enabled with 35-day retention (`main.bicep:85`) and **geo-redundant backup** is enabled for prod DR (verify/add `geoRedundantBackup` in the PG module).
  - **Given** a restore drill, **when** the operator performs a PITR restore to a timestamp ≤1h old, **then** a new server comes up, the app connects (with `Database=vrbook`), and a smoke passes — **RPO ≤1h and the full restore complete within RTO ≤4h are both measured and recorded** (closes gap **G25**).
  - **Given** the drill, **then** the elapsed wall-clock time from "decide to restore" to "app serving on restored DB" is captured as the **measured RTO**, and the max data-loss window as the **measured RPO** — both compared against the ≤4h / ≤1h targets.
  - **Given** a region-loss scenario, **then** the DR procedure (geo-restore to the paired region) is documented even if not fully drilled (documented-not-drilled is called out).
- **TDD plan:** a **live restore drill** — trigger PITR on a copy, repoint a throwaway app revision at the restored server, run the curl+authed smoke, stopwatch the whole thing. Archive timings + screenshots under `docs/ops/drills/restore-drill.md`. Re-run the drill once against the prod server pre-launch (on a restored copy, never overwriting prod).
- **Technical notes:** verify `infra/modules/*postgres*.bicep` sets `backup.geoRedundantBackup` for prod and `pointInTimeRestore` capability; document `az postgres flexible-server restore` command with the exact `--restore-time` + `--source-server` flags in the runbook; connection-string swap via KV `postgres-cs` update + revision restart.
- **Configuration:** prod PG `geoRedundantBackup=Enabled`, `backupRetentionDays=35`; KV `postgres-cs` (Database=vrbook). dev: 7-day, local · staging: 7-day, no geo · prod: 35-day + geo.
- **Rollout:** infra change ships with VRB-301's prod Bicep deploy; the drill runs before cutover. No app-code change.
- **Observability:** alert on backup-failure / backup-age > 24h; PG storage + backup metrics on the ops dashboard (VRB-306).
- **Definition of Done:** **restore drill actually performed**, RTO/RPO measured and within target → review → geo-redundant backup verified on prod → DR procedure in runbook → monitored.
- **Dependencies:** blocks VRB-303 (recovery substrate), VRB-312. Blocked-by VRB-301 prod PG provisioned. Closes **G25**; addresses PRD §8 + **G36**.
- **Parallelisation:** Lane = Data. Owns `docs/ops/drills/restore-drill.md`, PG Bicep module backup params. **PREREQUISITE (drill early).**

---

### VRB-305 — Custom domain, DNS, TLS, redirects, robots.txt, sitemap, canonical
- **Epic:** Go-Live · **Priority:** Must · **Estimate:** M
- **Narrative:** As a guest and as a search engine, I want VrBook served on its real custom domain over TLS with correct redirects and crawl directives, so that the product is trustworthy, indexable, and canonical-correct from day one.
- **Acceptance criteria:**
  - **Given** the prod web Container App behind Front Door (`frontDoorEnabled=isProd`), **when** a guest visits the apex + `www` custom domain, **then** TLS terminates with a valid managed certificate and `http→https` + `www↔apex` redirect to the single canonical host (301).
  - **Given** `robots.txt`, **then** it allows crawling of public routes, disallows `/admin/*`, `/account/*`, `/auth/*`, and points at `sitemap.xml`.
  - **Given** `sitemap.xml`, **then** it lists public SSR routes (`/`, `/properties`, and each active `/properties/[slug]`) generated from live data, refreshed on a schedule.
  - **Given** any public page, **then** a `<link rel="canonical">` resolves to the canonical host (no staging FQDN leaks — note the hard-coded staging API FQDN, gap **G8**, must be env-driven for prod).
  - **Given** the API custom domain, **then** the web build-arg API base URL points at the prod API host (not the hard-coded staging FQDN in `cd-staging-web.yml:149`).
- **TDD plan:** a synthetic check (Playwright/curl) asserting 301 redirect chains, TLS validity, `robots.txt`/`sitemap.xml` 200 + content shape; Google Rich Results / Lighthouse SEO check on `/` and a property page; assert no `staging` substring in rendered canonical/API URLs.
- **UI/UX spec:** no new UI beyond `robots.txt` + `sitemap.xml` routes; ensure the existing JSON-LD on `/properties/[slug]` uses the canonical host. Coordinate `sitemap.xml`/canonical **with the SEO/public-pages feature story** (Phase-3 feature backlog) so this go-live story owns only the prod-host/DNS/TLS wiring and the SEO story owns content correctness — avoid double-implementing the sitemap generator.
- **Configuration:** DNS records (apex A/ALIAS + `www` CNAME → Front Door endpoint; API CNAME; ACS DKIM/SPF handled in VRB-311/runbook); Front Door custom-domain + managed cert; env var for the public API base URL and canonical host per env. dev: `localhost` · staging: `*.azurecontainerapps.io` · prod: real domain.
- **Rollout:** DNS is a long pole (propagation hours-to-24h) — provision records early, validate via low-TTL before cutover. Rollback trigger: cert or redirect failure → keep Front Door default hostname, delay public announcement.
- **Observability:** synthetic uptime + TLS-expiry monitor on the custom domain; alert on cert < 21 days to expiry and on redirect-loop 3xx spikes.
- **Definition of Done:** domain resolves + TLS valid + redirects correct on prod → robots/sitemap/canonical verified live → review → **prod verified via synthetic check** → monitored.
- **Dependencies:** blocked-by VRB-301 (Front Door prod). Coordinates-with SEO feature story. Addresses **G8**, **G34**.
- **Parallelisation:** Lane = Edge. Owns Front Door custom-domain Bicep, `web/public/robots.txt`, `web/src/app/sitemap.ts`, prod API-base env wiring. Mid-priority (DNS lead time).

---

### VRB-306 — Observability: dashboards + alert rules with real thresholds + an owner
- **Epic:** Go-Live · **Priority:** Must · **Estimate:** M
- **Narrative:** As an on-call operator, I want prod dashboards and alert rules with concrete thresholds and a named owner per alert, so that I learn about a P95/error/webhook/DB/notification problem from an alert — not from a customer.
- **Acceptance criteria:**
  - **Given** App Insights + Log Analytics (ADR-0010, already provisioned), **when** prod is live, **then** an **alert rule exists with a real threshold and a named owner** for each of: **API P95 latency > 1s (5-min window)**, **5xx error rate > 1%**, **Stripe webhook failure / signature-verify failure**, **Postgres CPU > 80%**, **notification-dispatch failures** (queued `notification_log` rows not draining), and **migrator Job failure**.
  - **Given** each alert, **then** it routes to an action group (email + the owner) and links to its runbook — `payment-webhook-failure.md`, `notification-dispatch-failures.md`, `api-5xx-spike.md`, `postgres-cpu-high.md` (all exist under `docs/runbooks/`).
  - **Given** the ops dashboard, **then** it shows: request volume, P50/P95/P99 latency, error rate, Stripe webhook success %, notification drain lag, PG CPU/connections/storage, Container App replica count + restarts.
  - **Given** a synthetic booking funnel check, **then** an availability test exercises search→quote and alerts on failure.
  - **Given** a deliberately-induced condition in staging (e.g. force a 5xx burst), **then** the corresponding alert **actually fires** — thresholds are validated, not just declared.
- **TDD plan:** fire each alert once in staging by inducing the condition (load a slow endpoint for P95, kill the notifications worker for drain-lag, send a bad webhook signature) and confirm it triggers + routes; assert alert-rule existence via `az monitor metrics alert list` in a CI/ops check.
- **Technical notes:** add alert rules + action group + dashboard as **Bicep** (`infra/modules/observability.bicep` or extend the App Insights module) so they deploy with prod, not hand-clicked. Notification-drain metric may need a custom metric emitted by the Notifications worker (count of `Queued` older than N min). Wire deploy annotations from VRB-301/302.
- **Configuration:** action group with owner email; alert thresholds as Bicep params (P95=1000ms, err=1%, PG CPU=80%, webhook-fail≥1, drain-lag>10min). dev: none · staging: subset (for drill validation) · prod: full set armed.
- **Rollout:** armed **before** cutover so the first prod traffic is watched. Backward-compatible (additive). Rollback trigger: n/a (observability is fail-safe).
- **Observability:** this **is** the observability story — it stands up every alert the other stories reference.
- **Definition of Done:** every alert declared in Bicep + **each validated by inducing the condition in staging** → review → armed on prod → owner assigned per alert → monitored (self-referential: dashboards live).
- **Dependencies:** blocks VRB-312, VRB-313 (hypercare watches these). Blocked-by VRB-301 prod App Insights. Addresses PRD §8 + **G36**; compensating control for deferred k6/ZAP CI gates (per OPS plan §2/§7).
- **Parallelisation:** Lane = Observability. Owns `infra/modules/observability.bicep`, alert Bicep params, dashboard JSON. **PREREQUISITE (armed before cutover).**

---

### VRB-307 — Security hardening: WAF/rate-limiting, dependency+secret+image scanning, pen-test/checklist
- **Epic:** Go-Live · **Priority:** Must · **Estimate:** M
- **Narrative:** As a security-conscious operator, I want prod fronted by a WAF with rate limiting, all three scan classes (dependency, secret, image) run and triaged, and a completed hardening checklist, so that the public surface is defended and free of un-triaged HIGH findings at launch.
- **Acceptance criteria:**
  - **Given** prod, **then** Front Door **WAF** is enabled (`frontDoorEnabled=isProd`, `main.bicep:111,640`) with managed rule sets (Premium SKU required per the `main.bicep:108-111` note) and a **rate-limit rule** on the public + auth surfaces.
  - **Given** the OPS hardening slices, **then** **Trivy** image scan (OPS.5) is run against both `vrbook-api` + `vrbook-web` images with a committed suppressions file and **no un-triaged HIGH/CRITICAL-fixable**; **ZAP baseline** (OPS.4) run against the near-final surface with a committed suppression baseline and no un-triaged HIGH (reuse `docs/runbooks/zap-baseline.md`).
  - **Given** dependency + secret scanning, **then** Dependabot/`dotnet list package --vulnerable` + `npm audit` run and a secret scanner (gitleaks or GitHub secret scanning) is enabled on the repo with zero live secrets.
  - **Given** the security checklist, **then** the pre-launch checklist (fail-fast config validation for Entra — gap **G5** P0; no dev-auth in prod; RLS enforced; no `TODO: production`) is completed and signed.
  - **Given** config fail-fast (**G5**), **then** a prod boot with missing Entra config **fails fast** (`.ValidateOnStart()`) instead of silently serving unauthenticated.
- **TDD plan:** run Trivy + ZAP one-time (per OPS plan §2), archive evidence; a synthetic request storm proves the WAF rate-limit rule returns 429; an arch/integration test asserts the API refuses to boot without valid Entra config (covers G5); secret-scan CI job green.
- **Technical notes:** WAF policy + rate-limit rule in `infra/modules/front-door.bicep` (Premium SKU); reuse the scaffolded `security-zap.yml` + Trivy (OPS.4/OPS.5, currently `continue-on-error` informational — run one-time pre-launch); add `.ValidateOnStart()` to Entra options binding (`AuthExtensions.cs:30-32`, gap G5). Reference `docs/security/threat-model.md`.
- **Configuration:** WAF mode=Prevention on prod (Detection first, then flip); rate-limit thresholds as Bicep params; secret-scanning enabled at repo level. dev/staging: WAF off (Standard SKU) · prod: Premium + Prevention.
- **Rollout:** WAF starts in **Detection** mode to avoid false-positive blocks, flip to **Prevention** after a clean observation window. Rollback trigger: WAF blocking legitimate traffic → revert to Detection.
- **Observability:** WAF blocked-request count + rate-limit 429 rate on the security dashboard; alert on a WAF-block spike (potential attack or false positive).
- **Definition of Done:** WAF+rate-limit live on prod → Trivy+ZAP triaged with committed suppressions → dep+secret scans green → G5 fail-fast landed → checklist signed → review → **prod verified** → monitored.
- **Dependencies:** blocked-by VRB-301 (Front Door prod). Consumes OPS.4/OPS.5. Closes **G5**; addresses **G26**, **G33** (a11y noted separately).
- **Parallelisation:** Lane = Security. Owns `infra/modules/front-door.bicep` WAF policy, `security-zap.yml`, Trivy job, `AuthExtensions.cs` validate-on-start. Mid-priority.

---

### VRB-308 — Performance + load testing (k6 50 RPS / P95<1s, prod-sized) + Core Web Vitals budget
- **Epic:** Go-Live · **Priority:** Should · **Estimate:** M
- **Narrative:** As the platform owner, I want a credible load test proving 50 RPS at P95<1s against a prod-sized target plus a Core Web Vitals budget on the booking funnel, so that launch traffic won't tip the system over and the guest experience meets a measured bar.
- **Acceptance criteria:**
  - **Given** the k6 script (OPS.3, scaffolded; `docs/runbooks/k6-load-test.md` exists), **when** it runs **50 RPS for 5 minutes**, **then** **P95 < 1s** and error rate < 1% — and it runs against a **prod-sized target**, not scale-to-zero B1ms staging (the OPS plan §2/§3 caveat: staging PG is Burstable + scale-to-zero and is **not** representative). The target sizing is recorded in the evidence.
  - **Given** the run, **then** the evidence artifact (k6 summary + the target's PG SKU + replica config) is archived for the Go/No-Go pack.
  - **Given** the booking funnel (`/` → `/properties` → `/properties/[slug]` → quote), **then** a **Core Web Vitals budget** (LCP < 2.5s, CLS < 0.1, INP < 200ms) is measured via Lighthouse CI and met on the key pages.
  - **Given** a failing budget or P95, **then** launch is a **no-go** until remediated or the risk is explicitly accepted with a compensating control (App Insights P95 alert per VRB-306).
- **TDD plan:** one-time k6 run against a prod-sized target (either prod pre-cutover, or staging temporarily upsized to GP PG + min-1 replicas then reverted, per OPS plan §3); Lighthouse CI run on the three funnel pages with budgets as assertions. Both are **one-time pre-launch gates**, not CI-blocking (per OPS plan §2).
- **Technical notes:** reuse `perf-k6.yml` (dispatch) + the k6 script; provision the prod-sized target window; Lighthouse CI config committed under `web/` with the CWV budget. Do **not** run k6 against scale-to-zero staging (misleading).
- **Configuration:** k6 target URL + auth token for the prod-sized run; Lighthouse budget JSON. dev/staging: n/a · prod-sized target: transient. 
- **Rollout:** run late, against a stable near-final surface (OPS plan E4). Rollback trigger: n/a (test-only).
- **Observability:** k6 result compared against the live App Insights P95 alert threshold (VRB-306) — they must agree.
- **Definition of Done:** k6 50 RPS/P95<1s on a **prod-sized** target with archived evidence → CWV budget met on funnel → review → risk accepted or remediated → monitored via P95 alert.
- **Dependencies:** blocked-by VRB-306 (alert threshold parity), a prod-sized target (operator-gated). Consumes OPS.3. Addresses **G27**.
- **Parallelisation:** Lane = Performance. Owns `perf-k6.yml`, k6 script, `web/lighthouserc.json`. Mid-priority (needs prod-sized target).

---

### VRB-309 — Payments go-live: live Stripe keys + webhook, real-money E2E, refund path, replay
- **Epic:** Go-Live · **Priority:** Must · **Estimate:** M
- **Narrative:** As the platform owner, I want VrBook switched to live Stripe keys with a verified real-money end-to-end booking, a verified refund path, and reliable + replayable webhooks, so that we can take and return real money correctly and marketplace fees are handled right.
- **Acceptance criteria:**
  - **Given** OPS.6, **then** the **live** Stripe restricted key + **live webhook signing secret** are in prod KV (replacing `pending-identity-setup`), and the live webhook endpoint points at the prod API `POST /payments/webhooks/stripe`.
  - **Given** a **real-money end-to-end test** (small live transaction), **when** a guest books + pays, **then** the PaymentIntent captures, the signature-verified webhook lands, and the booking moves to **Confirmed** — verified end-to-end on prod.
  - **Given** the refund path, **when** a booking is refunded, **then** the refund executes **and the application-fee reversal actually runs** — closing gap **G37** (`StripeGateway.cs:135-144` / `RefundForBookingCommand.cs` currently write reversal cents to metadata only, never executing via `ApplicationFeeRefundService`). Verified with a live refund.
  - **Given** the merchant-of-record posture (gap **G38**, `StripeGateway.cs:71` sets `OnBehalfOf=supplier`), **then** the tax/facilitator posture (Q25 marketplace facilitator) is reconciled or the deviation is explicitly owner-accepted before live money flows.
  - **Given** webhook reliability, **then** failed webhooks are retried/replayable (Stripe dashboard replay + idempotent handler via `WebhookEvent`), and `docs/runbooks/payment-webhook-failure.md` covers the replay procedure.
  - **Given** a live dispute (`charge.dispute.created`), **then** the log-only handling is a known, documented limitation (`docs/runbooks/stripe-dispute-opened.md`) — not a launch blocker but flagged.
- **TDD plan:** live-mode test transaction (payment→webhook→Confirmed) verified through the UI (per the "test through UI" memory), then a live refund verifying fee reversal executes (query the Stripe balance transaction, not just metadata — G37); webhook replay drill from the Stripe dashboard confirming idempotency (no double-Confirm).
- **UI/UX spec:** none new — uses existing Stripe Elements checkout (PCI SAQ-A per PRD §9).
- **Configuration:** prod KV `stripe-secret-key` (live restricted), `stripe-webhook-secret` (live), `stripe-publishable-key` (live — also note gap **G6**: this + `acs-sender-address` are Bicep-referenced but not seeded by `10-store-secrets.ps1`; **seed before deploy** or Bicep fails atomically). dev/staging: TEST keys · prod: LIVE keys.
- **Rollout:** **launch-week, operator-gated.** Keys go live only at cutover. Rollback trigger: a failed real-money test → revert to `IsConfigured=false` "payment-disabled" mode and delay launch. Seed KV secrets **before** the Bicep deploy (documented trap).
- **Observability:** Stripe webhook success % + failure alert (VRB-306); refund + fee-reversal success metric; dispute alert.
- **Definition of Done:** live keys in KV → **real-money booking verified on prod** → **live refund verified incl. executed fee reversal (G37 closed)** → webhook replay proven idempotent → merchant-of-record posture reconciled/accepted → review → monitored.
- **Dependencies:** blocked-by VRB-301 (prod KV + API), VRB-306 (webhook alert). Consumes OPS.6. Closes **G37**; addresses **G38**, **G6**, **G16**, **G26**. **LATE — the hardest gate.**
- **Parallelisation:** Lane = Payments. Owns prod KV Stripe secrets, `StripeGateway.cs` (G37/G38), `docs/runbooks/stripe-key-rotation.md` reference. **Launch-week, operator-gated.**

---

### VRB-310 — UAT with the actual property owner + sign-off criteria
- **Epic:** Go-Live · **Priority:** Must · **Estimate:** S
- **Narrative:** As the platform owner, I want the real property owner to run the core flows on the near-final prod stack and sign off against explicit criteria, so that launch reflects real-user acceptance, not just green tests.
- **Acceptance criteria:**
  - **Given** the near-final prod (or prod-mirror) stack, **when** the property owner runs the **6 core flows** (guest browse+quote → guest book → guest cancel → admin confirm → admin reject → iCal sync — the "6 things"), **then** each completes successfully and is signed off.
  - **Given** the host lifecycle (PRD §3: invite → Entra-local admin sign-in → onboarding wizard → Stripe Connect → create listing + photos + pricing + cancellation policy → receive/confirm bookings → calendar/iCal → messaging → reports), **then** the owner completes it end-to-end and confirms it is usable.
  - **Given** UAT defects, **then** each is triaged P0/P1/P2; **all P0 and launch-relevant P1 are fixed** before go-live; P2 filed as post-launch.
  - **Given** sign-off, **then** the owner records explicit written acceptance (go/no-go input) referenced in `GO-LIVE-RUNBOOK.md`.
- **TDD plan:** a scripted UAT session following the 6-flow walk (`docs/OPS_M_10_2_F11_OPERATOR_WALK.md` §5-§6) + the host-lifecycle walk; defect log captured; re-test of fixed P0/P1 before sign-off. Layered/seed/upstream issues are engineering's to fix, not the owner's to triage.
- **UI/UX spec:** exercises real UI — flags the known FE gaps (placeholder home `/`, stub `/account/profile`, no mobile nav — gaps G17/G18/G19) if they block owner acceptance; those are P1 fixes if launch-blocking.
- **Configuration:** an Entra-local owner persona (pre-seeded admin — admins are operator-pre-seeded, not self-serve), a real tenant + Stripe Connect test/live account for the walk.
- **Rollout:** launch-week, after the stack is near-final. Rollback trigger: owner withholds sign-off → launch is a no-go until blocking defects fixed.
- **Observability:** UAT-session errors watched live on the VRB-306 dashboard to catch backend issues in real time.
- **Definition of Done:** 6 flows + host lifecycle passed → P0/P1 fixed → **written owner sign-off recorded** → referenced in runbook.
- **Dependencies:** blocked-by VRB-301, VRB-305, VRB-309 (a real booking needs payments), VRB-311 (legal surfaces visible). **LATE — launch-week.**
- **Parallelisation:** Lane = Product. Owns the UAT script + defect log. **Launch-week.**

---

### VRB-311 — Analytics + consent + GDPR/CCPA + legal surfaces
- **Epic:** Go-Live · **Priority:** Must · **Estimate:** L
- **Narrative:** As the platform owner, I want analytics + conversion tracking live **before** the first visitor, a cookie-consent banner, GDPR/CCPA data-subject flows, and the legal pages (Terms/Privacy/Cancellation, Ohio governing law), so that we capture launch data lawfully and meet our compliance posture from minute one.
- **Acceptance criteria:**
  - **Given** analytics (gap **G35**), **then** analytics + **conversion tracking** (search→quote→booking funnel + Tentative→Confirmed) are **live before launch** — if added after the first visitor, launch data is permanently lost.
  - **Given** cookie consent (gap **G32**), **then** a banner offers **necessary vs analytics** categories (PRD §9); analytics scripts load **only after** consent; the choice persists and is re-openable.
  - **Given** GDPR/CCPA (PRD §9), **then** a **"delete my data" / data-subject** request flow exists (self-serve or documented operator process) and a data-retention policy is published; sub-processor DPAs (Stripe, Azure) are referenced.
  - **Given** legal surfaces (gap **G32**), **then** **Terms of Service**, **Privacy Policy**, and per-property **Cancellation Policy** display pages exist — VrBook-drafted, **governing law: Ohio, USA** (Q27/PRD §9), owner-reviewed; linked from the footer.
  - **Given** emailed receipts, **then** they include the tax breakdown (Stripe Tax, marketplace-facilitator posture per PRD §9).
- **TDD plan:** verify (per the "test through UI" memory) that analytics events fire only post-consent (network tab: no analytics beacon before accept); a Vitest/Playwright test asserts the consent banner gates the analytics loader; legal pages render + are linked; a data-subject request walks end-to-end in staging. Lighthouse/axe a11y pass on the new pages (WCAG 2.2 AA baseline — gap G33).
- **UI/UX spec:**
  - **Cookie banner:** bottom/edge-anchored, non-blocking; **Accept all / Reject non-essential / Manage** actions; states = first-visit (shown), consented (hidden, re-openable via footer link), category toggles in Manage. Responsive (full-width mobile, inset desktop). a11y: focus-trapped when the Manage modal opens (note the existing modal lacks focus-trap, gap G33 — fix here), Esc closes, `role="dialog"` + labelled, keyboard-operable, respects `prefers-reduced-motion`.
  - **Legal pages:** static SSR routes (`/legal/terms`, `/legal/privacy`, `/legal/cancellation`), readable measure, in-page anchors/TOC, footer-linked, mobile-friendly.
- **Configuration:** analytics key/ID (consent-gated) per env; consent-state cookie; legal content (owner-approved). dev/staging: test analytics property · prod: live property. Ensure no analytics ID leaks a staging property into prod.
- **Rollout:** analytics + consent + legal ship **before** launch (analytics is the hard "before first visitor" constraint). Backward-compatible (additive pages + banner). Rollback trigger: consent banner failing to gate analytics → disable the analytics loader until fixed (never ship unconsented tracking).
- **Observability:** analytics pipeline health check (events arriving); consent-accept rate on the dashboard.
- **Definition of Done:** analytics + conversion tracking live and consent-gated → banner + legal pages live and owner-approved → data-subject flow verified → a11y pass → review → **prod verified before announcement** → monitored.
- **Dependencies:** blocks VRB-310 (owner reviews legal), VRB-312. Closes **G32**, **G35**; addresses **G33**, **G34**. Owner-gated: legal content approval.
- **Parallelisation:** Lane = Compliance. Owns `web/src/app/legal/*`, consent banner component + analytics loader, data-subject flow. Mid-priority (analytics must precede launch).

---

### VRB-312 — Launch runbook: sequenced steps, owners, go/no-go, comms
- **Epic:** Go-Live · **Priority:** Must · **Estimate:** S
- **Narrative:** As the launch coordinator, I want a single executable runbook with ordered steps, an owner per step, go/no-go gates, rollback triggers, and a comms plan, so that the cutover runs as a rehearsed procedure rather than an improvisation.
- **Acceptance criteria:**
  - **Given** `docs/ops/GO-LIVE-RUNBOOK.md`, **then** it lists **ordered cutover steps each with a named owner**, a **go/no-go gate checklist** (reusing `OPS_LAUNCH_COMPLETION_PLAN.md` §6), explicit **rollback triggers**, a **comms plan**, and the **operator-gated long-poles** (DKIM DNS, Stripe live keys, Entra prod cutover, personas).
  - **Given** the go/no-go gate, **then** launch proceeds only when **every** gate line is green (pipeline+correctness, hardening evidence, operator-gated, hygiene per OPS §6) — signed off by the Owner.
  - **Given** any rollback trigger during cutover, **then** the runbook names the exact revert action (traffic-shift-back per VRB-302, PITR per VRB-304, Stripe-disable per VRB-309, Entra-rollback per `entra-cutover-rollback.md`).
  - **Given** the runbook, **then** it is **executable** — someone other than the author can follow it step by step.
- **TDD plan:** a **dry-run / tabletop rehearsal** of the runbook against staging (or prod pre-announcement) — walk every step, confirm each owner + command is correct, time the cutover window. Findings folded back into the runbook.
- **Technical notes:** the runbook is the deliverable file `docs/ops/GO-LIVE-RUNBOOK.md` (see companion). It references, not duplicates, the OPS plan §6 checklist and each story's DoD.
- **Configuration:** the maintenance-window time; the comms distribution list; the go/no-go sign-off record location.
- **Rollout:** the runbook **is** the rollout. Rollback trigger: the runbook's own triggers.
- **Observability:** during cutover, the VRB-306 dashboard is the shared situational-awareness screen; each step's success is confirmed against it.
- **Definition of Done:** runbook complete + tabletop-rehearsed → owners confirmed → go/no-go signed → **used for the real cutover** → retro captured in VRB-313.
- **Dependencies:** blocked-by (assembles) VRB-301..311. Blocks the actual launch. **LATE — the convergence point.**
- **Parallelisation:** Lane = Ops. Owns `docs/ops/GO-LIVE-RUNBOOK.md`. **Launch-week.**

---

### VRB-313 — Post-launch hypercare + incident/on-call + first-week review
- **Epic:** Go-Live · **Priority:** Must · **Estimate:** M
- **Narrative:** As the platform owner, I want a 2-week hypercare period with daily reviews in week 1, a defined incident/on-call process, and a first-week review, so that early problems are caught and resolved fast and we learn from the launch.
- **Acceptance criteria:**
  - **Given** launch (T0), **then** a **2-week hypercare** window is active with **daily reviews in week 1** (PRD §8, Q20/Q28) — dashboard review, error-triage, webhook + email-deliverability + funnel-conversion check.
  - **Given** an incident, **then** the **on-call process** is defined: who is paged (owner + engineering), severity levels, response/resolution targets, the runbook per alert (all under `docs/runbooks/`), and a post-incident review template.
  - **Given** week 1, **then** a **first-week review** captures uptime vs the 99.5% target, P95 vs <1s, webhook success ≥99.9%, email DKIM/DMARC pass ≥99%, funnel conversion, and any incidents (PRD §10 success metrics).
  - **Given** hypercare exit (T+2 weeks), **then** a go/steady-state decision is recorded and residual items filed as backlog.
- **TDD plan:** verify each alert (VRB-306) routes to on-call during a hypercare drill; the daily-review checklist is exercised on day 1 with a real dashboard walk; the first-week review is produced against real metrics.
- **Technical notes:** the on-call/incident process + hypercare checklist live in the runbook (`GO-LIVE-RUNBOOK.md` §Hypercare); references the existing per-alert runbooks. No code.
- **Configuration:** on-call rota + contact list; incident-tracker location; hypercare daily-review time.
- **Rollout:** starts **the moment traffic flips**. Rollback trigger: a hypercare-detected P0 (data loss, payment failure, auth outage) invokes the VRB-312 rollback triggers.
- **Observability:** consumes every VRB-306 alert + dashboard; hypercare is the human loop closing on the observability the epic stood up.
- **Definition of Done:** hypercare active with daily reviews week 1 → incident/on-call process documented + drilled → first-week review produced against PRD §10 metrics → steady-state decision recorded.
- **Dependencies:** blocked-by VRB-306, VRB-312 (launch happened). Addresses PRD §8 + **G36**. **POST-LAUNCH — begins at cutover.**
- **Parallelisation:** Lane = Ops. Owns `GO-LIVE-RUNBOOK.md` §Hypercare + first-week-review doc. **Post-launch.**

---

## Cross-cutting dependency graph

```
VRB-301 (prod pipeline) ──┬─> VRB-302 (rollback) ──┐
                          ├─> VRB-305 (domain/TLS) ─┤
                          ├─> VRB-306 (observability)┤
                          ├─> VRB-307 (security/WAF)─┤
                          └─> VRB-309 (payments) ────┤
VRB-304 (restore drill) ──> VRB-303 (migration) ────┤
VRB-306 ──> VRB-308 (load test) ────────────────────┤
VRB-311 (analytics/consent/legal) ──────────────────┤
                                                     v
                              VRB-310 (owner UAT) ──> VRB-312 (launch runbook) ──> LAUNCH ──> VRB-313 (hypercare)
```

**Land-early set (prerequisites):** VRB-301, VRB-302, VRB-303, VRB-304, VRB-306.
**Launch-week set:** VRB-309, VRB-310, VRB-312, VRB-313.
**Operator long-poles (start Day 0):** DKIM/SPF DNS (VRB-305/VRB-311), Stripe live keys (VRB-309), Entra prod cutover, DNS (VRB-305), prod-sized k6 target (VRB-308).
