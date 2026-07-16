# VrBook — Configuration Inventory (complete)

Every setting, secret, env var, feature flag, and hard-coded value across the repo, with file citations. Companion to [`docs/architecture/CURRENT-STATE.md`](../architecture/CURRENT-STATE.md). Anything a developer or operator must know to run, deploy, or go live must be here — "the developer will figure it out" is a defect. Last regenerated 2026-07-11. (Build-artifact `bin/**` copies excluded.)

> **Key convention:** .NET reads `Section:Key`; the container injects it as `Section__Key` (double underscore). Staging/prod config is delivered **100% via Container App env vars from `infra/main.bicep`** — there is **no `appsettings.Staging.json`/`appsettings.Production.json`**.

---

## 1. `appsettings.json` files (3 total; workers have none)

### `src/VrBook.Api/appsettings.json` (base)
Serilog (Console CompactJson, `Information`), `AllowedHosts=*`, and these **all-empty-string** keys (injected at runtime): `ConnectionStrings:Postgres`, `ConnectionStrings:Redis`, `ApplicationInsights:ConnectionString`, `EntraExternalId:{Instance,TenantId,ClientId,TenantIssuerHost}`, `Stripe:{SecretKey,WebhookSecret,PublishableKey}`, `Acs:ConnectionString`, `Blob:AccountUrl`, `ServiceBus:Connection`, `SignalR:ConnectionString`, `Feed:OutboundTokenPepper`. Non-empty defaults: `Acs:SenderAddress=donotreply@vrbook.example.com` (placeholder), `Blob:{PropertyImagesContainer=property-images, MessageAttachmentsContainer=message-attachments, FeedCacheContainer=feed-cache}`, `Sync:{DefaultPollIntervalMinutes=30, StaleAlertHours=2}`, `Booking:{TentativeSlaHours=24, HoldDurationMinutes=15}`, `Loyalty:{BronzeThreshold=1, SilverThreshold=3, GoldThreshold=6}`, `Frontend:BaseUrl=http://localhost:3000` (placeholder), `Cors:AllowedOrigins=[http://localhost:3000]`, `Swagger:EnableInProduction=false`.

### `src/VrBook.Api/appsettings.Development.json`
Overrides: Serilog `Debug`; `ConnectionStrings:Postgres = Host=localhost;…;Username=vrbook;Password=vrbook;Include Error Detail=true` (**committed local dev creds**); `ConnectionStrings:Redis=localhost:6379`; `Swagger:EnableInProduction=true`.

### `src/VrBook.Migrator/appsettings.json`
Serilog; `ConnectionStrings:Postgres=""`; `Bootstrap:SeedPlatformAdmins=[]` (Bicep-populated per deploy).

**Workers** (`src/Workers/*`) — no appsettings; configured entirely by env vars (`AddJsonFile(optional:true)+AddEnvironmentVariables()`).

---

## 2. Strongly-typed options + config reads

**`.Configure<T>()` bindings:** `StripeOptions` (§`Stripe`, `PaymentModule.cs:24`), `RefundOptions` (§`Refund`), `ChannelPollOptions` (§`ChannelPoll` — **bound but NO appsettings entry; relies on hard-coded C# defaults** in `ChannelPollOptions.cs:23-30`).

**Untyped reads** (`configuration[...]`/`GetValue`): `Cors:AllowedOrigins`, `ApplicationInsights:ConnectionString`, `Swagger:EnableInProduction`, `ConnectionStrings:Postgres` (all 10 DbContexts), `ConnectionStrings:Redis`, `SignalR:ConnectionString`, `Acs:{ConnectionString,SenderAddress}`, `App:WebBaseUrl` (notification handlers), `Blob:{AccountUrl,PropertyImagesContainer}`, `EntraExternalId:{Instance,TenantId,ClientId,TenantIssuerHost}`, `EntraExternalId:AdminFlowName` (**read by `UserProvisioningMiddleware.cs:143` but absent from ALL config** — admin-gate inert until set), `Loyalty:Enabled` (default true), `Payment:AllowPlatformFallback` (default false, **not in any config**), `Features:UseRedisHoldStore` (default false), `Bootstrap:SeedPlatformAdmins`, `Bootstrap:E2e:Enabled`.

**⚠️ No fail-fast validation.** No `.ValidateOnStart()`/`IValidateOptions<T>` anywhere. Missing config → **silent graceful-degradation**: JwtBearer only wired if all 3 `EntraExternalId` values present (else API boots with **no token validation**); Redis/SignalR/AppInsights silently skipped if empty; only `AzureEmailSender` throws (on first use, not startup).

---

## 3. Secrets & env vars (`infra/main.bicep`)

`apiEnvVars` is reused verbatim for API + all 4 jobs. **Plain values:** `ASPNETCORE_{ENVIRONMENT,URLS}`, `ServiceBus__FullyQualifiedNamespace`, `App__WebBaseUrl` (dev-only), `Cors__AllowedOrigins__{0,1}`, `Swagger__EnableInProduction`, `Blob__{AccountUrl,PropertyImagesContainer,MessageAttachmentsContainer}`, `Sync__DefaultPollIntervalMin` (⚠️ name ≠ appsettings `Sync:DefaultPollIntervalMinutes`), `Sync__StaleAlertHours`, `Booking__{TentativeSlaHours,HoldDurationMinutes}`, `Refund__ServiceFeePercent`, `Loyalty__{Bronze,Silver,Gold}Threshold`, `AZURE_CLIENT_ID`.

**`secretRef` → Key Vault (API):** `ConnectionStrings__Postgres`→`postgres-cs`, `ConnectionStrings__Redis`→`redis-cs`, `SignalR__ConnectionString`→`signalr-cs`, `Stripe__SecretKey`→`stripe-secret`, `Stripe__WebhookSecret`→`stripe-webhook-secret`, `Stripe__PublishableKey`→`stripe-publishable-key`, `Acs__ConnectionString`→`acs-connection-string`, `Acs__SenderAddress`→`acs-sender-address`, `EntraExternalId__{Instance,TenantId,ClientId,TenantIssuerHost}`→`entra-{instance,tenant-id,api-client-id,tenant-issuer-host}`, `Feed__OutboundTokenPepper`→`feed-pepper`, `ApplicationInsights__ConnectionString`→`appi-cs`.

**Web** (`webEnvVars`): plain `NODE_ENV`, `PORT=3000`, `HOSTNAME`, `NEXT_TELEMETRY_DISABLED`, `NEXT_PUBLIC_API_BASE_URL`; secretRef `NEXT_PUBLIC_ENTRA_AUTHORITY_{ADMIN,GUEST}`→`entra-web-authority-{admin,guest}`, `NEXT_PUBLIC_ENTRA_CLIENT_ID`→`entra-web-client-id`.

**Migrator extra:** `Bootstrap__SeedPlatformAdmins__N__{Email,DisplayName}` (from `seedPlatformAdmins` array — staging seeds `niroshanaks@gmail.com`), `Bootstrap__E2e__Enabled` (staging true).

### Secret seeding (`infra/scripts/10-store-secrets.ps1`)
- **Auto-generated:** `feed-pepper` (48-char random), `postgres-admin-password`.
- **`pending-bicep-deploy` placeholders:** `postgres-cs`, `redis-cs`, `signalr-cs`, `appi-cs`.
- **`pending-identity-setup` placeholders:** `entra-{instance,tenant-id,api-client-id,tenant-issuer-host}`, `entra-web-authority-{admin,guest}`, `entra-web-client-id`, `e2e-{guest,owner,platform-admin}-password`.
- **Prompted (optional):** `stripe-secret`, `stripe-webhook-secret`, `sendgrid-key` (legacy), `b2c-api-client-secret` (legacy).

**⚠️ Seeding gaps (referenced by Bicep, NOT seeded by the script):** `stripe-publishable-key`, `acs-connection-string` (written by `acs.bicep` module), `acs-sender-address` (**no producer**). First deploy could fail resolving these secretRefs. **Orphans (seeded but unused):** `sendgrid-key`, `b2c-api-client-secret`.

---

## 4. Third-party credentials matrix

| Integration | KV secret | App setting | Populated? |
|---|---|---|---|
| Stripe secret / webhook / publishable | `stripe-secret` / `stripe-webhook-secret` / `stripe-publishable-key` | `Stripe:{SecretKey,WebhookSecret,PublishableKey}` | Prompted (test) / Prompted / **NOT seeded ⚠️** |
| ACS | `acs-connection-string` / `acs-sender-address` | `Acs:{ConnectionString,SenderAddress}` | Bicep-written / **NOT seeded ⚠️** |
| Entra (API) | `entra-{instance,tenant-id,api-client-id,tenant-issuer-host}` | `EntraExternalId:*` | `pending-identity-setup` placeholders |
| Entra (web) | `entra-web-authority-{admin,guest}`, `entra-web-client-id` | `NEXT_PUBLIC_ENTRA_*` | placeholders |
| Social IdP (Google/MS/FB/Apple) | — (configured inside Entra user flow, not repo KV) | — | external; see `docs/runbooks/social_idp_setup.md` |
| SignalR | `signalr-cs` | `SignalR:ConnectionString` | `pending-bicep-deploy` |
| Redis | `redis-cs` | `ConnectionStrings:Redis` | placeholder (deploy off) |
| App Insights | `appi-cs` | `ApplicationInsights:ConnectionString` | `pending-bicep-deploy` |
| Postgres | `postgres-cs` (+`postgres-admin-password`) | `ConnectionStrings:Postgres` | placeholder + auto-gen |
| Feed pepper | `feed-pepper` | `Feed:OutboundTokenPepper` | auto-gen |
| E2E passwords | `e2e-{guest,owner,platform-admin}-password` | `E2E_*_PASSWORD` (nightly workflow) | placeholders (staging) |
| Blob | — (managed identity) | `Blob:AccountUrl` | endpoint injected |

---

## 5. Frontend config (`web/`)

**`NEXT_PUBLIC_*` / `process.env` reads:** `NEXT_PUBLIC_API_BASE_URL` (`client.ts`; set via Dockerfile build-arg + Bicep + **hard-coded FQDN in `cd-staging-web.yml:149`**), `NEXT_PUBLIC_ENTRA_AUTHORITY_{ADMIN,GUEST}` + `NEXT_PUBLIC_ENTRA_CLIENT_ID` (`msalConfig.ts`; fallback to `login.microsoftonline.com/common`), `NEXT_PUBLIC_SITE_URL` (`layout.tsx:14`; **var never set → always uses hard-coded `https://www.vrbook.example.com` fallback**), `PLAYWRIGHT_BASE_URL`, `CI`, `GITHUB_RUN_ID`, `E2E_*_PASSWORD`. **Declared-but-unread:** `NEXT_PUBLIC_MAPBOX_TOKEN`, `NEXT_PUBLIC_SIGNALR_NEGOTIATE_URL`, `NEXT_PUBLIC_STRIPE_PUBLISHABLE_KEY` (in env examples/`env.d.ts` only).

**Example files:** `web/.env.local.example` (canonical). `.env.example` (repo root, full-stack) — corrected by VRB-201 to the per-flow `NEXT_PUBLIC_ENTRA_AUTHORITY_{ADMIN,GUEST}` split (the retired single `NEXT_PUBLIC_ENTRA_AUTHORITY` was removed). Note: `web/README.md` still lists the retired key — a WEB-lane doc cleanup, tracked separately.

---

## 6. Feature flags — real runtime (VRB-203)

**Naming convention:** `Features:<Area>.<Capability>` — the `Features:` config section, then a dotted `Area.Capability` pair (e.g. `Features:Booking.InstantBook`). Declared with a safe default in `FeatureFlagKeys` (`src/Modules/VrBook.Modules.Admin/Application/FeatureFlagKeys.cs`).

**Resolution order** (`DbFeatureToggle : IFeatureToggle`, VRB-203, replaces the old `StubFeatureToggle`): DB override in `admin.feature_flags` → config value under the flag key → caller's safe default. Cached in `IMemoryCache` for 30s **per-replica** (the `IFeatureToggle` contract's Redis + Service Bus distributed cache-bust is **deferred, not dropped** — Redis is not deployed; swap when it lands). A PUT invalidates the cache key.

1. `GET /api/v1/admin/toggles` + `PUT /api/v1/admin/toggles/{key}` — real, **PlatformAdmin-only** (tenant admins 403). Global flags only for now (`scope="global"`; non-global → 400). List merges known config-backed defaults with DB overrides.
2. `Features:Booking.UseRedisHoldStore` (renamed from `Features:UseRedisHoldStore`) — default false; **startup-time** DI selection of the hold store (not a live toggle — takes effect on restart). `BookingModule.cs`.
3. `Features:Loyalty.Enabled` (renamed from `Loyalty:Enabled`) — default true; resolved **live** via `IFeatureToggle` in `RealLoyaltyDiscountResolver`, so a platform admin can toggle loyalty without a redeploy.
4. **`AlertsController` `GET/POST /admin/alerts`** — still **501** (separate slice; not feature flags).

---

## 7. Hard-coded values that belong in config (defects)

- **Loyalty tier thresholds** — `LoyaltyAccount.cs:65-66` `const int SilverThreshold=3; GoldThreshold=6;` — the `Loyalty:*Threshold` config keys (appsettings + Bicep) are **DEAD** for tier calc.
- **Booking 24h SLA** — `Booking.cs:119` `AddHours(24)` hard-coded; `Booking:TentativeSlaHours` **never read** (and Bicep comment says "6h" — inconsistency). `Booking:HoldDurationMinutes` has **no consumer**.
- **Sync config** — `Sync:DefaultPollIntervalMinutes`/`StaleAlertHours` have no consumer (poll interval is a per-feed DB column); + the `Sync__DefaultPollIntervalMin` name mismatch.
- **ChannelPoll rate-limit policy** — host suffixes + token/window/burst hard-coded in `ChannelPollOptions.cs`.
- **Postgres firewall IPs** — `174.104.204.213` (owner), `135.18.171.52` (CAE outbound) literals in `main.bicep`.
- **Staging API FQDN** — full `ca-vrbook-api-staging.icydesert-abf3fa4e.eastus2.azurecontainerapps.io` baked into `cd-staging-web.yml:149` (breaks on any CAE rebuild).
- **Placeholder domains** — `Acs:SenderAddress=donotreply@vrbook.example.com`, `Frontend:BaseUrl=http://localhost:3000`, `NEXT_PUBLIC_SITE_URL` fallback `www.vrbook.example.com`.
- **Config referenced but never provided** — `EntraExternalId:AdminFlowName`, `Payment:AllowPlatformFallback`.

---

## 8. Per-environment matrix (source of truth per key)

| Key / secret | dev | staging | prod | Source |
|---|---|---|---|---|
| Postgres SKU | B2s Burstable | B1ms Burstable | D4ds_v5 GP HA | `main.bicep:83-84` |
| API replicas (min/max) | 0/3 | 0/3 | 1/10 | `main.bicep:91-92` |
| Front Door + WAF | off | off | on | `main.bicep:111` |
| Redis | off | off | off | `staging.bicepparam:26` |
| `seedPlatformAdmins` | [] | niroshanaks@gmail.com | [] (add before deploy) | `main.bicep:52-62` |
| `bootstrapE2eTenantEnabled` | false | true | **false** | `main.bicep:62` |
| Stripe keys | test | test | **live (go-live)** | KV |
| ACS sender | managed domain | managed domain | **custom domain+DKIM (go-live)** | KV / `acs.bicep` |
| Entra secrets | placeholders | operator-set | **prod tenant cutover** | KV |

Full go-live secret/config checklist: [`docs/OPS_LAUNCH_COMPLETION_PLAN.md`](../OPS_LAUNCH_COMPLETION_PLAN.md) §6 + the Stripe/DKIM runbooks under `docs/runbooks/`.
