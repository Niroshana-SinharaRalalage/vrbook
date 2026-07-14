# VrBook — Per-Environment Configuration Matrix

**One table, every key.** Every configuration key, secret, feature flag, and hard-coded value from [`docs/ops/CONFIG-INVENTORY.md`](CONFIG-INVENTORY.md), with its value/source per environment and its delivery mechanism. This is the source-of-truth companion to the Configuration & Settings epic ([`docs/stories/EPIC-configuration-settings.md`](../stories/EPIC-configuration-settings.md)) — story **VRB-202** owns keeping it accurate and adding a CI drift check.

**Delivery-type legend:**
- `KV` — Key Vault secret bound as a Container App `secretRef` (`infra/main.bicep`). Value below is the KV secret name.
- `Bicep` — plain Container App env var literal in `infra/main.bicep` (`Section__Key`).
- `appsettings` — value in `src/VrBook.Api/appsettings.json` (base) or `appsettings.Development.json` (dev override).
- `NEXT_PUBLIC` — Next.js build/runtime env var (Dockerfile build-arg + Bicep `webEnvVars`).
- `hard-coded` — literal in source; **⚠️ flagged if it should move to config**.

**Move-to-config flag:** ✅ = correctly externalised · ⚠️ = should be moved to config / fixed (has a story) · 🔶 = intentionally hard-coded, acceptable.

> Convention: .NET reads `Section:Key`; the container injects `Section__Key` (double underscore). Staging/prod is delivered **100% via Container App env vars** — there is no `appsettings.Staging.json`/`appsettings.Production.json`.

| # | Key / secret | Delivery | dev | staging | prod | Move? · story |
|---|---|---|---|---|---|---|
| **Connection strings & platform infra** |
| 1 | `ConnectionStrings:Postgres` | KV `postgres-cs` (dev: appsettings) | `Host=localhost;…;Username=vrbook;Password=vrbook` **committed creds** | `postgres-cs` secretRef | `postgres-cs` secretRef | ⚠️ dev creds committed — VRB-201 |
| 2 | `postgres-admin-password` | KV (auto-gen) | n/a | auto-gen 32-char | auto-gen | ✅ VRB-201 |
| 3 | `ConnectionStrings:Redis` | KV `redis-cs` (dev: appsettings) | `localhost:6379` | `redis-cs` (Redis **off**) | `redis-cs` (off) | ✅ (G29 deploy deferred) |
| 4 | `SignalR:ConnectionString` | KV `signalr-cs` | empty (Null notifier) | `signalr-cs` | `signalr-cs` | ✅ |
| 5 | `ApplicationInsights:ConnectionString` | KV `appi-cs` | empty (skipped) | `appi-cs` | `appi-cs` | ✅ |
| 6 | `Blob:AccountUrl` | Bicep | empty | MI endpoint injected | MI endpoint | ✅ |
| 7 | `Blob:PropertyImagesContainer` | appsettings/Bicep | `property-images` | `property-images` | `property-images` | ✅ |
| 8 | `Blob:MessageAttachmentsContainer` | appsettings/Bicep | `message-attachments` | `message-attachments` | `message-attachments` | ✅ |
| 9 | `Blob:FeedCacheContainer` | appsettings | `feed-cache` | `feed-cache` | `feed-cache` | ✅ |
| 10 | `ServiceBus:Connection` / `ServiceBus__FullyQualifiedNamespace` | Bicep | empty | namespace FQDN (relay not coded) | FQDN | 🔶 relay deferred (G9) |
| 11 | `Feed:OutboundTokenPepper` | KV `feed-pepper` | empty | auto-gen 48-char | auto-gen | ✅ VRB-201 |
| 12 | `AZURE_CLIENT_ID` | Bicep | n/a | managed-identity client id | MI client id | ✅ |
| 13 | `ASPNETCORE_ENVIRONMENT` | Bicep | `Development` | `Staging` | `Production` | ✅ |
| 14 | `ASPNETCORE_URLS` | Bicep | `http://localhost:5xxx` | `http://+:8080` | `http://+:8080` | ✅ |
| **Auth / Entra (API)** |
| 15 | `EntraExternalId:Instance` | KV `entra-instance` | empty → **no JWT validation** | `entra-instance` | `entra-instance` (prod tenant) | ⚠️ fail-fast — VRB-200 |
| 16 | `EntraExternalId:TenantId` | KV `entra-tenant-id` | empty | `entra-tenant-id` | prod tenant GUID | ⚠️ VRB-200 |
| 17 | `EntraExternalId:ClientId` | KV `entra-api-client-id` | empty | `entra-api-client-id` | prod app-reg id | ⚠️ VRB-200 |
| 18 | `EntraExternalId:TenantIssuerHost` | KV `entra-tenant-issuer-host` | empty | `entra-tenant-issuer-host` | prod issuer host | ✅ |
| 19 | `EntraExternalId:AdminFlowName` | **not provided anywhere** | — | — | — | ⚠️ read at `UserProvisioningMiddleware.cs:143`, never set (G7) — VRB-209 |
| **Auth / Entra (web)** |
| 20 | `NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN` | NEXT_PUBLIC ← KV `entra-web-authority-admin` | `login.microsoftonline.com/common` fallback | secretRef | prod admin authority | ✅ |
| 21 | `NEXT_PUBLIC_ENTRA_AUTHORITY_GUEST` | NEXT_PUBLIC ← KV `entra-web-authority-guest` | fallback | secretRef | prod guest authority | ✅ |
| 22 | `NEXT_PUBLIC_ENTRA_CLIENT_ID` | NEXT_PUBLIC ← KV `entra-web-client-id` | fallback | secretRef | prod SPA app-reg id | ✅ |
| **Stripe** |
| 23 | `Stripe:SecretKey` | KV `stripe-secret` | empty (payment-disabled) | test key (prompted) | **live key (go-live)** | ✅ VRB-204 |
| 24 | `Stripe:WebhookSecret` | KV `stripe-webhook-secret` | empty | test webhook secret | live webhook secret | ✅ VRB-204 |
| 25 | `Stripe:PublishableKey` | KV `stripe-publishable-key` | empty | **NOT seeded ⚠️** | live pub key | ⚠️ G6 first-deploy risk — VRB-201 |
| 26 | `Refund:ServiceFeePercent` | Bicep `Refund__ServiceFeePercent` | default | staging value | prod value | ✅ |
| 27 | `Payment:AllowPlatformFallback` | **not in any config** (default false) | false | false | false | ⚠️ referenced, never set — VRB-209 |
| **ACS Email** |
| 28 | `Acs:ConnectionString` | KV `acs-connection-string` (written by `acs.bicep`) | empty | Bicep-written | Bicep-written | ⚠️ not seeded by `10-store-secrets.ps1` (G6) — VRB-201 |
| 29 | `Acs:SenderAddress` | KV `acs-sender-address` (default `donotreply@vrbook.example.com`) | placeholder | **NOT seeded ⚠️** managed domain | **custom domain + DKIM (go-live)** | ⚠️ G6 + placeholder — VRB-201/VRB-204 |
| **Frontend / web** |
| 30 | `NEXT_PUBLIC_API_BASE_URL` | NEXT_PUBLIC (Dockerfile build-arg + Bicep) | `http://localhost:5xxx` | **hard-coded FQDN `cd-staging-web.yml:149`** | prod API FQDN | ⚠️ hard-coded staging FQDN (G8) — VRB-205 |
| 31 | `NEXT_PUBLIC_SITE_URL` | NEXT_PUBLIC (**never set**) | `www.vrbook.example.com` fallback | fallback | fallback | ⚠️ var never wired; placeholder domain — VRB-219 |
| 32 | `NODE_ENV` | NEXT_PUBLIC/Bicep | `development` | `production` | `production` | ✅ |
| 33 | `PORT` / `HOSTNAME` | Bicep | 3000 / localhost | 3000 / `0.0.0.0` | 3000 / `0.0.0.0` | ✅ |
| 34 | `NEXT_TELEMETRY_DISABLED` | Bicep | 1 | 1 | 1 | ✅ |
| 35 | `NEXT_PUBLIC_MAPBOX_TOKEN` | env.d.ts only (**unread**) | — | — | — | 🔶 declared, not consumed |
| 36 | `NEXT_PUBLIC_SIGNALR_NEGOTIATE_URL` | env.d.ts only (**unread**) | — | — | — | 🔶 declared, not consumed |
| 37 | `NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY` | env.d.ts only (**unread**) | — | — | — | ⚠️ needed when Stripe Elements ships — VRB-217 |
| **CORS / Swagger / hosting** |
| 38 | `Cors:AllowedOrigins` | Bicep `Cors__AllowedOrigins__{0,1}` (dev: appsettings) | `[http://localhost:3000]` | staging web origin | prod web origin(s) | ✅ VRB-205 |
| 39 | `Swagger:EnableInProduction` | Bicep (dev: appsettings) | `true` | `false` | `false` | ✅ |
| 40 | `App:WebBaseUrl` | Bicep (dev-only plain) | `http://localhost:3000` | web FQDN | web FQDN | ✅ |
| 41 | `Frontend:BaseUrl` | appsettings (default) | `http://localhost:3000` placeholder | (env-injected) | (env-injected) | ⚠️ placeholder — VRB-219 |
| 42 | `AllowedHosts` | appsettings | `*` | `*` | `*` | 🔶 (behind WAF prod) |
| **Booking / Sync / Loyalty config-defects** |
| 43 | `Booking:TentativeSlaHours` | appsettings + Bicep (**DEAD**; hard-coded 24h `Booking.cs:119`; locked value **48h**) | 24h effective | 24h effective | 24h effective | ⚠️ **G2** — VRB-207 |
| 44 | `Booking:HoldDurationMinutes` | appsettings + Bicep (**no consumer**) | 15 (dead) | 15 (dead) | 15 (dead) | ⚠️ **G3** — VRB-208 |
| 45 | `Sync:DefaultPollIntervalMinutes` | appsettings (Bicep sends `Sync__DefaultPollIntervalMin` — **name mismatch**, no consumer) | 30 (dead) | 30 (dead) | 30 (dead) | ⚠️ **G3/G4** — VRB-208 |
| 46 | `Sync:StaleAlertHours` | appsettings + Bicep (**no consumer**) | 2 (dead) | 2 (dead) | 2 (dead) | ⚠️ **G3** — VRB-208 |
| 47 | `Loyalty:BronzeThreshold` | appsettings + Bicep `main.bicep:381-383` (**DEAD**; hard-coded `LoyaltyAccount.cs:65-66`) | 1 (dead) | 1 (dead) | 1 (dead) | ⚠️ **G1** — VRB-206 |
| 48 | `Loyalty:SilverThreshold` | appsettings + Bicep (**DEAD** — const `3`) | 3 (dead) | 3 (dead) | 3 (dead) | ⚠️ **G1** — VRB-206 |
| 49 | `Loyalty:GoldThreshold` | appsettings + Bicep (**DEAD** — const `6`) | 6 (dead) | 6 (dead) | 6 (dead) | ⚠️ **G1** — VRB-206 |
| 50 | `Loyalty:Enabled` | untyped read (default true, **not in any config**) | true | true | true | ⚠️ real flag, not surfaced — VRB-203/VRB-206 |
| **Feature flags** |
| 51 | `Features:UseRedisHoldStore` | untyped read (default false, **not injected anywhere**) | false | false | false | ⚠️ real toggle, no consumer — VRB-203 |
| 52 | `IFeatureToggle` runtime | `StubFeatureToggle` (**always default no-op**) | stub | stub | stub | ⚠️ **G13** — VRB-203 |
| 53 | `GET/PUT /admin/toggles` | `TogglesController` / `AdminController` → **501** | 501 | 501 | 501 | ⚠️ **G13** — VRB-203 |
| **iCal rate-limit policy** |
| 54 | `ChannelPoll` host suffixes / token / window / burst | `ChannelPollOptions.cs:23-30` **hard-coded** (bound, no appsettings) | hard-coded | hard-coded | hard-coded | ⚠️ move to config — VRB-214 |
| **Bootstrap / seed** |
| 55 | `Bootstrap:SeedPlatformAdmins` | Bicep array (Migrator) | `[]` | `niroshanaks@gmail.com` | `[]` (add before deploy) | ✅ |
| 56 | `Bootstrap:E2e:Enabled` | Bicep bool (Migrator) | false | **true** | **false** | ✅ |
| 57 | `e2e-{guest,owner,platform-admin}-password` | KV placeholders | n/a | operator-set | n/a | ✅ VRB-201 |
| **Environment sizing ladder (`infra/main.bicep`)** |
| 58 | Postgres SKU | Bicep | B2s Burstable | B1ms Burstable | D4ds_v5 GP HA | ✅ |
| 59 | API replicas min/max | Bicep | 0/3 | 0/3 | 1/10 | ✅ |
| 60 | Front Door + WAF | Bicep | off | off | **on** | ✅ VRB-205 |
| 61 | Postgres firewall IPs | **hard-coded** `main.bicep` (`174.104.204.213`, `135.18.171.52`) | n/a | literals | literals | ⚠️ move to param — VRB-205 |
| **Third-party (go-live)** |
| 62 | Stripe mode | KV | test | test | **live (go-live)** | ✅ VRB-204 |
| 63 | ACS sender domain / DKIM/SPF/DMARC | `acs.bicep` + KV | managed `*.azurecomm.net` | managed | **custom domain + DKIM (go-live)** | ✅ VRB-204 |
| 64 | Entra tenant/app-reg | KV | placeholders | operator-set | **prod tenant cutover** | ✅ VRB-204 |
| 65 | Analytics / conversion tracking | **absent** (G35) | — | — | must be live pre-launch | ⚠️ new — VRB-204 |
| 66 | Error tracking (App Insights already; client-side SDK) | partial | dev | staging | prod | ⚠️ client-side — VRB-204 |
| **Orphan / legacy secrets (seeded, unused)** |
| 67 | `sendgrid-key` | KV (prompted, **orphan**) | — | seeded, unused | — | ⚠️ remove — VRB-201 |
| 68 | `b2c-api-client-secret` | KV (prompted, **orphan**) | — | seeded, unused | — | ⚠️ remove (post-B2C) — VRB-201 |
| **Stale example files** |
| 69 | `.env.example` (repo root) | file | references retired `NEXT_PUBLIC_ENTRA_AUTHORITY` | — | — | ⚠️ stale — VRB-201 |
| 70 | `web/.env.local.example` | file (canonical) | up to date | — | — | ✅ |

**Coverage:** 70 rows spanning all 8 sections of CONFIG-INVENTORY (appsettings, strongly-typed options, Bicep env vars, KV secrets, third-party matrix, frontend config, feature flags, hard-coded-defect list, and the per-environment sizing ladder). **22 rows flagged ⚠️** as should-move-to-config / fix — each carries the owning story ID. The four config-defect gap stories (G1→VRB-206, G2→VRB-207, G3/G4→VRB-208, G7→VRB-209) map directly onto rows 43–50 and 19/27.
