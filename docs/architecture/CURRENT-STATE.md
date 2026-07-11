# VrBook — Current-State Architecture (as-built)

**Purpose:** the single "understand the whole project in one read" document. Everything here is grounded in the code with file citations. Paired with [`docs/ops/CONFIG-INVENTORY.md`](../ops/CONFIG-INVENTORY.md) (every setting/secret) and [`docs/ops/CURRENT-GAPS.md`](../ops/CURRENT-GAPS.md) (the defect/gap register). Last regenerated: 2026-07-11 via a 6-lane parallel code audit.

> **Status legend:** ✅ Implemented · ◑ Partial · ⛔ Stubbed (returns 501 / no-op) · ✋ Not started.

---

## 1. What VrBook is

A **commission-free, direct-booking vacation-rental platform** — property owners and small hotel operators list rentals and take bookings + payments directly from guests, escaping the 15–20% OTA (Airbnb/Booking.com) commission. It is a **multi-tenant SaaS**: each tenant is an independent business with its own properties, Stripe Connect account, iCal calendar feeds, branding, and isolated data. Three primary surfaces: **guest** (browse/book/pay/review/message), **tenant admin / property owner** (listings, calendar, pricing, booking decisions, reports), **platform admin** (operate all tenants).

Product state: **Phase 1 (core booking product) + Phase 1.5 (multi-tenant SaaS) are functionally complete on staging.** Remaining: launch-hardening (mostly done, see `docs/OPS_LAUNCH_COMPLETION_PLAN.md`), then Phase 3 (hotel-style rooms + multi-unit cart) and Phase 4 (OTA package bundling) — neither designed yet.

---

## 2. Stack

| Layer | Technology |
|---|---|
| Backend | .NET 8, C#, **modular monolith**, MediatR (CQRS), EF Core 8, FluentValidation, Serilog |
| Database | PostgreSQL 16 — **one schema per module**, Row-Level Security for tenant isolation |
| Frontend | Next.js 14 (App Router), React 18, TypeScript 5.5, Tailwind 3.4, MSAL Browser 3.x, TanStack Query 5, Recharts, `@dnd-kit`, `@stripe/react-stripe-js` |
| Auth | Microsoft Entra External ID (CIAM) + MSAL + JwtBearer; DB-authoritative roles |
| Payments | Stripe Connect Express (destination charges, manual capture) |
| Email | Azure Communication Services (ACS) Email |
| Realtime | Azure SignalR (Serverless) |
| Infra | Azure Container Apps (+ Jobs), ACR, Key Vault, Postgres Flexible Server, ACS, Log Analytics + App Insights, Blob Storage, Service Bus (provisioned, relay not coded), Redis (not deployed) — all via **Bicep** |
| CI/CD | GitHub Actions → Azure Container Apps in `rg-vrbook-staging` |

---

## 3. Solution layout (`src/VrBook.sln`)

**Shared kernel + host** (`src/`): `VrBook.Domain` (aggregate/entity/VO base + `AggregateRoot`), `VrBook.Contracts` (DTOs, enums, events, cross-module interfaces — the ONLY sanctioned inter-module coupling), `VrBook.Application` (shared MediatR behaviors + DI), `VrBook.Infrastructure` (outbox, persistence base, RLS, Redis, realtime, stubs), `VrBook.Api` (composition root / host), `VrBook.Migrator` (one-shot migration + seed console).

**12 modules** (`src/Modules/VrBook.Modules.*`): Identity, Catalog, Pricing, Booking, Payment, Sync, Messaging, Reviews, Loyalty, Notifications, **Admin (⛔ stub)**, **Reports (read-only, no schema)**.

**3 workers** (`src/Workers/`): Sync, Booking, Notifications — each a single `Program.cs` run as a Container App **Job** on cron.

**Tests** (`tests/`): `VrBook.Architecture.Tests`, `VrBook.Api.IntegrationTests` (holds unit + integration + cross-tenant), `VrBook.Api.PactTests`.

**Modular-monolith shape:** one deployable host composes all modules in-process; each module owns its DB schema + `DbContext` + migrations; inter-module coupling only via `VrBook.Contracts` (`src/VrBook.Application/Common/IModuleRegistration.cs`).

---

## 4. Architecture layers & enforcement

**Clean Architecture**, enforced by NetArchTest (`tests/VrBook.Architecture.Tests/CleanArchitectureRulesTests.cs`): Domain has no outward deps; Contracts is pure; Application → Domain+Contracts only (no Infra/EF/Npgsql); Infrastructure never references Api; **all MediatR handlers are `internal sealed`**. Each module physically layers into `Domain/`, `Application/`, `Infrastructure/` sub-folders.

**CQRS/MediatR** — commands/queries are MediatR requests; handlers under `Application/Commands|Queries`. Registered per-assembly via `AddModuleAssembly` (`src/VrBook.Application/Common/DependencyInjection.cs`).

**Shared pipeline behaviors** (order matters, `DependencyInjection.cs:18-25`): `UnhandledException → Logging → Validation(FluentValidation) → Performance`.

**Identity-module behaviors** (load-bearing order, `IdentityModule.cs:54-68`): `AuditLogBehavior` (OUTER — writes an `AuditLogEntry` for any `IAuditable`, incl. `.failed` rows on rejection) wraps `TenantAuthorizationBehavior` (INNER — gates every `ITenantScoped` write by `currentUser.TenantId == request.TenantId`, with `IsPlatformAdmin` + `IBackgroundCommand` + `BackgroundTenantScope` bypasses; throws `CrossTenantAccessException`).

~148 architecture facts across 35 files pin these invariants + tenant-id shapes + role gates + no-dev-auth-in-prod + RLS-bypass allowlist.

---

## 5. The 12 modules

| Module | Responsibility | Key aggregates | State |
|---|---|---|---|
| **Identity** | Tenants, users, memberships, auth provisioning, audit log, tenant Stripe-readiness | `Tenant`, `User`, `TenantMembership`, `UserIdentity`, `AuditLogEntry` | ✅ |
| **Catalog** | Listings, images, amenities, house rules, activation gating | `Property` (root), `Amenity` | ✅ (image upload endpoints ⛔ 501) |
| **Pricing** | Plans, fees, rules, quote engine | `PricingPlan` (root), `PricingRule` | ✅ |
| **Booking** | Booking lifecycle state machine, holds, availability blocks, completion sweep | `Booking` (root), `BookingHold`, `AvailabilityBlock` | ✅ (admin queue/manual ⛔ 501) |
| **Payment** | Stripe PaymentIntent mirror, captures, refunds, webhook idempotency | `PaymentIntent` (root), `Refund`, `WebhookEvent` | ✅ (dispute → log-only) |
| **Sync** | iCal channel-feed ingest, sync runs, conflict detection | `SyncRun` (root), `ChannelFeed`, `SyncConflict`, `ExternalReservation` | ✅ |
| **Messaging** | Guest↔owner thread per booking | `MessageThread` (root), `Message` | ✅ (attachments ⛔ 501) |
| **Reviews** | One review per booking, owner reply, moderation | `Review` (root) | ✅ |
| **Loyalty** | Per-guest loyalty accounts + tier promotion | `LoyaltyAccount` (root) | ✅ |
| **Notifications** | Outbound notification log + email dispatch | `NotificationLog` (root) | ✅ |
| **Admin** | Platform-admin bounded context | — | ⛔ **no-op stub** (`AdminModule.cs:17-25`) |
| **Reports** | Read-only analytics (ADR/occupancy/revenue/source) | none (reads other schemas) | ✅ |

---

## 6. Domain model highlights

Base: `AggregateRoot` (`src/VrBook.Domain/Common/AggregateRoot.cs`) — `Id`, `RowVersion` (optimistic token **globally disabled** at mapping, `BaseDbContext.cs:48-58`), soft-delete + audit columns, domain-event buffer (`Raise`/`DequeueEvents`).

- **Tenant** — status machine `PendingOnboarding → Active → Suspended → Closed`; `PlatformFeeBps ∈ [0,10000]`; auto-activates/suspends on Stripe capability changes.
- **User** — `Email` VO, `IsPlatformAdmin` (global), `PreSeededAt` (operator pre-seed). Legacy `IsOwner/IsAdmin` flags **removed** (OPS.M.21). One user → many `UserIdentity` (provider↔oid).
- **TenantMembership** — `(UserId, TenantId, Role∈{tenant_admin,tenant_member}, IsPrimary)`. `tenant_admin` = "Property Owner".
- **Property** — owns Images/HouseRules/AmenityIds; **gated `Activate(tenantStatus, chargesEnabled, payoutsEnabled)`** refuses to publish unless the tenant is Stripe-ready; deprecated no-arg `Activate()` is an `[Obsolete]` migration bridge.
- **Booking** — state machine `Tentative → Confirmed → CheckedIn → CheckedOut → Completed` (+ `Rejected`/`Cancelled`); `Place` sets a **24h `TentativeUntil`**; `CheckOut` snapshots `CompletionDueAt` (turnover-aware, OPS.M.16). `BookingHold` mirrors the authoritative Postgres/Redis hold.
- **PricingPlan** — one per property; `BaseNightlyRate`, `WeekendRate`, min/max stay, fees, rules; `PricingRule` kinds validated per-kind.
- **PaymentIntent** — local mirror keyed by `BookingId`; raises `PaymentCaptured`/`RefundIssued`/`PaymentFailed`.
- **Multi-tenancy:** every tenant-owned aggregate carries a denormalized `TenantId` (NOT NULL since the OPS.M.3c wave). Guests are deliberately tenant-less; Loyalty + Notifications are guest-scoped (no/nullable `tenant_id`).

---

## 7. API surface (`src/VrBook.Api`)

All routes are literal `api/v1/*` (no API-versioning package). Cross-cutting: Swagger (`/swagger`, root redirect), RFC-7807 ProblemDetails (Hellang), CORS from config, JwtBearer (Entra), `UserProvisioningMiddleware` (on-login provisioning), outbox, health (`/health/live`, `/health/ready`), Serilog + PII redaction. (`Program.cs`)

- **Public / anonymous:** `GET /properties` (search), `GET /properties/{slug}`, `GET /properties/{id}/availability`, `GET /amenities`, `POST /properties/{id}/quotes`, `GET /properties/{id}/reviews`, `GET /feeds/{token}.ics`, `POST /payments/webhooks/stripe` (signature-verified), health.
- **Guest-authed (`[Authorize]`):** `bookings` (holds, place, get, my-bookings, cancel/confirm/reject/check-in/check-out/complete, review — owner-action verbs resolve tenant from the booking via `IGuestTenantResolver`), `me` (get/update/deactivate, tenant onboarding progress, tenants list), `me/loyalty`, `payments` (intents, refunds), `properties/{id}/pricing` (+ rules CRUD/reorder), property create/edit (+ image endpoints ⛔ 501), calendar, blocks, `threads` (+ messages; attachments ⛔ 501), `realtime/negotiate`, review responses.
- **Tenant-admin (`api/v1/admin/*`, `[Authorize]`, tenant scoping via pipeline):** admin properties/bookings (read), `admin/bookings/queue|manual` (⛔ 501), guest search, toggles/alerts (⛔ 501 stubs), tenant Stripe onboarding, notifications retry, channel-feeds CRUD, sync-conflicts, reviews moderation, reports (occupancy/revenue/adr/source).
- **Platform-admin (`[Authorize(Roles="PlatformAdmin")]`):** amenities CRUD, `admin/platform/tenants` (list/detail/suspend/reactivate/platform-fee/memberships — **the only place a URL tenant id flows into a command, safe because role-gated**), `admin/platform/users/seed` (admin pre-seed).

---

## 8. Data model & persistence

**Convention** (`src/VrBook.Infrastructure/Persistence/BaseDbContext.cs`): each module context sets its own schema, applies `OutboxMessageConfiguration` (so **each schema owns its own `outbox_messages` table**), auto-populates audit columns, and applies a soft-delete global filter (`DeletedAt == null`). Optimistic concurrency **globally disabled** (Phase-1 single-actor MVP).

| Context | Schema | Tables |
|---|---|---|
| Identity | `identity` | users, audit_log, tenants, tenant_memberships, user_identities, migration_audit |
| Catalog | `catalog` | properties, property_images, house_rules, amenities |
| Pricing | `pricing` | pricing_plans, fees, pricing_rules |
| Booking | `booking` | bookings, line items, guest entries, booking_holds, availability_blocks |
| Payment | `payment` | payment_intents, refunds, webhook_events |
| Sync | `sync` | channel_feeds, external_reservations, sync_conflicts, sync_runs |
| Messaging | `messaging` | threads, messages |
| Reviews | `reviews` | reviews |
| Loyalty | `loyalty` | accounts (keyed on user_id; **no tenant_id** — guest-scoped) |
| Notifications | `notifications` | notification_log (`tenant_id` **nullable forever** — guest notifications) |

**Cross-schema FK:** every `tenant_id` references `identity.tenants("Id")` via **raw-SQL FK** in the OPS.M.3a migrations (EF can't model cross-context FKs). `identity` is the canonical tenant registry.

**Migrations** (forward-only; `src/VrBook.Migrator`): ~75 total — Identity 20, Catalog 10, Booking 9, Payment 7, Pricing 7, Reviews 6, Sync 6, Messaging 5, Notifications 4, Loyalty 1. Naming: `yyyyMMddHHmmss_<SliceCode>Name`. Multi-step column adds split into add→backfill→NOT-NULL. Migrator runs `MigrateAsync` per context, then two idempotent backfills: `SeedPlatformAdminsBackfill` (Bicep-declared platform admins) + `SeedE2EBackfill` (staging-only E2E fixture: tenant + personas + smoke property + 2 Tentative bookings).

**Row-Level Security** (`src/VrBook.Infrastructure/Persistence/`): `TenantGucCommandInterceptor` runs `set_config('app.tenant_id', …, true)` + `app.is_platform_admin` (transaction-scoped) before every command. Resolution: `ICurrentUser.TenantId` → `BackgroundTenantScope` → empty (**fail-safe deny**). Per-module `OpsM9_*_RlsPolicies` migrations create `tenant_id = app.tenant_id OR app.is_platform_admin` policies (+ a public-read carve-out for active listings). Platform-admin/worker bypass via `IRlsBypassDbContextFactory` + `RlsBypassScope` (AsyncLocal, audited, call-sites allowlisted by arch test).

---

## 9. Background processing & eventing

**Workers** (Container App Jobs, cron, SIGTERM-honoring):

| Worker | Cron | Job |
|---|---|---|
| Sync (`src/Workers/VrBook.Workers.Sync`) | `*/5 * * * *` | Poll due iCal channel feeds per tenant (RLS-bypass list, then per-feed `BackgroundTenantScope`) |
| Booking (`src/Workers/VrBook.Workers.Booking`) | expiry `*/10 * * * *`; completion `0 6 * * *` | `--mode=expiry` (Tentative SLA sweep) / `--mode=completion` (CheckedOut→Completed, ripples to Loyalty+Notifications) |
| Notifications (`src/Workers/VrBook.Workers.Notifications`) | `*/2 * * * *` | Drain queued notification_log rows → send via ACS |
| Migrator | pre-deploy | Migrations + 2 backfills |

**Eventing — transactional outbox** (`src/VrBook.Infrastructure/Outbox/`): `DomainEventOutboxInterceptor` (SaveChanges interceptor) writes one `OutboxMessage` per domain event **into the same transaction**, then post-commit publishes **in-process synchronously** via MediatR `INotificationHandler`s. Registered **scoped** so handlers share the caller's DbContext. ⚠️ **The cross-process outbox→Service Bus relay is provisioned in infra but NOT coded** — outbox rows accumulate with `DispatchedAt` never set; the only consumer today is in-process MediatR. This is the main incomplete piece of the async architecture.

---

## 10. Integrations

| Integration | Wiring | State / go-live need |
|---|---|---|
| **Stripe Connect Express** | `src/Modules/VrBook.Modules.Payment/Infrastructure/Stripe/StripeGateway.cs` — manual-capture PaymentIntents, destination charges (`TransferData`, `ApplicationFeeAmount`, `OnBehalfOf`), Express onboarding + account links, signature-verified webhook w/ idempotency. `IsConfigured=false` → booking runs "payment-disabled" | **TEST mode.** Go-live: live keys + live webhook + real Connect onboarding. `charge.dispute.created` only logs (no dispute domain event). |
| **ACS Email** | `infra/modules/acs.bicep` + `AzureEmailSender.cs`; driven by the notifications worker | Working on **Azure-managed `*.azurecomm.net`** domain. Go-live: **custom sender domain + DKIM/SPF DNS** (OPS.8). |
| **iCal sync** | `src/Modules/VrBook.Modules.Sync` + Sync worker; per-host token-bucket rate limiting (`OutboundRateLimitHandler`, **in-memory per-replica**) | Functional against real channels. |
| **Entra External ID** | JwtBearer (`AuthExtensions.cs`) + MSAL (`web/src/lib/auth/`); per-flow authority split (admin Entra-local vs guest +socials) | Staging **Entra-only** (DevAuth retired). Go-live: prod tenant/app-reg/user-flow cutover; `entra-*` KV secrets are `pending-identity-setup` placeholders. |
| **SignalR** | `SignalRRealtimeNotifier` (else `NullRealtimeNotifier`) | Free F1 staging / Standard S1 prod. |
| **Redis** | Conditional (`deployRedis=false`) — **not deployed**; holds run on `PostgresHoldStore` | Enable via `deployRedis=true` + `Features:UseRedisHoldStore=true` when traffic warrants. |
| **Blob storage** | `stvrbook{env}`, containers property-images/message-attachments/feed-cache; MI access | Provisioned + wired. No CDN in front of images. |

---

## 11. Frontend (`web/`)

Next.js 14 App Router. **Routes** grouped: public/SEO-critical SSR (`/`, `/properties`, `/properties/[slug]` with JSON-LD), guest account (mostly client: `/account/*`, `/bookings/[id]`), admin (all client, wrapped in `AdminAuthGuard`+sidebar), platform-admin (`/admin/platform/*`), auth (`/auth/*`), `/select-tenant`.

**Auth** — MSAL Browser 3.x with the critical `msalReady` init gate (`Providers.tsx`); per-flow authority (admin vs guest); `cacheLocation:'sessionStorage'`; token provider waits for account + self-heals the cold-load 401 race; `X-Active-Tenant` header from per-tab sessionStorage; `AdminAuthGuard` produces `social-admin-rejected`/`admin-not-provisioned` statuses. This auth wiring is **genuinely production-grade**.

**Data** — hand-written typed `apiFetch` (`web/src/lib/api/client.ts`) + per-domain modules; `useAuthedQuery` (defers until MSAL ready, `SignInGate` for unauth). ⚠️ The OpenAPI-generated client (`gen:api` → `web/src/lib/api/generated/`) is **empty/not committed** — all access is hand-maintained.

**Design system** — Tailwind + shadcn token conventions (`components.json`, CSS-var semantic tokens + brand-orange/brand-maroon ramps, dark mode via next-themes, Inter, lucide). ⚠️ But **almost no shared UI component library exists** — only `ui/ConfirmActionModal.tsx`; buttons/cards/badges are ad-hoc inline Tailwind copy-pasted per page. Two parallel color systems (semantic tokens vs raw `brand-*` classes).

**UI quality** — good loading/empty/error/skeleton states on public pages; reasonable a11y baseline (roles/aria/landmarks); responsive breakpoints throughout. ⚠️ Gaps: **home `/` "Featured stays" is hardcoded placeholder data**; **`/account/profile` is a stub**; **no mobile hamburger nav** (primary nav is `hidden md:flex` → disappears on mobile); unit tests skew to auth guards, thin on feature rendering.

---

## 12. Tests & CI quality gates

**Suites:** .NET — `VrBook.Api.IntegrationTests` (474 facts: unit domain/handlers + `Integration` + `CrossTenant`, Testcontainers Postgres), `VrBook.Architecture.Tests` (148 facts), `VrBook.Api.PactTests` (provider verifier, **skipped** — WAF/Kestrel adapter pending). Frontend — Vitest (~15 files, auth-heavy), Pact consumer (1 of 12 interactions lands), **Playwright E2E (31 scenarios: anon 5 blocking + guest 10 + owner 10 + platform-admin 6, incl. 1 fixme)**.

**Strong coverage:** domain invariants (heavy `[Fact]` density), cross-tenant isolation + RLS (the strongest safety net), architecture rules. **Thin:** frontend rendering, report handlers, Pact (nominal), authed E2E (needs operator personas). No coverage threshold enforced.

**Blocking CI gates:** dotnet format + Release build + unit tests, OpenAPI breaking-change (oasdiff) + Spectral, web lint/typecheck/`check:e2e-suite`(=31)/Vitest/Pact-drift/build, api+web curl smokes, **anonymous playwright-smoke**. **Informational (`continue-on-error`):** .NET integration tests, Pact provider verify, Trivy scans, nightly authed E2E, k6, ZAP.

---

## 13. Deployment & environments

**Bicep** (`infra/main.bicep` + `infra/modules/`) provisions: VNet, Key Vault (purge-protected), ACS Email, Log Analytics + App Insights, ACR, Postgres Flex, Redis (off), Service Bus (relay not coded), SignalR, Storage, managed identity, Container Apps env, 2 Container Apps (api + web) + 4 Jobs (migrator + 3 workers), Front Door+WAF (**prod only**).

**Environments:** dev / staging / prod declared; **only staging is live + pipelined.** Sizing ladders by env: staging = B1ms burstable Postgres + **scale-to-zero** Container Apps; prod = D4ds_v5 GP Postgres HA + min-1 replicas + Front Door. Staging Postgres is **public + IP-allowlisted** (`psql-vrbook-staging-v2`); prod is VNet-injected.

**CI/CD** (`.github/workflows/`): `cd-staging-api.yml` (backend tests→contracts→5 images→Bicep deploy→migrate→deploy workers/api→smoke), `cd-staging-web.yml` (frontend→image→deploy→smoke→playwright-smoke), `nightly-playwright.yml`, `perf-k6.yml` (dispatch), `security-zap.yml` (dispatch), `_deploy-container-app.yml` (reusable). Push to `develop` → path-filtered deploy. Images tagged by commit SHA. OPS.INFRA.3 warm-first convergence handles scale-to-zero; Bicep deploy has a `ServerIsBusy` retry.

**Deployment gaps:** ⚠️ **no prod deploy pipeline exists** (all workflows hardcode `rg-vrbook-staging`; prod would be fully manual). ⚠️ **no tested rollback** (single-revision 100%-to-latest; blue/green inputs defined but unused). Provisioning + secret seeding + Entra setup are manual PowerShell/portal.

---

## 14. Known stubs, gaps & defects (pointer)

The full register with severity + fix is in [`docs/ops/CURRENT-GAPS.md`](../ops/CURRENT-GAPS.md). Headlines:

- **⛔ Stubs:** Admin module (no-op), property image upload (501), booking admin queue/manual (501), message attachments (501), toggles/alerts (501), feature-flag runtime (no-op `StubFeatureToggle`), several infra stubs (tax calc, availability reader).
- **⚠️ Async:** outbox→Service Bus relay unimplemented (in-process only).
- **⚠️ Config defects:** loyalty tier thresholds + booking 24h SLA are **hard-coded in the domain while their config keys are dead**; `stripe-publishable-key` + `acs-sender-address` referenced by Bicep but **not seeded** (first-deploy risk); no fail-fast config validation; `Sync__DefaultPollIntervalMin` vs `Sync:DefaultPollIntervalMinutes` name mismatch; hard-coded staging API FQDN in the web build-arg.
- **⚠️ Frontend:** empty generated API client, no shared component library, placeholder home + profile, no mobile nav.
- **⚠️ Ops:** no prod pipeline, no tested rollback, Redis not deployed, sandbox-only Stripe/ACS/Entra, several quality gates informational-only, in-memory (non-distributed) rate limiter.
