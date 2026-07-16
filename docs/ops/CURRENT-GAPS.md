# VrBook — Gaps, Stubs & Defects Register (as of 2026-07-11)

Prioritized register of everything incomplete, stubbed, or defective, from the Phase-1 code audit. Each becomes (or maps to) a user story in Phase 3. **Sev:** P0 = correctness/go-live blocker · P1 = fix before/at launch · P2 = tech debt / post-launch.

## A. Correctness defects (config that silently doesn't work)

| # | Sev | Defect | Evidence | Impact |
|---|---|---|---|---|
| G1 | **P1** | Loyalty tier thresholds hard-coded in domain; `Loyalty:*Threshold` config is dead | `LoyaltyAccount.cs:65-66` vs appsettings + `main.bicep:381-383` | Ops can't tune loyalty without a code change; config is a lie |
| G2 | **P1** | Booking Tentative SLA hard-coded to 24h; `Booking:TentativeSlaHours` never read; Bicep comment says "6h" | `Booking.cs:119` | Can't tune the hold-expiry window; internal inconsistency |
| G3 | P2 | `Booking:HoldDurationMinutes` + `Sync:DefaultPollIntervalMinutes`/`StaleAlertHours` have no consumers | — | Dead config |
| G4 | P2 | `Sync__DefaultPollIntervalMin` (Bicep) ≠ `Sync:DefaultPollIntervalMinutes` (appsettings) | `main.bicep:374` | Even if wired, the key wouldn't bind |
| G5 | ✅ **RESOLVED (VRB-200)** | ~~No fail-fast config validation — missing Entra config boots the API with **no token validation** (silent)~~ Fixed: every required section is bound via `AddValidatedConfiguration` with `.ValidateOnStart()` + a cross-field `IValidateOptions<T>`; Staging/Production now crash on missing `EntraExternalId:*` (Development keeps a warned dev-loopback carve-out) | was `AuthExtensions.cs:30-32`; now `src/VrBook.Api/Configuration/ConfigValidationExtensions.cs` | Closed — a misconfigured Staging/Production deploy fails readiness instead of serving unauthenticated |
| G6 | ✅ **RESOLVED (VRB-201)** | ~~`stripe-publishable-key` + `acs-sender-address` referenced by Bicep but not seeded~~ Fixed: both now seeded as placeholders in `10-store-secrets.ps1`; `acs-connection-string` producer (`acs.bicep`) documented; orphans `sendgrid-key`/`b2c-api-client-secret` removed; a `SeedSecretsParityTests` build gate now fails if any Bicep secretRef is unseeded | `10-store-secrets.ps1`; `tests/VrBook.Architecture.Tests/Infra/SeedSecretsParityTests.cs` | Closed — a first/clean deploy can no longer fail on an unresolved secretRef |
| G7 | P2 | `EntraExternalId:AdminFlowName` read but never provided; admin-flow gate inert until set | `UserProvisioningMiddleware.cs:143` | Admin-vs-guest surface enforcement depends on undocumented config |
| G8 | P2 | Hard-coded staging API FQDN in web build-arg | `cd-staging-web.yml:149` | Breaks on any CAE environment rebuild |
| G37 | **P1** | **Application-fee reversal on refund is a no-op** — reversal cents written to refund metadata only, never executed via `ApplicationFeeRefundService` | `StripeGateway.cs:135-144`; `RefundForBookingCommand.cs` (found in the 2026-07-13 independent design review, C4) | Platform fee not clawed back on refund; over-refund/negative-balance guard is approximate. Launch-relevant. |
| G38 | **P1** | **Single-tenant charge sets `OnBehalfOf=supplier`, making the SUPPLIER the merchant-of-record** — contradicts the platform-as-marketplace-facilitator tax posture (Q25) | `StripeGateway.cs:71` (review C5) | Tax liability sits on the wrong entity; incompatible with Stripe-Tax-as-facilitator. Launch-relevant. |
| G39 | P2 | **`POST /api/v1/admin/platform/tenants/{id}/suspend` with a missing `[JsonRequired]` `Reason` returns 500, not a 400 validation problem** — surfaced by VRB-300 CI (Contract test disproved a 400 assertion; observed 500). Suggests the required-field violation escapes model-state validation and throws in the pipeline/handler | `TenantsPlatformController.cs:53` + `SuspendTenantRequest`; VRB-300 CI run 29362869190 | Bad input yields an opaque 500 instead of a documented 400; **Platform lane** to verify with a live DB + fix. |
| G40 | P2 | **`GET /api/v1/admin/properties/{ownId}` returned 500 (not 200) and `GET /api/v1/admin/properties` omitted the seeded property inside the `TwoTenantApiFixture` shared collection** — VRB-300 Contract-test observation; contradicts static code analysis (works on staging edit page), so likely shared-collection state / fixture ordering rather than a prod defect | `AdminPropertiesController` + `GetPropertyDetailByIdHandler`; VRB-300 CI run 29362869190 | **CATALOG lane** to reproduce with Docker + confirm prod-vs-fixture; if fixture-only, harden the shared-collection seed/isolation. |
| G41 | P2 | **No exact notification-queue-depth metric** — VRB-306 alerts on drain via the dispatch worker's log line (failed/dead-lettered + worker-silent), which catches failures and a stopped worker but not a slow *backlog* of `Queued` rows older than N min while the worker still ticks. A precise gauge needs the worker to emit `count(NotificationLog WHERE status='Queued' AND created < now()-10m)` as a custom metric | `VrBook.Workers.Notifications` + `NotificationsDbContext`; VRB-306 (TL-approved log-based proxy 2026-07-15) | **Notifications feature lane** to emit the stale-Queued gauge; then add an exact `drain-lag>10min` metric alert to `infra/modules/alerts.bicep`. |

## B. Stubbed / unimplemented features (return 501 or no-op)

| # | Sev | Item | Evidence |
|---|---|---|---|
| G9 | **P1** | **Outbox → Service Bus relay not implemented** — domain events dispatch in-process only; outbox rows never marked dispatched | `OutboxMessage.cs:10-13`, `DomainEventOutboxInterceptor.cs:83-89` |
| G10 | **P1** | Property **image upload/order/delete** endpoints → 501 (listings can't get photos via API) | `PropertiesController.cs` (A2.1) |
| G11 | P2 | Booking admin **queue** + **manual booking** → 501 | `BookingsController.cs` (A4.1) |
| G12 | P2 | Message **attachments** → 501 | `ThreadsController.cs` (A7.5) |
| G13 | ✅ **RESOLVED (VRB-203)** (feature flags) | ~~Feature-flag runtime is a no-op stub; `TogglesController` → 501~~ Fixed: `DbFeatureToggle` (DB override → config → default, 30s IMemoryCache) replaces `StubFeatureToggle`; `admin.feature_flags` table + `GET/PUT /admin/toggles` (PlatformAdmin) live; `Features:<Area>.<Capability>` naming convention; `Loyalty:Enabled`→`Features:Loyalty.Enabled` (live-togglable) + `Features:UseRedisHoldStore`→`Features:Booking.UseRedisHoldStore`. **`AlertsController` → 501 still open** (separate slice). | `src/Modules/VrBook.Modules.Admin/**`, `AdminController.cs` (toggles) |
| G14 | P2 | **Admin module** is a no-op bounded context (TODO) | `AdminModule.cs:17-25` |
| G15 | P2 | Infra stubs: `StubTaxCalculator`, `StubBookingAvailabilityReader`, `StubExternalChannelConflictChecker` (partly overridden), `NullRealtimeNotifier` fallback | `src/VrBook.Infrastructure/Stubs/` |
| G16 | P2 | Stripe **dispute** (`charge.dispute.created`) only logs — no dispute domain event/workflow | `HandleStripeWebhookCommand.cs` |

## C. Frontend gaps

| # | Sev | Item | Evidence |
|---|---|---|---|
| G17 | **P1** | Home `/` "Featured stays" is **hardcoded placeholder data** ("Featured properties land here") | `web/src/app/page.tsx:23-45` |
| G18 | **P1** | `/account/profile` is a **stub** (dashed placeholder) | `web/src/app/account/profile/page.tsx` |
| G19 | **P1** | **No mobile navigation** — primary nav is `hidden md:flex`; links vanish on mobile | `SiteHeaderNav` |
| G20 | P2 | **No shared UI component library** — only `ui/ConfirmActionModal`; buttons/cards/badges ad-hoc inline Tailwind, copy-pasted per page; two parallel color systems (semantic tokens vs raw `brand-*`) | `web/src/components/ui/` |
| G21 | P2 | OpenAPI **generated API client empty**/not committed; all access hand-maintained | `web/src/lib/api/generated/` |
| G22 | P2 | Frontend unit tests skew to auth guards; thin on feature-component rendering | `web/src/**/*.test.tsx` |

## D. Ops / deployment gaps

| # | Sev | Item | Evidence |
|---|---|---|---|
| G23 | **P0** | **No prod deploy pipeline** — only `cd-staging-*`; all workflows hardcode `rg-vrbook-staging`; prod would be fully manual | `.github/workflows/` |
| G24 | **P0** | **No tested rollback** — single-revision 100%-to-latest; blue/green `revision_suffix`/`traffic_weight` inputs defined but unused | `_deploy-container-app.yml` |
| G25 | **P1** | Backup/restore **not tested** (PG Flex has backups; restore never exercised) | — |
| G26 | **P1** | Sandbox-only integrations: Stripe TEST, ACS managed domain (no custom DKIM), Entra placeholders | CONFIG-INVENTORY §4 |
| G27 | P1 | Quality gates informational-only: .NET integration tests, Pact provider verify, Trivy, nightly authed E2E, k6, ZAP | CI workflows |
| G28 | P2 | Outbound iCal rate limiter is **in-memory per-replica** (not distributed) | `InMemoryHostRateLimiter` |
| G29 | P2 | Redis not deployed — holds + distributed lock on Postgres/no-op | `deployRedis=false` |
| G30 | P2 | Optimistic concurrency globally disabled (Phase-1 single-actor) | `BaseDbContext.cs:48-58` |
| G31 | P2 | Pact provider verifier skipped (WAF/Kestrel adapter pending); 1 of 12 consumer interactions lands (OPS.1.9) | `PactVerifierTests.cs:43` |

## E. Compliance / NFR gaps (to confirm in PRD)

| # | Sev | Item |
|---|---|---|
| G32 | P1 | No cookie-consent / GDPR-CCPA surfaces, terms/privacy/cancellation-policy pages, or data-retention policy found — need PRD decisions |
| G33 | P1 | No WCAG 2.2 AA audit; a11y is baseline-only (roles/aria present, no focus-trap in modal) |
| G34 | P1 | No i18n / multi-currency display strategy beyond Stripe currency; SEO present on public pages but no sitemap.xml/robots.txt confirmed |
| G35 | P1 | Analytics / conversion tracking not present — must be live before launch or launch data is lost |
| G36 | P2 | No documented availability target / RTO / RPO / on-call model |

---

These map into Phase-3 stories. P0/P1 items are launch-relevant; the go-live-specific ones (G23–G27) are tracked in `docs/OPS_LAUNCH_COMPLETION_PLAN.md`.
