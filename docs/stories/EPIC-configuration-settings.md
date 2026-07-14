# EPIC — Configuration & Settings (VRB-2xx)

> **Every story here inherits the global [Definition of Ready + Definition of Done](../ENGINEERING-RULES.md#definition-of-ready-before-you-write-the-first-test).** Before code: **claim the story on [`BOARD.md`](BOARD.md)** (first-push-wins), read it + its `blocked-by`, and **grep for an existing implementation before building one**. TDD; **write API contract tests for every endpoint you touch and keep the VRB-300 suite green**; stay in your lane ([`../plan/EXECUTION-PLAN.md`](../plan/EXECUTION-PLAN.md)); on finish **self-heal the board + docs**. Operating model: [`../AGENT-PLAYBOOK.md`](../AGENT-PLAYBOOK.md). Each story's own DoD is *in addition to* the global one.

**Epic owner:** Platform / Config lane · **Status:** ready for GATE 2 story review · **Grounding:** [`docs/ops/CONFIG-INVENTORY.md`](../ops/CONFIG-INVENTORY.md) (primary source), [`docs/ops/CURRENT-GAPS.md`](../ops/CURRENT-GAPS.md), [`docs/architecture/CURRENT-STATE.md`](../architecture/CURRENT-STATE.md), [`docs/product/PRD.md`](../product/PRD.md), [`OPEN-QUESTIONS.md`](../../OPEN-QUESTIONS.md). Companion matrix: [`docs/ops/CONFIG-MATRIX.md`](../ops/CONFIG-MATRIX.md).

## Intro

VrBook configuration lives in **two layers** and this epic delivers both:

1. **Product-level settings** — things a human (host / property owner or platform admin) configures *in the app*: listing settings, pricing/fees/tax/min-stay, availability + iCal, cancellation & refund policy, payment/payout, notification templates, branding/SEO, and admin user & role management — all through an **admin settings UI** with validation, safe defaults, and a **who-changed-what audit trail**.
2. **Platform-level configuration** — engineer-configured plumbing: strongly-typed options with **fail-fast startup validation**, **secrets management** via Key Vault + managed identity, a real **feature-flag mechanism**, the **per-environment config matrix**, third-party account setup, and cross-cutting CORS / auth-callback / rate-limit / CDN / cache config.

**Load-bearing ordering.** Three platform stories are prerequisites for almost everything else and must land first:

- **VRB-200 (fail-fast validation)** — today there is **no `.ValidateOnStart()` anywhere** (gap G5): a missing Entra config silently boots the API with **no token validation** (`AuthExtensions.cs:30-32`). Until this lands, every other config change ships blind. **Do first.**
- **VRB-201 (secrets management)** — nothing sensitive lives in source, but `stripe-publishable-key` + `acs-sender-address` are referenced by Bicep and **not seeded** (gap G6), so a first deploy can fail atomically on secretRef resolution. Every later story that adds a secret depends on the seed discipline this story codifies.
- **VRB-202 (per-env config matrix)** — the [`CONFIG-MATRIX.md`](../ops/CONFIG-MATRIX.md) is the single source of truth every other story reads and updates; a CI drift check keeps it honest.

The product-level settings stories all sit on **VRB-210 (admin settings UI shell)** + **VRB-211 (settings audit trail)**, which provide the shared form framework, validation pattern, safe-default contract, and the append-only change log surfaced in the UI. The four **config-defect gap** stories (VRB-206 G1, VRB-207 G2, VRB-208 G3/G4, VRB-209 G7) are small, independent, and can run in parallel once VRB-200 is in.

**Owner-locked constraints honoured throughout:** admin surfaces are Entra-local email+password only (never social — ADR-0016); post-M.21 role authority is `is_platform_admin` (global) + `tenant_memberships.role` (per-tenant) with **no** `IsOwner`/`IsAdmin` literals; new `[Authorize(Roles=...)]` decorators are `PlatformAdmin`-only. Product decisions are already resolved in OPEN-QUESTIONS (Q1 48h SLA, Q2 loyalty no-benefit, Q3/Q24 two cancellation models with platform-set tiers, Q4 platform-admin fee, Q5/Q25 Stripe Tax facilitator).

### Summary table

| ID | Title | Layer | Priority | Est | Gap/Q |
|---|---|---|---|---|---|
| VRB-200 | Strongly-typed options + fail-fast startup validation | Platform | Must | M | G5 |
| VRB-201 | Secrets management: Key Vault, managed identity, seed-all-referenced | Platform | Must | M | G6 |
| VRB-202 | Per-environment config matrix + CI drift check | Platform | Must | S | — |
| VRB-203 | Feature-flag mechanism + naming convention | Platform | Must | M | G13 |
| VRB-204 | Third-party account setup (Stripe live, ACS DKIM, analytics, error tracking) | Platform | Must | M | G26/G35 |
| VRB-205 | CORS, auth callback URLs, rate limits, CDN, caching config | Platform | Should | M | G8 |
| VRB-206 | Fix loyalty tier thresholds — config-driven | Platform | Should | S | G1 |
| VRB-207 | Booking Tentative SLA configurable (48h) | Platform | Must | S | G2/Q1 |
| VRB-208 | Remove dead / mismatched Sync + hold config | Platform | Should | S | G3/G4 |
| VRB-209 | Provide `EntraExternalId:AdminFlowName` + admin-gate config | Platform | Should | S | G7 |
| VRB-210 | Admin settings UI shell (framework, validation, safe defaults) | Product | Must | L | — |
| VRB-211 | Settings change audit trail (who changed what) | Product | Must | M | — |
| VRB-212 | Property / listing settings | Product | Must | M | — |
| VRB-213 | Pricing rules, fees, taxes & minimum-stay settings | Product | Must | L | Q4/Q5/Q25 |
| VRB-214 | Availability rules + iCal endpoints & cadence settings | Product | Must | M | — |
| VRB-215 | Cancellation & refund policy configuration (2 models, per-property) | Product | Must | L | Q3/Q24 |
| VRB-216 | Platform global cancellation-tier + fee + tax-posture table | Product | Must | M | Q24/Q4/Q25 |
| VRB-217 | Payment / payout configuration (Stripe Connect, currency, schedule) | Product | Must | M | Q9 |
| VRB-218 | Notification templates, channels & sender identity | Product | Should | M | — |
| VRB-219 | Branding / domain / SEO metadata per property | Product | Should | M | — |
| VRB-220 | Roles / permissions + admin user management | Product | Must | M | — |

**21 stories.** Estimates: S=small (≤1 session), M=medium (1–2), L=large (2–3+).

---

## Layer 1 — Platform-level configuration (engineer-configured)

### VRB-200 — Strongly-typed options + fail-fast startup validation
- **Epic:** Configuration & Settings · **Priority:** Must · **Estimate:** M
- **Narrative:** As a platform engineer, I want every required config section bound to a validated options class that fails the app at startup when a value is missing or malformed, so that a misconfigured deploy never silently boots with degraded security (e.g. no token validation) instead of crashing loudly.
- **Acceptance criteria:**
  - Given the API starts in `Staging`/`Production` **When** any of `EntraExternalId:{Instance,TenantId,ClientId}` is empty **Then** startup throws an `OptionsValidationException` and the container fails its readiness probe — it must **not** boot with JwtBearer unwired (closes G5; today `AuthExtensions.cs:30-32` degrades silently).
  - Given `Development` **When** the same values are absent **Then** startup is allowed but logs a single explicit `Warning` naming the unvalidated sections (dev-loopback carve-out).
  - Given each of `StripeOptions`, `RefundOptions`, `EntraExternalIdOptions`, `AcsOptions`, `BlobOptions` **When** bound **Then** it uses `AddOptions<T>().Bind(section).ValidateDataAnnotations().ValidateOnStart()` with an `IValidateOptions<T>` for cross-field rules.
  - Given a malformed value (e.g. `Refund:ServiceFeePercent` > 100, non-URL `Blob:AccountUrl`) **When** startup runs **Then** the failure message names the exact `Section:Key` and the rule violated.
  - Given all required sections are present **Then** startup succeeds and emits one structured `ConfigValidationPassed` log listing validated section names (never values).
- **TDD plan:**
  - Unit (`OptionsValidationTests`): `MissingEntraClientId_FailsValidation`, `RefundFeeOver100_FailsValidation`, `AllRequiredPresent_Passes`, `Dev_MissingEntra_WarnsButBoots`.
  - Integration (`StartupFailFastTests`, Testcontainers host): `Staging_WithoutEntra_HostFailsToStart` asserts the host throws before serving; `Production_ValidConfig_HealthReadyReturns200`.
  - Architecture (`ConfigArchTests`): `EveryRequiredOptionsClass_HasValidateOnStart` reflects over `IOptions<>` registrations to fail on any un-validated required section.
- **Technical notes:** New `Options/` classes per module (`StripeOptions.cs` already exists at `src/Modules/VrBook.Modules.Payment/Infrastructure/Stripe/StripeOptions.cs` — add DataAnnotations + `IValidateOptions`). Wire in `AuthExtensions.cs`, `PaymentModule.cs:24`, and the composition root (`src/VrBook.Api/Program.cs`). No migration. The environment-aware carve-out reads `IHostEnvironment.IsDevelopment()`.
- **Configuration:** No new keys — this hardens existing ones. Adds a validation gate over: `EntraExternalId:*`, `Stripe:*`, `Acs:*`, `Refund:ServiceFeePercent`, `Blob:AccountUrl`. Defaults per env unchanged (see matrix rows 15–29).
- **Rollout:** No feature flag — this is a guardrail. Ship after confirming all staging KV secrets are populated (a bad secret would now hard-fail the deploy, which is the point). Backward-compat: dev boots unchanged. Rollback: revert; degradation returns.
- **Observability:** `ConfigValidationPassed` info log with section list; `OptionsValidationException` surfaces in App Insights startup traces; add an alert on container **restart-loop** (crash = misconfig).
- **Definition of Done:** tests green → review → staging deploy fails-then-passes as designed → prod validated → restart-loop alert wired.
- **Dependencies:** blocks VRB-201, VRB-204, and all product stories that add validated secrets. Blocked-by none.
- **Parallelisation:** Config lane; owns `src/**/Options/*.cs`, `AuthExtensions.cs`, `Program.cs` options wiring, `ConfigArchTests`.

### VRB-201 — Secrets management: Key Vault, managed identity, seed-all-referenced
- **Epic:** Configuration & Settings · **Priority:** Must · **Estimate:** M
- **Narrative:** As a platform operator, I want every secret that Bicep references to be guaranteed-seeded in Key Vault before deploy (via a single idempotent script) and never present in source, so that a first/clean deploy cannot fail atomically on an unresolved `secretRef`.
- **Acceptance criteria:**
  - Given `infra/main.bicep` references a `secretRef` **When** `infra/scripts/10-store-secrets.ps1` runs **Then** a placeholder (`pending-identity-setup` / `pending-bicep-deploy`) exists for **every** referenced secret — including `stripe-publishable-key` and `acs-sender-address`, which are currently unseeded (closes G6).
  - Given a CI pre-deploy check **When** it diffs Bicep `secretRef` names against the seed-script secret list **Then** any Bicep-referenced-but-unseeded secret **fails the build** with the missing name.
  - Given the seed script **When** run twice **Then** it is idempotent (never overwrites a real value with a placeholder).
  - Given the orphan secrets `sendgrid-key` + `b2c-api-client-secret` **When** the audit runs **Then** they are removed from the seed script and flagged for KV deletion.
  - Given all app secrets **When** source is scanned (existing secret-scan gate) **Then** zero secrets appear in tracked files; `.env.example` files carry shape-only placeholders and the stale repo-root `.env.example` (retired `NEXT_PUBLIC_ENTRA_AUTHORITY`) is corrected.
  - Given the API/workers **When** they read a secret **Then** access is via Container App `secretRef` bound from KV using the user-assigned managed identity — never a KV connection string in app config.
- **TDD plan:**
  - Unit/script test (`SeedSecretsParityTests`, PowerShell Pester or a small dotnet test): `EverySecretRefInBicep_HasSeedLine` parses `main.bicep` + `10-store-secrets.ps1`.
  - Integration: `10-store-secrets.ps1 -WhatIf` in CI asserts idempotency + full coverage against a captured Bicep secretRef list.
  - E2E: a clean-RG bootstrap smoke (dispatch) provisions KV → runs seed → `main.bicep` deploy succeeds with no unresolved secretRef.
- **Technical notes:** `infra/scripts/10-store-secrets.ps1` gains seed lines for `stripe-publishable-key`, `acs-sender-address`; `acs.bicep` already writes `acs-connection-string` (document the producer). Managed-identity + KV access policy already provisioned (`infra/main.bicep`). The parity check is a new CI step in `cd-staging-api.yml` pre-`az deployment`.
- **Configuration:** Touches KV secret set (matrix rows 1–5, 11, 23–29, 57, 67–68). Adds seed lines; no app keys. Per-env: dev uses local/none, staging+prod use KV.
- **Rollout:** No flag. Land the seed lines + parity check **before** any story that introduces a new secret. Backward-compat: existing populated secrets untouched. Rollback: revert script; deploy risk returns.
- **Observability:** Seed script logs each secret set/skipped (name only); CI parity-check failure names the missing secret; alert on `Key Vault` `SecretNotFound` in App Insights.
- **Definition of Done:** parity test green → review → clean-RG bootstrap smoke green → staging deploy green → prod checklist item ticked in `OPS_LAUNCH_COMPLETION_PLAN.md`.
- **Dependencies:** blocked-by VRB-200 (validation makes an unseeded secret fail loudly). Blocks VRB-204, VRB-213, VRB-217, VRB-218.
- **Parallelisation:** Infra lane; owns `infra/scripts/10-store-secrets.ps1`, `infra/main.bicep` secretRef block, `.env.example`, `SeedSecretsParityTests`.

### VRB-202 — Per-environment config matrix + CI drift check
- **Epic:** Configuration & Settings · **Priority:** Must · **Estimate:** S
- **Narrative:** As an engineer onboarding or debugging a config issue, I want one authoritative table of every key/secret and its value-or-source per dev/staging/prod, kept from drifting by CI, so that "the developer will figure it out" is never the answer.
- **Acceptance criteria:**
  - Given [`docs/ops/CONFIG-MATRIX.md`](../ops/CONFIG-MATRIX.md) **Then** it lists every key/secret from CONFIG-INVENTORY with delivery type (Bicep plain / KV secretRef / appsettings / NEXT_PUBLIC / hard-coded), a value-or-source per env, and a move-to-config flag.
  - Given a new key is added to `appsettings.json` or a new `secretRef` to `main.bicep` **When** CI runs the drift check **Then** the build fails until the matrix has a row for it.
  - Given a hard-coded value flagged ⚠️ in the matrix **Then** it links to the story that externalises it.
- **TDD plan:**
  - Unit (`ConfigMatrixDriftTests`): `EveryAppsettingsKey_HasMatrixRow`, `EveryBicepSecretRef_HasMatrixRow` — parse `appsettings.json` + `main.bicep`, assert each key/secret appears as a table row in `CONFIG-MATRIX.md`.
- **Technical notes:** Drift test lives in `tests/VrBook.Architecture.Tests` (doc-vs-code). Parses markdown table + JSON + Bicep. Doc-only edits don't trigger CI (path filters), so the drift test runs on any code change touching config files.
- **Configuration:** Meta-story; governs all rows. No runtime keys.
- **Rollout:** No flag. Ship the doc (already drafted) + the test. Rollback: drop the test.
- **Observability:** CI failure names the un-documented key.
- **Definition of Done:** matrix complete → drift test green → review → merged.
- **Dependencies:** blocked-by none; every other story updates the matrix (soft dependency).
- **Parallelisation:** Config lane; owns `docs/ops/CONFIG-MATRIX.md`, `ConfigMatrixDriftTests`.

### VRB-203 — Feature-flag mechanism + naming convention
- **Epic:** Configuration & Settings · **Priority:** Must · **Estimate:** M
- **Narrative:** As a platform admin, I want a real runtime feature-flag mechanism with a consistent naming convention and an admin toggle UI, so that we can dark-launch and kill-switch features without a redeploy (today `StubFeatureToggle` is a no-op and `TogglesController` returns 501 — G13).
- **Acceptance criteria:**
  - Given `IFeatureToggle` **When** resolved at runtime **Then** it is backed by a real store (config-first with a DB-backed override table; Redis optional when deployed) — not `StubFeatureToggle` (`src/VrBook.Infrastructure/Stubs/StubFeatureToggle.cs`).
  - Given `GET /api/v1/admin/toggles` and `PUT /api/v1/admin/toggles/{key}` (`AdminController.cs` / `TogglesController.cs`) **When** called by a `PlatformAdmin` **Then** they list and set flags (no longer 501); tenant-admins get 403.
  - Given a flag key **Then** it follows the convention `Features:<Area>.<Capability>` (e.g. `Features:Booking.InstantBook`, `Features:Payment.UseRedisHoldStore`) documented in a naming section of CONFIG-INVENTORY.
  - Given `Features:UseRedisHoldStore` (a real toggle today with no consumer) **Then** it is migrated to the new convention key and actually wired to hold-store selection.
  - Given `Loyalty:Enabled` (a real flag not surfaced) **Then** it is exposed through the same mechanism.
  - Given a flag change **When** applied **Then** it is written to the settings audit trail (VRB-211) and takes effect without redeploy.
- **TDD plan:**
  - Unit (`FeatureToggleTests`): `ConfigFlag_ResolvesDefault`, `DbOverride_WinsOverConfig`, `UnknownFlag_ReturnsFalseSafe`.
  - Integration (`TogglesControllerTests`): `PlatformAdmin_ListsAndSetsFlags`, `TenantAdmin_Get403`, `SetFlag_WritesAuditEntry`.
  - Architecture: `NoStubFeatureToggle_InProdComposition` fails if the stub is registered outside test.
- **Technical notes:** New `DbFeatureToggle : IFeatureToggle` reading an `admin.feature_flags` table (or `identity` schema) with a config fallback. Migration for the flag table. Replaces stub registration in the composition root. `AdminModule.cs:17-25` (currently no-op) is the natural home for the flag aggregate.
- **Configuration:** New keys under `Features:*` (convention `Features:<Area>.<Capability>`); defaults all `false` except `Features:Loyalty.Enabled=true`. Per-env: same defaults dev/staging/prod; overrides via DB.
- **Rollout:** The mechanism itself is unflagged; every *feature* it gates is a flag. Migration adds the table (additive). Backward-compat: absent flag → safe default `false`. Rollback: revert to stub (flags read defaults).
- **Observability:** Log each flag evaluation-override + each admin toggle change; metric `feature_flag_overrides_active`; alert none (informational).
- **Definition of Done:** tests green → review → staging toggle round-trips in UI → prod verified → audit entries confirmed.
- **Dependencies:** blocked-by VRB-211 (audit trail) for the change-logging AC; blocks any dark-launch story (e.g. Instant Book).
- **Parallelisation:** Config lane; owns `IFeatureToggle` impls, `AdminController.cs`/`TogglesController.cs`, `admin.feature_flags` migration.

### VRB-204 — Third-party account setup (Stripe live, ACS DKIM, analytics, error tracking)
- **Epic:** Configuration & Settings · **Priority:** Must · **Estimate:** M
- **Narrative:** As a platform operator preparing for launch, I want every third-party integration promoted from sandbox to production with documented setup runbooks, so that we go live on live Stripe, a DKIM-verified email domain, real analytics, and client-side error tracking (today all are sandbox-only — G26; analytics absent — G35).
- **Acceptance criteria:**
  - Given Stripe **When** prod config is applied **Then** live secret + webhook + publishable keys are in KV, a live Connect onboarding path works, and the live webhook endpoint is registered (test→live cutover documented in a runbook).
  - Given ACS email **When** prod is configured **Then** a **custom sender domain with DKIM + SPF + DMARC** DNS records is verified (replacing the managed `*.azurecomm.net` domain and the `donotreply@vrbook.example.com` placeholder), and deliverability (DKIM/DMARC pass) ≥ 99% per the launch metric.
  - Given analytics **Then** a consent-gated analytics tag (respecting the cookie-consent categories from the compliance epic) is live and firing conversion events on the booking funnel before launch — else launch data is lost (G35).
  - Given error tracking **Then** client-side errors report to a tracker (App Insights browser SDK or equivalent) alongside the existing server-side App Insights.
  - Given each integration **Then** a runbook under `docs/runbooks/` documents the exact setup steps and the KV secrets involved.
- **TDD plan:**
  - Integration (`StripeLiveModeSmokeTests`, dispatch/staging-live): `LiveWebhookSignature_Verifies`, `ConnectOnboarding_ReturnsAccountLink`.
  - E2E (`analytics.e2e.ts`): `BookingFunnel_FiresConversionEvent_WhenConsentGranted`, `NoAnalytics_WhenConsentDenied`.
  - Manual/runbook-verified: DKIM/DMARC DNS check (`dig`/portal), deliverability test send.
- **Technical notes:** Extends `docs/runbooks/social_idp_setup.md` pattern with `stripe_go_live.md`, `acs_custom_domain_dkim.md`, `analytics_setup.md`. KV secrets: `stripe-*` (live), `acs-sender-address` (custom). Client analytics wired in `web/src/app/layout.tsx` behind consent. Note MoR/tax posture fixes G37/G38 are separate payment stories, not here.
- **Configuration:** `Stripe:*` live (matrix 23–25), `Acs:SenderAddress` custom (29), new `NEXT_PUBLIC_ANALYTICS_*` + error-tracker DSN (client). Per-env: dev none/sandbox, staging sandbox, **prod live** (go-live gate).
- **Rollout:** Feature-flagged analytics (via VRB-203) so it can be toggled per env. Stripe live is a hard go-live cutover (irreversible per-mode); rollback = revert to test keys pre-launch only. DKIM DNS is additive.
- **Observability:** Webhook-success metric ≥99.9%, email deliverability metric, analytics event volume dashboard; alert on webhook-failure spike + DMARC-fail rate.
- **Definition of Done:** runbooks reviewed → staging sandbox verified → prod live keys + DKIM verified → conversion events flowing → monitored in launch dashboard.
- **Dependencies:** blocked-by VRB-201 (secret seeding), VRB-200 (validation). Blocks launch (`OPS_LAUNCH_COMPLETION_PLAN.md`).
- **Parallelisation:** Infra + Web lanes; owns `docs/runbooks/{stripe_go_live,acs_custom_domain_dkim,analytics_setup}.md`, KV live secrets, `web/src/app/layout.tsx` analytics.

### VRB-205 — CORS, auth callback URLs, rate limits, CDN, caching config
- **Epic:** Configuration & Settings · **Priority:** Should · **Estimate:** M
- **Narrative:** As a platform engineer, I want CORS origins, MSAL redirect/callback URLs, rate limits, CDN, and cache headers driven by per-env config (not hard-coded FQDNs), so that a CAE environment rebuild or new prod domain doesn't require a code change (today the staging API FQDN is baked into `cd-staging-web.yml:149` — G8, and firewall IPs are literals in `main.bicep`).
- **Acceptance criteria:**
  - Given the web build **When** it needs the API base URL **Then** `NEXT_PUBLIC_API_BASE_URL` comes from a Bicep output / env var, not a hard-coded FQDN in `cd-staging-web.yml:149` (closes G8).
  - Given Postgres firewall IPs (`174.104.204.213`, `135.18.171.52`) **Then** they are Bicep params, not literals in `main.bicep`.
  - Given `Cors:AllowedOrigins` **Then** it is set per env (dev localhost, staging web origin, prod web origin(s)) and validated (VRB-200).
  - Given MSAL redirect/callback URIs **Then** the admin + guest authority URLs and redirect URIs are per-env config, matching the Entra app-registration reply URLs.
  - Given the outbound iCal rate limiter **Then** its policy (host suffixes / token / window / burst, today hard-coded in `ChannelPollOptions.cs:23-30`) is bound from config (also covered by VRB-214) and per-replica-vs-distributed behaviour is documented (G28).
  - Given public images **Then** a CDN/cache strategy is documented and cache-control headers on public/SEO pages are configured per env.
- **TDD plan:**
  - Unit (`CorsConfigTests`): `AllowedOrigins_BoundPerEnv`, `EmptyOrigins_FailsValidation`.
  - Integration (`ApiFqdnResolutionTests`): asserts web build reads env, not a literal (lint the workflow file).
  - E2E: `crossOrigin.e2e.ts` — web origin calls API without CORS error on staging.
- **Technical notes:** `cd-staging-web.yml:149` → Bicep output binding; `main.bicep` firewall IPs → `param allowedClientIps array`. CORS in `Program.cs` from `Cors:AllowedOrigins`. Rate-limit policy → `ChannelPollOptions` bound from `ChannelPoll:*`. CDN in front of Blob `property-images` documented (currently none — CURRENT-STATE §10).
- **Configuration:** `Cors:AllowedOrigins`, `NEXT_PUBLIC_API_BASE_URL`, `NEXT_PUBLIC_SITE_URL` (currently never set — matrix 31), `ChannelPoll:*`, firewall-IP param. Per-env per matrix rows 30–31, 38, 54, 61.
- **Rollout:** No flag. Sequence: add Bicep params/outputs → update workflow → validate. Backward-compat: defaults match current staging. Rollback: revert workflow + Bicep.
- **Observability:** Log resolved CORS origins at startup; metric on rate-limiter rejections; alert on CORS-preflight 4xx spike.
- **Definition of Done:** tests green → review → staging CAE-rebuild dry-run doesn't break web → prod domain config verified → monitored.
- **Dependencies:** blocked-by VRB-200. Overlaps VRB-214 (rate-limit policy).
- **Parallelisation:** Infra + Web lanes; owns `cd-staging-web.yml`, `main.bicep` firewall params, `Program.cs` CORS, `ChannelPollOptions.cs`.

### VRB-206 — Fix loyalty tier thresholds — config-driven (G1)
- **Epic:** Configuration & Settings · **Priority:** Should · **Estimate:** S
- **Narrative:** As a platform admin, I want loyalty tier thresholds to actually read from config, so that ops can tune tiers without a code change (today `LoyaltyAccount.cs:65-66` hard-codes `Silver=3`/`Gold=6` while the `Loyalty:*Threshold` config keys are dead — the config is a lie).
- **Acceptance criteria:**
  - Given `Loyalty:{BronzeThreshold,SilverThreshold,GoldThreshold}` in config **When** tier resolution runs **Then** `TierDefinition.Resolve` uses the configured values, not the `const` at `LoyaltyAccount.cs:65-66`.
  - Given the config is absent **Then** it defaults to Bronze 1 / Silver 3 / Gold 6 (the current constants) — safe default, no behaviour change.
  - Given the Bicep env vars (`main.bicep:381-383`) **Then** they now bind and take effect (previously dead).
  - Given Q2 (loyalty gives no guest benefit at launch) **Then** only tier *tracking* is affected; no discount/perk is introduced.
- **TDD plan:**
  - Unit (`LoyaltyTierResolutionTests`): `ConfiguredThresholds_Applied`, `MissingConfig_DefaultsTo1_3_6`, `Boundary_ExactlyGoldThreshold_ReturnsGold`.
  - Integration: `TierPromotion_UsesConfiguredThreshold` with a testcontainer + overridden config.
- **Technical notes:** New `LoyaltyOptions` (`Loyalty` section) with `ValidateOnStart` (VRB-200); inject into the resolver. `TierDefinition` static table (`LoyaltyAccount.cs:61-79`) becomes config-driven — replace `const` reads with injected options. No migration (thresholds are compute-time).
- **Configuration:** `Loyalty:BronzeThreshold=1`, `SilverThreshold=3`, `GoldThreshold=6` (all envs default; matrix 47–49). `Loyalty:Enabled` surfaced via VRB-203.
- **Rollout:** No flag (config-defaulted, behaviour-preserving). Backward-compat: identical output at default values. Rollback: revert; constants return.
- **Observability:** Log resolved thresholds at startup; existing `TierPromoted` domain event unchanged.
- **Definition of Done:** tests green → review → staging tier calc unchanged at defaults → prod verified.
- **Dependencies:** blocked-by VRB-200 (options validation). Independent of other product stories.
- **Parallelisation:** Config lane; owns `src/Modules/VrBook.Modules.Loyalty/**`, `LoyaltyOptions`.

### VRB-207 — Booking Tentative SLA configurable (48h) (G2/Q1)
- **Epic:** Configuration & Settings · **Priority:** Must · **Estimate:** S
- **Narrative:** As a platform admin, I want the Tentative-booking hold window to read from config with the owner-locked value of **48h**, so that the hold-expiry sweep and guest UX use one tunable, correct value (today `Booking.cs:119` hard-codes `AddHours(24)`, `Booking:TentativeSlaHours` is never read, and the Bicep comment says "6h" — a three-way inconsistency).
- **Acceptance criteria:**
  - Given `Booking:TentativeSlaHours` **When** a booking is placed **Then** `TentativeUntil` = now + configured hours, reading config instead of the hard-coded `AddHours(24)` at `Booking.cs:119`.
  - Given no config **Then** the default is **48** (Q1 owner-locked, 2026-07-13) — not 24, not 6.
  - Given the expiry-sweep worker (`--mode=expiry`, `*/10 * * * *`) **Then** it sweeps against the same configured value (one source of truth for domain + worker).
  - Given the Bicep env var **Then** its stale "6h" comment is corrected and it delivers 48.
  - Given a placed booking **When** 48h elapse without confirmation **Then** the sweep expires it and releases the hold.
- **TDD plan:**
  - Unit (`TentativeSlaTests`): `Place_SetsTentativeUntil_FromConfig`, `DefaultSla_Is48Hours`, `ConfiguredSla_Overrides`.
  - Integration (`TentativeExpirySweepTests`, testcontainer): `Booking_ExpiresAfterConfiguredSla`, `WorkerAndDomain_UseSameValue`.
- **Technical notes:** New `BookingOptions` (`Booking` section) validated on start; inject into the `Place` command handler and the expiry worker (`src/Workers/VrBook.Workers.Booking`). `Booking.cs:119` `AddHours(24)` → `AddHours(options.TentativeSlaHours)` (pass via command, keep domain pure). Fix `main.bicep` `Booking__TentativeSlaHours` value+comment.
- **Configuration:** `Booking:TentativeSlaHours=48` (all envs; matrix 43). Retire `Booking:HoldDurationMinutes` handled in VRB-208.
- **Rollout:** No flag but **behaviour-changing** (24h→48h) — call it out in release notes; it lengthens holds. Backward-compat: in-flight Tentative bookings keep their already-stamped `TentativeUntil`. Rollback: set config back to 24.
- **Observability:** Log configured SLA at startup + on each sweep run; metric `tentative_expired_total`; alert on sweep-run failure.
- **Definition of Done:** tests green → review → staging place+sweep verified at 48h → prod verified → monitored.
- **Dependencies:** blocked-by VRB-200. Independent otherwise.
- **Parallelisation:** Config lane; owns `src/Modules/VrBook.Modules.Booking/Domain/Booking.cs`, Place handler, Booking worker, `BookingOptions`.

### VRB-208 — Remove dead / mismatched Sync + hold config (G3/G4)
- **Epic:** Configuration & Settings · **Priority:** Should · **Estimate:** S
- **Narrative:** As an engineer, I want dead and mismatched config keys either wired to a consumer or removed, so that the config surface tells the truth (today `Booking:HoldDurationMinutes`, `Sync:DefaultPollIntervalMinutes`, and `Sync:StaleAlertHours` have no consumers, and Bicep sends `Sync__DefaultPollIntervalMin` — a name that wouldn't even bind).
- **Acceptance criteria:**
  - Given `Sync:DefaultPollIntervalMinutes` **When** audited **Then** it is either wired to the sync worker's poll cadence or removed from appsettings + Bicep; and the Bicep `Sync__DefaultPollIntervalMin` name-mismatch (`main.bicep:374`) is fixed or deleted (closes G4).
  - Given `Sync:StaleAlertHours` **Then** it is wired to a real stale-feed alert threshold or removed.
  - Given `Booking:HoldDurationMinutes` **Then** it is wired to hold-store TTL or removed (holds today are a per-feed/DB concern — CONFIG-INVENTORY §7).
  - Given the decision per key **Then** it is recorded in CONFIG-MATRIX with rationale (wired vs removed).
  - Given removal **Then** the corresponding appsettings + Bicep + matrix rows are deleted together (no orphan).
- **TDD plan:**
  - Unit (`DeadConfigTests`): for any key retained, `Key_HasConsumer` asserts a binding exists; for removed keys, `ConfigMatrixDriftTests` (VRB-202) confirms they're gone from appsettings+Bicep+matrix.
- **Technical notes:** Decide per key (architect consult if wiring, per working-pattern rules). Sync poll cadence is currently a per-feed DB column (CONFIG-INVENTORY §7) — likely remove the global key. `main.bicep:374` name fix. Touches `appsettings.json`, `main.bicep`, `CONFIG-MATRIX.md`.
- **Configuration:** Retires/wires matrix rows 44–46. Default: removed (no consumer) unless architect elects to wire.
- **Rollout:** No flag; pure cleanup. Backward-compat: removing dead keys has zero runtime effect. Rollback: trivial revert.
- **Observability:** None new; if `StaleAlertHours` is wired, add a stale-feed alert.
- **Definition of Done:** decision recorded → tests green → review → staging deploy clean → matrix updated.
- **Dependencies:** blocked-by VRB-202 (matrix drift test). Independent otherwise.
- **Parallelisation:** Config lane; owns `appsettings.json` Sync/Booking sections, `main.bicep:374`.

### VRB-209 — Provide `EntraExternalId:AdminFlowName` + admin-gate config (G7)
- **Epic:** Configuration & Settings · **Priority:** Should · **Estimate:** S
- **Narrative:** As a platform engineer, I want `EntraExternalId:AdminFlowName` (and `Payment:AllowPlatformFallback`) actually provided in config, so that the admin-vs-guest surface gate at `UserProvisioningMiddleware.cs:143` is driven by documented config instead of being inert.
- **Acceptance criteria:**
  - Given `UserProvisioningMiddleware.cs:143` reads `EntraExternalId:AdminFlowName` **When** the app runs **Then** the key is provided per env (KV or Bicep) and documented in CONFIG-INVENTORY + matrix (row 19) — closing G7.
  - Given the value is absent in `Staging`/`Production` **Then** VRB-200 validation fails fast (the admin gate must not be silently inert where it enforces the owner-locked admin-vs-social split — ADR-0016).
  - Given `Payment:AllowPlatformFallback` (referenced, never set, default false) **Then** it is documented and explicitly set to `false` per env (matrix 27).
  - Given the admin flow name is set **Then** a provisioning attempt through the admin flow classifies the user as admin-eligible and a guest-flow attempt does not.
- **TDD plan:**
  - Unit (`AdminFlowGateTests`): `AdminFlowName_Configured_GatesAdminProvisioning`, `MissingInStaging_FailsValidation`, `GuestFlow_NotAdminEligible`.
  - Integration: `UserProvisioningMiddlewareTests` with the flow name set/unset.
- **Technical notes:** Add `EntraExternalId:AdminFlowName` to the seeded config (KV `entra-admin-flow-name` or Bicep plain — architect's call; likely Bicep plain since it's a flow name, not a secret). Wire into `EntraExternalIdOptions` (VRB-200). Coordinate with the owner-locked admin pre-seed policy (OPS.M.22) and ADR-0016.
- **Configuration:** New `EntraExternalId:AdminFlowName` (per env, matches the Entra `AdminSignUpSignIn` flow when it exists; note per CLAUDE.md the split admin flow lands with OPS.M.22). `Payment:AllowPlatformFallback=false` (all envs). Matrix rows 19, 27.
- **Rollout:** No flag. Sequence after the Entra admin flow exists (OPS.M.22 dependency). Backward-compat: setting it enforces the gate that was previously inert — verify no legitimate admin is locked out. Rollback: unset (gate returns inert — degraded, so monitor).
- **Observability:** Log the resolved admin flow name (non-secret) at startup; audit admin-provisioning decisions; alert on admin-provisioning rejections.
- **Definition of Done:** tests green → review → staging admin sign-in gated correctly → prod verified → monitored.
- **Dependencies:** blocked-by VRB-200; coordinates with OPS.M.22 (admin flow existence). Blocks nothing.
- **Parallelisation:** Config + Identity lanes; owns `UserProvisioningMiddleware.cs`, `EntraExternalIdOptions`, config seeding for the flow name.

---

## Layer 2 — Product-level settings (configured by humans in the app)

### VRB-210 — Admin settings UI shell (framework, validation, safe defaults)
- **Epic:** Configuration & Settings · **Priority:** Must · **Estimate:** L
- **Narrative:** As a host or platform admin, I want a consistent Settings area with a shared form framework (sectioned nav, inline validation, safe defaults, save/discard, and a visible change history), so that every settings screen behaves the same and I can't accidentally save an invalid or empty configuration.
- **Acceptance criteria:**
  - Given `/admin/settings/*` (tenant-admin) and `/admin/platform/settings/*` (platform-admin) **When** rendered **Then** a shared `SettingsLayout` provides a section sidebar, page title, and a sticky save/discard bar, gated by `AdminAuthGuard` (Entra-local admin only per ADR-0016).
  - Given any settings form **When** a field is invalid **Then** an inline error appears on the field (not a toast-only), the save bar shows the count of errors, and save is blocked.
  - Given a form with a missing optional value **Then** a documented **safe default** is shown as placeholder/prefill and persisted only if the user confirms.
  - Given a successful save **Then** an optimistic success state shows, the change is written to the audit trail (VRB-211), and the "last changed by X at T" line updates.
  - Given a save conflict / server validation failure **Then** the field-level errors from the API are mapped back onto the form (RFC-7807 problem-details `errors` map).
  - Given a tenant-admin **Then** they see only tenant-scoped sections; platform-only sections (global tiers, platform fee, feature flags) are hidden/403.
- **TDD plan:**
  - Unit (Vitest): `SettingsLayout.test.tsx` (renders sections, gates by role), `useSettingsForm.test.tsx` (dirty tracking, discard, error mapping), `SafeDefault.test.tsx`.
  - E2E (Playwright): `admin-settings-shell.e2e.ts` — owner opens settings, edits a field invalid→valid, saves, sees audit line; platform-only section hidden for tenant-admin.
- **Technical notes:** New `web/src/app/admin/settings/**` + `web/src/components/settings/` (`SettingsLayout`, `SettingsSection`, `useSettingsForm`, `FieldError`, `SaveBar`). Reuses `AdminAuthGuard` + admin sidebar (CURRENT-STATE §11). Builds the **shared UI primitives** currently missing (G20) — this story seeds the component library (buttons/inputs/cards) the other settings stories consume. Uses TanStack Query mutations + `apiFetch` (`web/src/lib/api/client.ts`).
- **UI/UX spec:**
  - **States:** loading (skeleton rows), empty (safe-default prefill), editing (dirty bar), saving (spinner on save), success (green check + audit line), error (inline field errors + summary), conflict (map server errors).
  - **Responsive:** section nav collapses to a top dropdown < md (also fixes the no-mobile-nav gap G19 within settings); forms single-column on mobile, two-column ≥ lg.
  - **A11y (WCAG 2.2 AA):** labelled inputs, `aria-invalid` + `aria-describedby` on errors, focus moves to first error on failed save, focus-trap in any modal (closes the modal focus-trap gap G33), 44px targets, visible focus rings, no color-only error signalling.
  - **Validation + safe defaults:** client mirrors server rules; every field documents its safe default; destructive changes (e.g. policy switch) require a confirm modal.
  - **Audit-trail display:** each section shows a "Recent changes" panel (who/what/when) sourced from VRB-211.
  - **Design system:** semantic tokens (not raw `brand-*`), Inter, lucide icons; establishes the shared primitives to standardise the two parallel color systems (G20).
- **Configuration:** No runtime keys; consumes the settings APIs of VRB-212..220. Feature-flag `Features:Admin.SettingsUi` (VRB-203) to dark-launch.
- **Rollout:** Flag `Features:Admin.SettingsUi`. Ship shell first, then wire each settings domain behind it. Backward-compat: existing scattered admin pages remain until migrated. Rollback: flag off.
- **Observability:** Client error tracking (VRB-204) on settings forms; log save failures; metric `settings_save_error_rate`.
- **Definition of Done:** unit+E2E green → review → staging shell works for owner + platform-admin → prod → monitored.
- **Dependencies:** blocked-by VRB-211 (audit display), VRB-203 (flag). Blocks VRB-212..220 (they render inside it).
- **Parallelisation:** Web lane; owns `web/src/app/admin/settings/**`, `web/src/components/settings/**`, shared UI primitives.

### VRB-211 — Settings change audit trail (who changed what)
- **Epic:** Configuration & Settings · **Priority:** Must · **Estimate:** M
- **Narrative:** As a platform admin (and for compliance), I want every settings change recorded as an immutable audit entry with actor, timestamp, before/after, and scope, so that we can answer "who changed this and when" for any configuration.
- **Acceptance criteria:**
  - Given any settings write (property, pricing, policy, payout, notification template, role, feature flag, platform tier) **When** committed **Then** an append-only audit entry records `{actor_user_id, tenant_id?, section, key, old_value, new_value, at, request_id}`.
  - Given the existing Identity `AuditLogBehavior` (`IdentityModule.cs:54-68`, writes `AuditLogEntry` for any `IAuditable`, incl. `.failed` rows) **Then** settings commands implement `IAuditable` and reuse it — no parallel audit system.
  - Given a settings section in the UI **Then** it shows the recent changes for that section (actor + human-readable diff + timestamp).
  - Given a failed/rejected settings write **Then** a `.failed` audit row is still written (existing behaviour).
  - Given sensitive values (secrets) **Then** the audit stores a redacted marker, never the secret value.
  - Given retention **Then** audit entries follow the 7-year financial-record retention where the change is financial (fees/payout/tax), else standard retention (PRD §7).
- **TDD plan:**
  - Unit (`SettingsAuditTests`): `SettingsChange_WritesAuditEntry_WithBeforeAfter`, `SecretChange_RedactsValue`, `RejectedChange_WritesFailedRow`.
  - Integration (`AuditLogBehaviorSettingsTests`, testcontainer): asserts the entry lands in `identity.audit_log` with actor + diff.
  - E2E: `settings-audit.e2e.ts` — owner changes a fee, the recent-changes panel shows their name + old→new.
- **Technical notes:** Reuse `identity.audit_log` + `AuditLogBehavior` (CURRENT-STATE §4). Settings commands implement `IAuditable`. New read query `GetSettingsChangesQuery(section, tenant?)` for the UI panel. Value diffing in the handler; redact when `key` matches a secret pattern.
- **UI/UX spec:** Recent-changes panel per section: reverse-chronological list, actor avatar/name, "changed {key} from {old} to {new}", relative + absolute timestamp; "view all" opens a filtered audit view; a11y — list semantics, readable diffs (not color-only). Empty state "No changes yet."
- **Configuration:** No runtime keys. Retention governed by PRD §7 (financial 7y).
- **Rollout:** No flag (audit is always-on). Migration: none if `audit_log` suffices; else additive columns. Backward-compat: existing audit rows unaffected. Rollback: revert (loses new panel only).
- **Observability:** Metric `settings_changes_total{section}`; alert on unexpected platform-fee/tier changes (sensitive); log actor on each change.
- **Definition of Done:** tests green → review → staging shows real actor+diff → prod verified → retention documented.
- **Dependencies:** blocked-by none (reuses Identity audit). Blocks VRB-210 (display) and every product settings story (they emit audit entries).
- **Parallelisation:** Identity + Web lanes; owns `identity.audit_log` reads, `IAuditable` settings commands, `GetSettingsChangesQuery`, recent-changes panel.

### VRB-212 — Property / listing settings
- **Epic:** Configuration & Settings · **Priority:** Must · **Estimate:** M
- **Narrative:** As a host, I want a settings screen to manage a property's core listing settings (title, description, capacity, house rules, amenities, activation state, turnover hours), so that I control how my listing appears and behaves without editing raw data.
- **Acceptance criteria:**
  - Given a property I own **When** I open its settings **Then** I can edit title, description, capacity, house rules, amenity selection, and turnover hours (`TurnoverHours` — OPS.M.16), with validation and safe defaults.
  - Given activation **Then** the gated `Activate(tenantStatus, chargesEnabled, payoutsEnabled)` rule is surfaced — I cannot publish unless the tenant is Stripe-ready, and the UI explains why if blocked (CURRENT-STATE §6).
  - Given amenities **Then** I select from the platform amenity catalog (platform-admin-owned CRUD); I cannot invent amenities.
  - Given I save **Then** changes are tenant-scoped (RLS + `HasTenantRole(tid,"tenant_admin")`) and audited (VRB-211).
  - Given image management **Then** the settings screen links to the photo gallery manager (the 501 image endpoints are a separate Must-fix story G10; this story owns the settings, not the upload plumbing).
- **TDD plan:**
  - Unit: `PropertySettingsForm.test.tsx` (validation, activation-blocked state), handler `UpdatePropertySettingsTests`.
  - Integration (`PropertySettingsTests`, testcontainer): owner updates settings; cross-tenant write rejected (`CrossTenantAccessException`).
  - E2E: `property-settings.e2e.ts` — owner edits title+turnover, saves, sees audit line; activation blocked when Stripe not ready.
- **Technical notes:** Catalog module (`src/Modules/VrBook.Modules.Catalog`), `Property` aggregate. New/extended `UpdatePropertySettingsCommand` (`IAuditable`, `ITenantScoped`). Renders in VRB-210 shell. Amenity catalog read from platform CRUD.
- **UI/UX spec:** States: loading/editing/saving/success/error/activation-blocked (with reason). Responsive single→two column. A11y: labelled fields, error focus, amenity multi-select keyboard-navigable. Safe defaults: turnover hours default from OPS.M.16; capacity min 1. Audit panel per VRB-211.
- **Configuration:** No runtime keys; per-property DB-stored settings. `TurnoverHours` default per OPS.M.16.
- **Rollout:** Behind `Features:Admin.SettingsUi`. Migration: none (fields exist). Backward-compat: existing property edit page remains until migrated. Rollback: flag off.
- **Observability:** Log setting updates; metric `property_settings_updated_total`.
- **Definition of Done:** tests green → review → staging owner edits verified → prod → monitored.
- **Dependencies:** blocked-by VRB-210, VRB-211. Relates to G10 (image upload) story.
- **Parallelisation:** Catalog + Web lanes; owns `Catalog` property-settings command + `web/src/app/admin/settings/property/**`.

### VRB-213 — Pricing rules, fees, taxes & minimum-stay settings
- **Epic:** Configuration & Settings · **Priority:** Must · **Estimate:** L
- **Narrative:** As a host, I want to configure my property's pricing (base/weekend rates, min/max stay, pricing rules, fees) and see how platform tax (Stripe Tax) and the platform fee apply, so that guests get a transparent, legally-correct quote.
- **Acceptance criteria:**
  - Given a property's pricing plan **When** I edit **Then** I can set base nightly rate, weekend rate, min/max stay, and the 3 rule kinds (DateRangeOverride / LastMinute / LengthOfStay) with drag-reorder (existing capability, moved into settings shell).
  - Given fees **Then** I can configure the property fee model (cleaning etc.) with validation (non-negative, currency-scoped).
  - Given tax **Then** the UI shows that tax is **platform-calculated via Stripe Tax** with VrBook as marketplace facilitator (Q5/Q25) — the host does not enter tax rates; a quote preview shows nightly + fees + tax + platform fee breakdown.
  - Given the platform fee (default 1500 bps, platform-admin-set — Q4) **Then** it is shown read-only to the host (whether owners see it is per Q4; display as configured by VRB-216).
  - Given min-stay < 1 or max < min **Then** validation blocks save.
  - Given I save **Then** it's tenant-scoped + audited; changes reflect in the next anonymous quote.
- **TDD plan:**
  - Unit: `PricingSettingsForm.test.tsx` (min/max validation, rule reorder), `PricingRuleValidationTests` (per-kind).
  - Integration (`PricingSettingsTests`): update plan → quote reflects; cross-tenant rejected.
  - E2E: `pricing-settings.e2e.ts` — owner sets rates + rule, quote preview updates with tax + fee breakdown.
- **Technical notes:** Pricing module (`src/Modules/VrBook.Modules.Pricing`), `PricingPlan`/`PricingRule`. Tax integration is the Stripe-Tax Must-story (PRD §6) — this story surfaces its output in the quote preview, not the engine. Fee model exists (◑). Platform fee read from Tenant `PlatformFeeBps`.
- **UI/UX spec:** States: loading/editing/saving/success/error; live quote-preview panel. Responsive. A11y: dnd rule-reorder keyboard-operable (`@dnd-kit` keyboard sensor), labelled rate fields, error focus. Safe defaults: weekend rate = base if unset; min-stay 1. Audit panel.
- **Configuration:** No per-key config; `Refund:ServiceFeePercent` (matrix 26) surfaced read-only; `Stripe:*` for tax. Tenant `PlatformFeeBps` default 1500.
- **Rollout:** Behind `Features:Admin.SettingsUi`; tax preview behind `Features:Pricing.StripeTax`. Backward-compat: existing pricing page remains. Rollback: flags off.
- **Observability:** Log pricing changes; metric `pricing_settings_updated_total`; quote-preview error rate.
- **Definition of Done:** tests green → review → staging quote reflects edits + tax breakdown → prod → monitored.
- **Dependencies:** blocked-by VRB-210, VRB-211; relates to Stripe-Tax story + VRB-216 (fee display) + VRB-217 (currency).
- **Parallelisation:** Pricing + Web lanes; owns `Pricing` settings + `web/src/app/admin/settings/pricing/**`.

### VRB-214 — Availability rules + iCal endpoints & cadence settings
- **Epic:** Configuration & Settings · **Priority:** Must · **Estimate:** M
- **Narrative:** As a host, I want to manage availability rules, calendar blocks, and my inbound/outbound iCal feeds (including poll cadence), so that my calendar stays in sync with external channels without double-booking.
- **Acceptance criteria:**
  - Given my property **When** I open availability settings **Then** I can add/remove availability blocks and view the calendar (existing capability in settings shell).
  - Given channel feeds **Then** I can add an inbound iCal URL, set its per-feed poll cadence, remove it, and copy my outbound `.ics` feed URL (`/feeds/{token}.ics`) — existing ChannelFeeds CRUD surfaced in settings.
  - Given the outbound feed token **Then** it is signed with the server-side pepper (`Feed:OutboundTokenPepper`, KV) — never exposed; the URL is copy-to-clipboard.
  - Given the inbound poll cadence **Then** it is a per-feed DB value (not the dead global `Sync:DefaultPollIntervalMinutes` — see VRB-208), validated to a sane range (e.g. 15–1440 min).
  - Given the per-host outbound rate-limit policy **Then** its config (host suffixes/token/window/burst — today hard-coded `ChannelPollOptions.cs:23-30`) is moved to config (shared with VRB-205) and documented as per-replica (G28).
  - Given I save **Then** tenant-scoped + audited; a sync conflict surfaces in the existing conflicts view.
- **TDD plan:**
  - Unit: `AvailabilitySettingsForm.test.tsx`, `FeedCadenceValidationTests` (range), `ChannelPollOptionsBindingTests`.
  - Integration (`ChannelFeedSettingsTests`, testcontainer): add feed → poll picks it up; cross-tenant rejected.
  - E2E: `availability-settings.e2e.ts` — owner adds inbound feed + copies outbound URL + adds a block.
- **Technical notes:** Sync module (`src/Modules/VrBook.Modules.Sync`), `ChannelFeed`/`AvailabilityBlock`. `ChannelPollOptions.cs` bound from a new `ChannelPoll:*` section. Sync worker cadence per-feed. Feed token pepper unchanged.
- **UI/UX spec:** States: loading/editing/saving/success/error/conflict-warning. Calendar responsive. A11y: calendar keyboard-navigable, feed-URL copy button with `aria-live` confirmation, labelled cadence input. Safe defaults: cadence 30 min. Audit panel.
- **Configuration:** `ChannelPoll:*` (moved from hard-coded, matrix 54), per-feed cadence (DB), `Feed:OutboundTokenPepper` (KV, matrix 11). Retires global Sync keys via VRB-208.
- **Rollout:** Behind `Features:Admin.SettingsUi`. Migration: none (feeds exist). Backward-compat: existing channel-feeds page remains. Rollback: flag off.
- **Observability:** Existing sync-run metrics; alert on stale feed (if `StaleAlertHours` wired in VRB-208); rate-limiter rejection metric.
- **Definition of Done:** tests green → review → staging feed add + poll verified → prod → monitored.
- **Dependencies:** blocked-by VRB-210, VRB-211; overlaps VRB-205 + VRB-208.
- **Parallelisation:** Sync + Web lanes; owns `Sync` feed settings, `ChannelPollOptions.cs`, `web/src/app/admin/settings/availability/**`.

### VRB-215 — Cancellation & refund policy configuration (2 models, per-property)
- **Epic:** Configuration & Settings · **Priority:** Must · **Estimate:** L
- **Narrative:** As a host, I want to choose my property's cancellation policy from the two supported models (tiered refund with platform-set tiers, or refundable-rate upgrade), so that guests see clear refund terms and refunds are computed correctly (Q3/Q24, owner-locked 2026-07-13).
- **Acceptance criteria:**
  - Given a property **When** I open cancellation settings **Then** I pick exactly one of: **(1) Tiered refund** — thresholds+percentages are **platform-admin-set global values** (VRB-216), I only opt in; or **(2) Refundable-rate upgrade** — guest pays extra at booking for a full-refund option, else the booking is non-refundable (Q24).
  - Given the tiered model **Then** the current global tiers (default: full ≥7d / 50% 2–7d / none <48h — Q24 proposal) are shown read-only; I cannot edit the numbers (platform owns them).
  - Given the refundable-upgrade model **Then** I configure the upgrade price/percentage; the refund requires the request to arrive before check-in.
  - Given a captured (Confirmed) booking **Then** the chosen policy drives the refund amount; Tentative/unconfirmed bookings are always fully released (Q24).
  - Given the policy is snapshotted per booking at Place **Then** later policy changes don't retroactively alter existing bookings.
  - Given per-item display (cross-business-cart-ready) **Then** the policy is attached per line item and shown per-item (Q3R/Q24R design principle) — even though the cart is Phase 3/4, the storage shape is per-line now.
  - Given I save **Then** tenant-scoped + audited; guest-facing policy text updates on the listing.
- **TDD plan:**
  - Unit: `CancellationPolicyForm.test.tsx` (model switch, upgrade-price validation), `RefundCalculationTests` (tiered boundaries: 7d/2d/48h; refundable-upgrade paid vs unpaid).
  - Integration (`CancellationPolicySnapshotTests`, testcontainer): policy snapshot at Place; global-tier change doesn't alter existing booking.
  - E2E: `cancellation-settings.e2e.ts` — owner picks tiered, guest sees terms; owner switches to refundable-upgrade with confirm modal.
- **Technical notes:** Booking + Payment modules; `CancellationPolicy` enum exists on `Booking` (Q3). Refund path (`RefundForBookingCommand`) — note the fee-reversal defect G37 is a separate payment story. Snapshot policy onto the booking line at Place. Global tiers read from VRB-216. Design per the locked "standardize framework, localize values" principle.
- **UI/UX spec:** States: loading/editing (model radio)/saving/success/error; switching models requires a confirm modal (destructive-ish). Guest-facing preview of the policy text. Responsive. A11y: radio-group semantics, `aria-describedby` on each model, error focus. Safe defaults: tiered model with current global tiers. Audit panel (policy changes are sensitive).
- **Configuration:** No per-key runtime config for the host side; global tiers via VRB-216. Policy stored per property/line.
- **Rollout:** Behind `Features:Admin.SettingsUi` + `Features:Booking.CancellationEngine`. Migration: per-line policy snapshot column (additive). Backward-compat: existing bookings default to Flexible/tiered. Rollback: flag off (falls back to current enum behaviour).
- **Observability:** Log policy selection changes; metric `cancellation_policy_by_model`; alert on refund-calc errors.
- **Definition of Done:** tests green → review → staging both models verified end-to-end (owner set → guest sees → refund computes) → prod → monitored.
- **Dependencies:** blocked-by VRB-210, VRB-211, VRB-216 (global tiers). Relates to refund fee-reversal (G37) + tax posture (G38) payment stories.
- **Parallelisation:** Booking + Payment + Web lanes; owns cancellation-policy settings command, refund calc, `web/src/app/admin/settings/cancellation/**`.

### VRB-216 — Platform global cancellation-tier + fee + tax-posture table
- **Epic:** Configuration & Settings · **Priority:** Must · **Estimate:** M
- **Narrative:** As a platform admin, I want to own the global cancellation-tier numbers, the default/per-tenant platform fee, and the tax posture, so that tenants localize *which* model they use while the platform standardizes the framework values (locked design principle 2026-07-13).
- **Acceptance criteria:**
  - Given `/admin/platform/settings` (PlatformAdmin only — `[Authorize(Roles="PlatformAdmin")]`) **When** I edit the tiered-refund table **Then** I set the thresholds+percentages (e.g. full ≥7d / 50% 2–7d / none <48h) as **global values** applied to every property that opts into the tiered model (Q24).
  - Given the platform fee **Then** I set the default (1500 bps) and per-tenant overrides (Q4) — this is the only place fee numbers are edited; hosts see them read-only (VRB-213).
  - Given the tax posture **Then** I confirm Stripe Tax + marketplace-facilitator is active and view per-state enablement (Q25) — engine config, not per-rate entry.
  - Given a tier or fee change **Then** it is heavily audited (VRB-211) and does **not** retroactively change already-snapshotted bookings (VRB-215).
  - Given a tenant-admin **Then** these screens are 403 (platform-only).
- **TDD plan:**
  - Unit: `GlobalTierForm.test.tsx` (monotonic thresholds, percent 0–100), `PlatformFeeValidationTests` (bps 0–10000).
  - Integration (`PlatformSettingsTests`): platform-admin sets tiers/fee; tenant-admin 403; change audited; existing snapshots unaffected.
  - E2E: `platform-settings.e2e.ts` — platform admin edits tiers + a tenant fee override.
- **Technical notes:** Extends the platform-admin console (`admin/platform/*`, CURRENT-STATE §7). Tenant `PlatformFeeBps ∈ [0,10000]` already exists. Global tiers → a new platform-scoped config table (Admin module `AdminModule.cs` — currently a no-op stub, natural home). Tax posture reads Stripe Tax config.
- **UI/UX spec:** States: loading/editing/saving/success/error; confirm modal on tier/fee change (sensitive, platform-wide). A11y: table-edit keyboard-navigable, validation on each cell, error focus. Safe defaults: current Q24 proposal tiers, 1500 bps. Prominent audit panel.
- **Configuration:** Global tiers (DB, platform-scoped), `PlatformFeeBps` default 1500 (tenant column), Stripe Tax posture (via `Stripe:*`). No new appsettings keys.
- **Rollout:** Behind `Features:Admin.SettingsUi`. Migration: global-tier table (additive). Backward-compat: seed with the current defaults. Rollback: flag off.
- **Observability:** Alert on any platform-fee/tier change (sensitive); metric `platform_tier_config_version`; audit every change with actor.
- **Definition of Done:** tests green → review → staging platform-admin edits verified + tenant read-only confirmed → prod → monitored.
- **Dependencies:** blocked-by VRB-210, VRB-211. Blocks VRB-215 (tiers) + VRB-213 (fee display).
- **Parallelisation:** Admin/Identity + Web lanes; owns platform-settings command + global-tier table + `web/src/app/admin/platform/settings/**`.

### VRB-217 — Payment / payout configuration (Stripe Connect, currency, schedule)
- **Epic:** Configuration & Settings · **Priority:** Must · **Estimate:** M
- **Narrative:** As a host, I want to complete Stripe Connect onboarding, set my settlement currency, and view my payout schedule from a settings screen, so that I can receive payouts and understand when funds arrive.
- **Acceptance criteria:**
  - Given payment settings **When** I open them **Then** I see my Stripe Connect Express onboarding status and can start/resume onboarding (existing onboarding wizard surfaced in settings); a property cannot publish until `chargesEnabled && payoutsEnabled` (ties to VRB-212 activation gate).
  - Given currency **Then** I set my tenant's single settlement currency (Q9, single-currency-per-tenant at launch, no FX); the UI notes display-currency+FX arrives with the Phase 3 cart.
  - Given payout schedule **Then** I view (and where Stripe permits, set) the payout cadence; changes proxy to Stripe.
  - Given the platform fee **Then** it's shown read-only (from VRB-216) so I understand my net.
  - Given onboarding incomplete **Then** the screen clearly lists the remaining Stripe requirements and blocks publish with an explanation.
  - Given I change currency/schedule **Then** it's tenant-scoped + audited.
- **TDD plan:**
  - Unit: `PayoutSettingsForm.test.tsx` (currency select, onboarding-status states), `TenantCurrencyValidationTests`.
  - Integration (`StripeConnectSettingsTests`, sandbox): onboarding link generated; readiness reflected; cross-tenant rejected.
  - E2E: `payout-settings.e2e.ts` — owner resumes onboarding, sets currency, sees payout schedule.
- **Technical notes:** Payment + Identity modules; `StripeGateway.cs` onboarding/account-links (CURRENT-STATE §10), Tenant Stripe-readiness. Tenant `DefaultCurrency` (Q9). `NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY` (declared-but-unread today, matrix 37) wired for Elements. Note MoR/tax posture defect G38 is a separate payment story.
- **UI/UX spec:** States: loading/onboarding-incomplete (requirements list)/onboarding-complete/saving/success/error. Responsive. A11y: status announced via `aria-live`, labelled currency select, error focus. Safe defaults: currency = tenant default. Audit panel.
- **Configuration:** Tenant `DefaultCurrency`; `Stripe:*` (KV, matrix 23–25); `NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY` wired. Per Q9 no FX at launch.
- **Rollout:** Behind `Features:Admin.SettingsUi`. Migration: none (currency exists). Backward-compat: existing onboarding wizard remains. Rollback: flag off.
- **Observability:** Log onboarding-status transitions; metric `stripe_onboarding_completed_total`; alert on onboarding-failure spike.
- **Definition of Done:** tests green → review → staging onboarding + currency verified (sandbox) → prod (live via VRB-204) → monitored.
- **Dependencies:** blocked-by VRB-210, VRB-211, VRB-201 (Stripe secrets), VRB-204 (live keys for prod). Relates to VRB-216 (fee).
- **Parallelisation:** Payment + Web lanes; owns payout-settings command + `web/src/app/admin/settings/payouts/**`.

### VRB-218 — Notification templates, channels & sender identity
- **Epic:** Configuration & Settings · **Priority:** Should · **Estimate:** M
- **Narrative:** As a host/platform admin, I want to manage notification templates, choose channels, and configure the sender identity, so that guests receive correctly-branded, deliverable notifications from a verified domain.
- **Acceptance criteria:**
  - Given notification settings **When** I open them **Then** I can view/edit the templates for the notification types (booking placed/confirmed/rejected/cancelled, etc.) with variable placeholders and a preview.
  - Given channels **Then** I see the enabled channels (email via ACS today; SMS/push are future) and can toggle where supported.
  - Given sender identity **Then** the sender address/domain is shown; it must be a DKIM-verified domain (VRB-204) — the `donotreply@vrbook.example.com` placeholder is replaced, and an invalid/unverified sender is flagged.
  - Given a template with an undefined variable **Then** validation blocks save.
  - Given a save **Then** it's scoped appropriately (platform-default templates vs tenant overrides), audited, and used by the notifications worker on next dispatch.
  - Given a test-send **Then** I can send a preview to myself.
- **TDD plan:**
  - Unit: `NotificationTemplateForm.test.tsx` (variable validation, preview render), `TemplateRenderTests` (placeholder substitution, missing-var error).
  - Integration (`NotificationSettingsTests`, testcontainer): edit template → worker dispatch uses it; sender validated.
  - E2E: `notification-settings.e2e.ts` — edit a template, preview, test-send.
- **Technical notes:** Notifications module (`src/Modules/VrBook.Modules.Notifications`), `AzureEmailSender.cs`, notifications worker. Templates stored (platform default + tenant override). Sender from `Acs:SenderAddress` (KV, matrix 29) — validated against verified domain. `App:WebBaseUrl` for links.
- **UI/UX spec:** States: loading/editing/preview/saving/success/error/unverified-sender-warning. Responsive editor. A11y: labelled template fields, live preview region `aria-live`, error focus, variable-insert menu keyboard-operable. Safe defaults: platform default templates. Audit panel.
- **Configuration:** `Acs:{ConnectionString,SenderAddress}` (KV, matrix 28–29); templates in DB; `App:WebBaseUrl` (matrix 40).
- **Rollout:** Behind `Features:Admin.SettingsUi`. Migration: template table if not present (additive). Backward-compat: fall back to code/default templates until overridden. Rollback: flag off.
- **Observability:** Deliverability metric (DKIM/DMARC pass), dispatch-failure metric (existing notifications retry), template-render-error metric.
- **Definition of Done:** tests green → review → staging edit+test-send verified → prod (DKIM domain via VRB-204) → monitored.
- **Dependencies:** blocked-by VRB-210, VRB-211; sender identity blocked-by VRB-204 (DKIM). 
- **Parallelisation:** Notifications + Web lanes; owns template settings command + `web/src/app/admin/settings/notifications/**`.

### VRB-219 — Branding / domain / SEO metadata per property
- **Epic:** Configuration & Settings · **Priority:** Should · **Estimate:** M
- **Narrative:** As a host, I want per-property branding, canonical domain, and SEO metadata settings, so that my listing ranks and looks right in search + social — the direct-booking moat depends on organic traffic (PRD §6).
- **Acceptance criteria:**
  - Given a property **When** I open branding/SEO settings **Then** I can set the SEO title, meta description, canonical URL, and social/OG image, with length hints and a search/social preview.
  - Given the site URL **Then** it comes from `NEXT_PUBLIC_SITE_URL` (currently never set → hard-coded `www.vrbook.example.com` fallback — matrix 31) which this story wires per env, so canonical/OG URLs are correct.
  - Given SEO **Then** `sitemap.xml` + `robots.txt` presence is confirmed/added (G34) and per-property JSON-LD reflects the metadata.
  - Given a missing meta description **Then** a safe default is generated from the listing description.
  - Given I save **Then** tenant-scoped + audited; public SSR pages (`/properties/[slug]`) reflect the metadata.
- **TDD plan:**
  - Unit: `SeoSettingsForm.test.tsx` (length hints, preview), `CanonicalUrlTests` (uses `NEXT_PUBLIC_SITE_URL`).
  - Integration/E2E: `seo-metadata.e2e.ts` — set meta, view rendered `<head>` + JSON-LD on the public page; sitemap.xml/robots.txt reachable.
- **Technical notes:** Catalog module + `web/src/app/properties/[slug]` + `layout.tsx:14` (`NEXT_PUBLIC_SITE_URL`). Add `sitemap.xml`/`robots.txt` route handlers. Per-property SEO fields (additive migration).
- **UI/UX spec:** States: loading/editing/saving/success/error; live Google/social preview cards. Responsive. A11y: labelled fields, char-count `aria-live`, error focus, image-upload alt-text required. Safe defaults: title/description derived from listing. Audit panel.
- **Configuration:** `NEXT_PUBLIC_SITE_URL` wired per env (dev localhost, staging web origin, prod domain — matrix 31); per-property SEO fields (DB).
- **Rollout:** Behind `Features:Admin.SettingsUi`. Migration: SEO columns (additive). Backward-compat: fall back to derived metadata. Rollback: flag off.
- **Observability:** Core Web Vitals monitoring (PRD LCP/INP/CLS budgets); log SEO changes; sitemap generation metric.
- **Definition of Done:** tests green → review → staging metadata renders in `<head>`+JSON-LD, sitemap/robots reachable → prod → CWV monitored.
- **Dependencies:** blocked-by VRB-210, VRB-211; relates to VRB-205 (`NEXT_PUBLIC_SITE_URL`).
- **Parallelisation:** Catalog + Web lanes; owns SEO settings command + `web/src/app/properties/[slug]` head + sitemap/robots routes.

### VRB-220 — Roles / permissions + admin user management
- **Epic:** Configuration & Settings · **Priority:** Must · **Estimate:** M
- **Narrative:** As a platform admin (and, scoped, a tenant admin), I want to manage admin users and their roles/memberships, so that the right people have the right access — honouring the owner-locked admin-vs-guest IdP split and the post-M.21 role authority shape.
- **Acceptance criteria:**
  - Given platform-admin user management (`admin/platform/users`) **Then** I can pre-seed admins (existing `POST /admin/platform/users/seed`, OPS.M.22), grant/revoke `is_platform_admin`, and view users — Entra-local email+password only (ADR-0016); **no social IdP** on the admin surface.
  - Given tenant membership management **Then** a tenant admin manages memberships within their tenant via `HasTenantRole(tid,"tenant_admin")`; roles are `tenant_admin` / `tenant_member` (no `IsOwner`/`IsAdmin` literals — post-M.21).
  - Given role changes **Then** they materialize correctly: global `is_platform_admin` → `ClaimTypes.Role="PlatformAdmin"`; per-tenant → `MembershipRoles` / `HasTenantRole`.
  - Given an admin not yet provisioned **Then** the pre-seed gate applies (`admin_account_not_provisioned`, OPS.M.22).
  - Given any role/membership change **Then** it is audited (VRB-211, sensitive) and cannot be done cross-tenant (RLS + pipeline).
  - Given the UI **Then** platform-only actions are hidden/403 for tenant admins.
- **TDD plan:**
  - Unit: `RoleManagementForm.test.tsx` (role options, platform-only gating), `MembershipCommandTests`.
  - Integration (`AdminUserManagementTests`): platform-admin seeds+promotes; tenant-admin manages own tenant only; cross-tenant rejected; audit written.
  - Architecture: `NoLegacyRoleLiterals` (existing `OpsM15_*`/`OpsM17_*` arch tests) stay green — no `IsOwner`/`IsAdmin`/`Owner,Admin`.
  - E2E: `role-management.e2e.ts` — platform admin pre-seeds an admin; tenant admin adds a member.
- **Technical notes:** Identity module; `identity.users.is_platform_admin`, `identity.tenant_memberships`, `UserProvisioningMiddleware`, existing seed endpoint (OPS.M.22). Reuse `HasTenantRole` + `IsPlatformAdmin`; `[Authorize(Roles="PlatformAdmin")]` for platform surface. No new role literals.
- **UI/UX spec:** States: loading/editing/saving/success/error/not-provisioned. Responsive. A11y: role selects labelled, confirm modal on grant/revoke (sensitive), error focus. Safe defaults: least privilege. Prominent audit panel. Admin sign-in surface is email+password only (no social buttons — ADR-0016).
- **Configuration:** `EntraExternalId:AdminFlowName` (VRB-209) governs admin provisioning; no new keys. Role authority per CLAUDE.md frozen shape.
- **Rollout:** Behind `Features:Admin.SettingsUi`. Migration: none (columns exist). Backward-compat: existing platform-tenant console remains. Rollback: flag off.
- **Observability:** Audit + alert on any `is_platform_admin` grant/revoke and membership change (sensitive); metric `admin_role_changes_total`.
- **Definition of Done:** arch tests stay green → unit+integration+E2E green → review → staging platform+tenant flows verified → prod → monitored.
- **Dependencies:** blocked-by VRB-210, VRB-211; coordinates with VRB-209 (admin flow) + OPS.M.22 (pre-seed).
- **Parallelisation:** Identity + Web lanes; owns membership/role commands + `web/src/app/admin/platform/users/**` + `web/src/app/admin/settings/team/**`.

---

## Cross-story notes

- **Sequencing:** VRB-200 → VRB-201 → VRB-202 land first (guardrails + secrets + matrix). VRB-206..209 are independent small fixes that can run in parallel behind VRB-200. VRB-210 + VRB-211 unblock all product-settings stories (VRB-212..220), which then parallelize across module lanes.
- **Feature flags:** every product-settings screen ships behind `Features:Admin.SettingsUi` (VRB-203), enabling incremental migration off the current scattered admin pages.
- **Audit everywhere:** every settings write implements `IAuditable` and reuses the Identity `AuditLogBehavior` — no parallel audit system (VRB-211).
- **Owner-locked invariants honoured:** admin surfaces Entra-local only (ADR-0016); role authority `is_platform_admin` + `tenant_memberships.role` (post-M.21); new `[Authorize(Roles=...)]` are `PlatformAdmin`-only; no `IsOwner`/`IsAdmin` literals.
- **Config-defect coverage:** G1→VRB-206, G2→VRB-207, G3/G4→VRB-208, G5→VRB-200, G6→VRB-201, G7→VRB-209, G8→VRB-205, G13→VRB-203, G28→VRB-205/214, G34→VRB-219, G35→VRB-204.
