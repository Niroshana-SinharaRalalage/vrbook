// main.bicep — Orchestrator for the VRBook Azure deployment.
// Wires every module together. Conditional sizing per env:
//   prod    — HA Postgres, Front Door enabled, dedicated workload profile
//   staging — no HA, Front Door enabled
//   dev     — no HA, no Front Door, consumption-only
//
// Secrets convention:
//   The KV is created here, but secret values are seeded out-of-band by the
//   CI/CD pipeline (see §16). main.bicep accepts the Postgres admin password
//   as a @secure() param so the pipeline can pass it via getSecret() from a
//   bootstrap KV — it never appears in a parameter file.

targetScope = 'resourceGroup'

@description('Environment short name.')
@allowed([
  'dev'
  'staging'
  'prod'
])
param env string

@description('Azure region.')
param location string = 'eastus2'

@description('Postgres administrator login.')
param pgAdminLogin string = 'vrbook_admin'

@description('Postgres administrator password (pipeline-supplied via getSecret() from a bootstrap KV).')
@secure()
param pgAdminPassword string

@description('Container image for the API (pipeline supplies a tag; default placeholder for what-if).')
param apiImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Container image for the iCal sync worker job.')
param syncWorkerImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Container image for the booking worker (SB-triggered).')
param bookingWorkerImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Container image for the notifications worker (SB-triggered).')
param notificationWorkerImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Container image for the DB migrator job (manual trigger).')
param migratorImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Container image for the Next.js web frontend.')
param webImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('VRB-306 — alert recipient email (on-call). Kept a param (never hard-coded); set per env in the .bicepparam. Empty ⇒ recipients stay portal-managed.')
param alertEmail string = ''

@description('VRB-306 — named owner label stamped on each alert. The email lives on the action group; this is the human-readable owner recorded per alert.')
param alertOwner string = 'on-call'

@description('Slice OPS.M.22.6 — declarative pre-M.22 platform admin backfill list. Each entry becomes an idempotent identity.users row via VrBook.Migrator.SeedPlatformAdminsBackfill on every deploy. Empty list = no-op. Staging default carries the owner; prod cutover adds team leads before first deploy.')
param seedPlatformAdmins array = env == 'staging'
  ? [
      {
        email: 'niroshanaks@gmail.com'
        displayName: 'Niroshana'
      }
    ]
  : []

@description('Slice OPS.2.2 — enable the Playwright E2E fixture backfill (isolated e2e-tenant + pre-seeded e2e-owner / e2e-platform-admin personas via VrBook.Migrator.SeedE2EBackfill). Staging only; MUST stay false in prod so the E2E marker never appears on a production tenant. Consumed as Bootstrap:E2e:Enabled by the migrator.')
param bootstrapE2eTenantEnabled bool = env == 'staging'

// ---------- Derived flags & sizing ----------
var isProd = env == 'prod'
var isStaging = env == 'staging'
var isDev = env == 'dev'

// Postgres Flexible Server SKU NAME (without tier prefix). Tier passed separately via skuTier.
//
// Slice OPS.INFRA.2 (2026-07-09) — staging right-sizing:
//   * D2ds_v5 GeneralPurpose (~$110/mo compute) → B1ms Burstable (~$15/mo).
//     Staging carries one operator + Playwright E2E; burstable easily covers it.
//     Cross-tier scaling (GP → Burstable) is an in-place operation with brief
//     downtime, supported by Azure Postgres Flex Server.
//   * Backup retention 14d → 7d for staging (still comfortable; free storage
//     up to 100% of provisioned).
//   * Storage stays at 128 GB — Azure Flex Server does NOT support storage
//     shrink; setting a smaller size than current fails deployment. A future
//     blue/green rebuild could drop it to 32 GB and save ~$10/mo, but the
//     operational cost is not worth ~$10/mo. Tracked as OPS.INFRA.3 candidate.
//   * prod stays on D4ds_v5 GeneralPurpose 256 GB; unchanged.
var pgSku = isProd ? 'Standard_D4ds_v5' : (isStaging ? 'Standard_B1ms' : 'Standard_B2s')
var pgTier = isProd ? 'GeneralPurpose' : 'Burstable'
var pgBackupRetention = isProd ? 35 : 7
var pgStorageGB = isProd ? 256 : 128

// Slice OPS.INFRA.2 — scale-to-zero for staging API + web. First cold-start
// costs ~2-4s vs prod's always-warm min=1. Prod stays on min=1 for SLO;
// dev+staging idle 20+ hours a day and don't need a warm replica.
var apiMinReplicas = isProd ? 1 : 0
var apiMaxReplicas = isProd ? 10 : 3

// Slice OPS.INFRA.2 — cheapest-workable container-app resource envelope.
//   * API (staging+dev): 0.5 vCPU / 1 GiB. Live working set ~200 MiB idle;
//     under load a 12-module .NET monolith + EF/MediatR climbs to 400-700 MiB
//     — 1 GiB is the correct floor. Below that risks OOM (GC starts at ~75%
//     of limit → 384 MiB for a 512 MiB container).
//   * Web (staging+dev): 0.25 vCPU / 0.5 GiB. Next.js standalone runs at
//     ~108 MiB working set; 512 MiB has 4x headroom.
//   * Both scale-to-zero (min=0) with cold-start ~2-4s on first request.
//   * Prod stays 1.0/2Gi (api) + 0.5/1Gi (web); untouched until prod cutover.
var apiCpu = isProd ? '1.0' : '0.5'
var apiMemory = isProd ? '2Gi' : '1Gi'
var webCpu = isProd ? '0.5' : '0.25'
var webMemory = isProd ? '1Gi' : '0.5Gi'

// Front Door deferred for staging — the staging WAF policy needs Premium_AzureFrontDoor
// SKU to use managed rules (Standard rejects ManagedRules per WAF schema). Enable for prod
// only until we revisit Premium AFD vs custom-rules-only WAF. See ADR-XXXX (todo).
var frontDoorEnabled = isProd
var dedicatedProfileEnabled = isProd

var tags = {
  env: env
  app: 'vrbook'
  costCenter: 'product'
}

// ---------- Foundation: network + KV + logs ----------
module net 'modules/network.bicep' = {
  name: 'net'
  params: {
    env: env
    location: location
    tags: tags
    // Slice OPS.INFRA.2 — only carry the Redis DNS zone when redis is actually
    // deployed. Currently deployRedis=false everywhere → skip the zone (~$0.50/mo).
    includeRedisDns: deployRedis
  }
}

module kv 'modules/key-vault.bicep' = {
  name: 'kv'
  params: {
    env: env
    location: location
    tags: tags
  }
}

// Slice 0.5: Azure Communication Services Email per ADR-0011.
// The 'acs-connection-string' Key Vault secret reference already exists in
// apiSecrets[] below; this module provisions the resource + writes the
// connection string into Key Vault. Slice 4 wires the actual email sender.
module acs 'modules/acs.bicep' = {
  name: 'acs'
  params: {
    env: env
    tags: tags
    keyVaultName: kv.outputs.name
  }
}

module law 'modules/log-analytics.bicep' = {
  name: 'law'
  params: {
    env: env
    location: location
    tags: tags
  }
}

module appi 'modules/app-insights.bicep' = {
  name: 'appi'
  params: {
    env: env
    location: location
    tags: tags
    workspaceId: law.outputs.id
  }
}

// ---------- Container registry ----------
module acr 'modules/acr.bicep' = {
  name: 'acr'
  params: {
    env: env
    location: location
    tags: tags
  }
}

// ---------- Data plane ----------
// OPS.INFRA.1 post-cutover shape (2026-07-05):
//   * Staging: publicNetworkAccess=Enabled + IP allowlist + serverNameOverride
//     locks the server as psql-vrbook-staging-v2 (created during the blue/green
//     rebuild). Postgres Flex Server does not support rename, so the -v2
//     suffix is permanent (cosmetic only).
//   * Prod: VNet-injected + Disabled per parameter defaults. Never touched by
//     INFRA.1. Prod-specific privacy posture will be revisited at prod cutover.
var pgIsStagingPublic = env == 'staging'
var pgStagingFirewallRules = [
  // Owner IP from LankaConnect staging allowlist.
  {
    name: 'Owner-Home-Office'
    startIp: '174.104.204.213'
    endIp: '174.104.204.213'
  }
  // Portal convention: 0.0.0.0-0.0.0.0 = allow all in-region Azure-internal
  // traffic. NOT the internet.
  {
    name: 'AllowAzureServices'
    startIp: '0.0.0.0'
    endIp: '0.0.0.0'
  }
  // OPS.INFRA.1 A8 remediation: CAE outbound IP needs an explicit rule; the
  // AllowAzureServices umbrella did NOT cover Container Apps → Postgres Flex
  // outbound. Discovered empirically at cutover; without it new API revisions
  // failed activation on health-probe timeout.
  {
    name: 'CAE-Outbound'
    startIp: '135.18.171.52'
    endIp: '135.18.171.52'
  }
]

module pg 'modules/postgres-flexible.bicep' = {
  name: 'pg'
  params: {
    env: env
    location: location
    tags: tags
    subnetId: net.outputs.pgSubnetId       // ignored when publicNetworkAccess=Enabled
    privateDnsZoneId: net.outputs.pgPrivateDnsZoneId  // ditto
    skuName: pgSku
    skuTier: pgTier
    storageSizeGB: pgStorageGB
    backupRetentionDays: pgBackupRetention
    haEnabled: isProd
    administratorLogin: pgAdminLogin
    administratorLoginPassword: pgAdminPassword
    publicNetworkAccess: pgIsStagingPublic ? 'Enabled' : 'Disabled'
    firewallRules: pgIsStagingPublic ? pgStagingFirewallRules : []
    serverNameOverride: pgIsStagingPublic ? 'psql-vrbook-staging-v2' : ''
  }
}

// Azure Cache for Redis is being retired by Microsoft. We're temporarily skipping
// it for staging to unblock the first deploy; will revisit with Azure Managed Redis
// (Microsoft.Cache/redisEnterprise) before A3 (pricing engine) which actually needs it.
@description('Whether to deploy Azure Cache for Redis. Disabled in staging due to MS retirement.')
param deployRedis bool = false

module redis 'modules/redis.bicep' = if (deployRedis) {
  name: 'redis'
  params: {
    env: env
    location: location
    tags: tags
    subnetId: net.outputs.dataSubnetId
    privateDnsZoneId: net.outputs.redisPrivateDnsZoneId
  }
}

// ---------- Messaging & realtime ----------
module sb 'modules/service-bus.bicep' = {
  name: 'sb'
  params: {
    env: env
    location: location
    tags: tags
  }
}

module sigr 'modules/signalr.bicep' = {
  name: 'sigr'
  params: {
    env: env
    location: location
    tags: tags
    keyVaultName: kv.outputs.name
  }
}

// ---------- Storage ----------
module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    env: env
    location: location
    tags: tags
  }
}

// ---------- Identity (depends on KV + storage + SB + ACR) ----------
module mi 'modules/managed-identity.bicep' = {
  name: 'mi'
  params: {
    env: env
    location: location
    tags: tags
    keyVaultId: kv.outputs.id
    storageAccountId: storage.outputs.id
    serviceBusNamespaceId: sb.outputs.id
    acrId: acr.outputs.id
  }
}

// ---------- Container Apps Environment ----------
// Reference the Log Analytics workspace via `existing` so listKeys() can resolve at
// deploy-prep time (the name pattern is computable at compile time because `env` is
// a param). dependsOn keeps ordering with the deploying module.
resource lawExisting 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: 'law-vrbook-${env}'
  dependsOn: [
    law
  ]
}

module cae 'modules/container-apps-env.bicep' = {
  name: 'cae'
  params: {
    env: env
    location: location
    tags: tags
    infrastructureSubnetId: net.outputs.appsSubnetId
    logAnalyticsCustomerId: law.outputs.customerId
    logAnalyticsSharedKey: lawExisting.listKeys().primarySharedKey
    includeDedicatedProfile: dedicatedProfileEnabled
  }
}

// ---------- Shared env-var inventory for the API (§23.3) ----------
// Non-secret values are inlined; secrets are referenced via secretRef into the secrets array.
var apiEnvVars = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: isProd ? 'Production' : (isStaging ? 'Staging' : 'Development') }
  { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
  { name: 'ConnectionStrings__Postgres', secretRef: 'postgres-cs' }
  { name: 'ConnectionStrings__Redis', secretRef: 'redis-cs' }
  { name: 'ServiceBus__FullyQualifiedNamespace', value: sb.outputs.namespaceFqdn }
  { name: 'SignalR__ConnectionString', secretRef: 'signalr-cs' }
  { name: 'Stripe__SecretKey', secretRef: 'stripe-secret' }
  { name: 'Stripe__WebhookSecret', secretRef: 'stripe-webhook-secret' }
  { name: 'Stripe__PublishableKey', secretRef: 'stripe-publishable-key' }
  // Email — Azure Communication Services (ADR-0011 supersedes SendGrid).
  // Connection string is seeded out-of-band into KV (or provisioned by a future
  // acs-email.bicep module). A9 reads Acs__* in lieu of SendGrid__*.
  { name: 'Acs__ConnectionString', secretRef: 'acs-connection-string' }
  // Slice 4 C2/C5: sender uses the ACS AzureManagedDomain that the email
  // service auto-provisions (DKIM/SPF/DMARC pre-verified). Custom domain
  // (e.g. bookings@vrbook.example.com) lands in OPS.8 / MULTI_TENANCY_OPS_PLAN
  // §8 once the DNS records are wired. The GUID is environment-specific;
  // production picks up its own managed domain at deploy time.
  { name: 'Acs__SenderAddress', secretRef: 'acs-sender-address' }
  // Identity — Microsoft Entra External ID (ADR-0012 supersedes AD B2C).
  { name: 'EntraExternalId__Instance', secretRef: 'entra-instance' }
  { name: 'EntraExternalId__TenantId', secretRef: 'entra-tenant-id' }
  { name: 'EntraExternalId__ClientId', secretRef: 'entra-api-client-id' }
  // Slice OPS.M.12.6 — the External-tenant issuer host used by
  // IdentityProviderClassifier to normalize a token's `idp` claim to the
  // canonical `entra` string when it matches the tenant issuer host (rather
  // than the Entra-local absent-idp shape). See
  // src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/IdentityProviderClassifier.cs.
  // Value is e.g. `vrbookcid.ciamlogin.com` — not sensitive, but stored in
  // KV for symmetry with the rest of the Entra config.
  { name: 'EntraExternalId__TenantIssuerHost', secretRef: 'entra-tenant-issuer-host' }
  // App:WebBaseUrl — same-origin base URL for outbound deep links (review
  // notification etc.). Empty in staging + prod so links fall through to the
  // handler's built-in fallback; populated in dev so notification templates
  // resolve to the local web container.
  { name: 'App__WebBaseUrl', value: isDev ? 'https://ca-vrbook-web-${env}.${cae.outputs.defaultDomain}' : '' }
  // CORS - allow the deployed web Container App + localhost for dev. Same-cluster
  // ingress means we know the FQDN deterministically: ca-vrbook-web-{env}.{caeDomain}.
  { name: 'Cors__AllowedOrigins__0', value: 'http://localhost:3000' }
  { name: 'Cors__AllowedOrigins__1', value: 'https://ca-vrbook-web-${env}.${cae.outputs.defaultDomain}' }
  // Swagger UI exposed in dev + staging for engineering convenience.
  // Prod never serves the spec - clients use the published OpenAPI artifact.
  { name: 'Swagger__EnableInProduction', value: isProd ? 'false' : 'true' }
  { name: 'Blob__AccountUrl', value: storage.outputs.blobEndpoint }
  { name: 'Blob__PropertyImagesContainer', value: 'property-images' }
  { name: 'Blob__MessageAttachmentsContainer', value: 'message-attachments' }
  { name: 'Feed__OutboundTokenPepper', secretRef: 'feed-pepper' }
  { name: 'Booking__TentativeSlaHours', value: '48' } // VRB-207 (G2/Q1) — owner-locked 48h hold window
  { name: 'Booking__HoldDurationMinutes', value: '15' }
  // Platform-wide service fee retained on refunds for captured bookings (0..100).
  // Set to 0 to issue full refunds. Per-property fees land in A5.1.
  { name: 'Refund__ServiceFeePercent', value: '0' }
  { name: 'Loyalty__BronzeThreshold', value: '1' }
  { name: 'Loyalty__SilverThreshold', value: '3' }
  { name: 'Loyalty__GoldThreshold', value: '6' }
  { name: 'ApplicationInsights__ConnectionString', secretRef: 'appi-cs' }
  { name: 'AZURE_CLIENT_ID', value: mi.outputs.clientId }
]

// Secret descriptors — names referenced by env vars above must match s.name here.
var apiSecrets = [
  { name: 'postgres-cs', keyVaultSecretName: 'postgres-cs' }
  { name: 'redis-cs', keyVaultSecretName: 'redis-cs' }
  { name: 'signalr-cs', keyVaultSecretName: 'signalr-cs' }
  { name: 'stripe-secret', keyVaultSecretName: 'stripe-secret' }
  { name: 'stripe-webhook-secret', keyVaultSecretName: 'stripe-webhook-secret' }
  { name: 'stripe-publishable-key', keyVaultSecretName: 'stripe-publishable-key' }
  { name: 'acs-connection-string', keyVaultSecretName: 'acs-connection-string' }
  { name: 'acs-sender-address', keyVaultSecretName: 'acs-sender-address' }
  { name: 'entra-instance', keyVaultSecretName: 'entra-instance' }
  { name: 'entra-tenant-id', keyVaultSecretName: 'entra-tenant-id' }
  { name: 'entra-api-client-id', keyVaultSecretName: 'entra-api-client-id' }
  { name: 'entra-tenant-issuer-host', keyVaultSecretName: 'entra-tenant-issuer-host' }
  { name: 'feed-pepper', keyVaultSecretName: 'feed-pepper' }
  { name: 'appi-cs', keyVaultSecretName: 'appi-cs' }
]

// ---------- API Container App ----------
module apiApp 'modules/container-app.bicep' = {
  name: 'api'
  params: {
    name: 'ca-vrbook-api-${env}'
    location: location
    tags: tags
    environmentId: cae.outputs.id
    containerImage: apiImage
    registryServer: acr.outputs.loginServer
    userAssignedIdentityId: mi.outputs.id
    workloadProfileName: dedicatedProfileEnabled ? 'd4' : 'Consumption'
    targetPort: 8080
    externalIngress: true
    minReplicas: apiMinReplicas
    maxReplicas: apiMaxReplicas
    cpu: apiCpu
    memory: apiMemory
    envVars: apiEnvVars
    secrets: apiSecrets
    keyVaultName: kv.outputs.name
    scaleRuleType: 'http'
    httpConcurrentRequests: 50
  }
}

// ---------- Notification Dispatch Worker (scheduled job, */2 * * * *) ----------
// Slice 4 C2: drains Queued NotificationLog rows + dispatches via ACS per
// ADR-0011. One-shot per tick; the dispatcher releases stale Sending leases
// (5-min timeout), picks up to 50 rows whose NotBefore is due, leases each,
// sends, marks Sent or Failed/DeadLetter. See docs/SLICE4_PLAN.md §2.4-§2.5.
//
// Replaces the A9 v1 KEDA Service Bus Container App that never had a real
// dispatcher attached. The 'notifications' Service Bus topic stays declared
// in service-bus.bicep for the A11 outbox->topic relay (Phase 2).
module notifDispatchJob 'modules/container-app-job.bicep' = {
  name: 'notif-dispatch-job'
  params: {
    name: 'caj-vrbook-notifdispatch-${env}'
    location: location
    tags: tags
    environmentId: cae.outputs.id
    containerImage: notificationWorkerImage
    registryServer: acr.outputs.loginServer
    userAssignedIdentityId: mi.outputs.id
    workloadProfileName: 'Consumption'
    triggerType: 'Schedule'
    cronExpression: '*/2 * * * *'
    replicaTimeoutSeconds: 300
    cpu: '0.5'
    memory: '1Gi'
    envVars: apiEnvVars
    secrets: apiSecrets
    keyVaultName: kv.outputs.name
  }
}

// ---------- iCal sync worker (scheduled job, */5 * * * *) ----------
module syncJob 'modules/container-app-job.bicep' = {
  name: 'sync-job'
  params: {
    name: 'caj-vrbook-sync-${env}'
    location: location
    tags: tags
    environmentId: cae.outputs.id
    containerImage: syncWorkerImage
    registryServer: acr.outputs.loginServer
    userAssignedIdentityId: mi.outputs.id
    workloadProfileName: 'Consumption'
    triggerType: 'Schedule'
    cronExpression: '*/5 * * * *'
    replicaTimeoutSeconds: 600
    cpu: '0.5'
    memory: '1Gi'
    envVars: apiEnvVars
    secrets: apiSecrets
    keyVaultName: kv.outputs.name
  }
}

// ---------- Booking SLA expiry sweep (scheduled job, */10 * * * *) ----------
// Slice 0.4: scans Tentative bookings whose SLA window (Booking:TentativeSlaHours,
// 48h — VRB-207) has elapsed, i.e. TentativeUntil <= now. Auto-confirms
// when no iCal conflict; auto-cancels (and cancels the Stripe auth-hold) when one
// exists. Default --mode=expiry on the worker, so no args block needed.
// See docs/REPLAN.md slice 0.4.
module bookingExpiryJob 'modules/container-app-job.bicep' = {
  name: 'booking-expiry-job'
  params: {
    name: 'caj-vrbook-bookingexpiry-${env}'
    location: location
    tags: tags
    environmentId: cae.outputs.id
    containerImage: bookingWorkerImage
    registryServer: acr.outputs.loginServer
    userAssignedIdentityId: mi.outputs.id
    workloadProfileName: 'Consumption'
    triggerType: 'Schedule'
    cronExpression: '*/10 * * * *'
    replicaTimeoutSeconds: 600
    cpu: '0.5'
    memory: '1Gi'
    envVars: apiEnvVars
    secrets: apiSecrets
    keyVaultName: kv.outputs.name
  }
}

// ---------- Booking completion sweep (scheduled job, 0 6 * * * UTC) ----------
// Slice 5: scans CheckedOut bookings whose CheckedOutAt is at least 24h old,
// calls Booking.Complete() which raises BookingCompleted -> Loyalty stay-count
// increment + Notifications "thanks for staying" + deferred review.request
// email. Same worker image as bookingExpiryJob, distinguished by --mode arg.
// See docs/SLICE5_PLAN.md §2.1.
module bookingCompletionJob 'modules/container-app-job.bicep' = {
  name: 'booking-completion-job'
  params: {
    name: 'caj-vrbook-completion-${env}'
    location: location
    tags: tags
    environmentId: cae.outputs.id
    containerImage: bookingWorkerImage
    registryServer: acr.outputs.loginServer
    userAssignedIdentityId: mi.outputs.id
    workloadProfileName: 'Consumption'
    triggerType: 'Schedule'
    cronExpression: '0 6 * * *'
    replicaTimeoutSeconds: 600
    cpu: '0.5'
    memory: '1Gi'
    envVars: apiEnvVars
    secrets: apiSecrets
    keyVaultName: kv.outputs.name
    commandArgs: ['--mode=completion']
  }
}

// ---------- DB migrator (manual-trigger job) ----------
// Slice OPS.M.22.6 — the migrator additionally carries the
// Bootstrap:SeedPlatformAdmins backfill array as flattened env vars
// (`Bootstrap__SeedPlatformAdmins__N__Email` / `_DisplayName`). Empty
// list = zero extra env vars = zero backfill work at migrator start.
// Every entry produces an idempotent identity.users insert (SeedPlatform
// AdminsBackfill.RunAsync). Kept OUT of apiEnvVars so the api / workers
// don't carry backfill config they don't consume.
var backfillEnvVars = flatten(map(range(0, length(seedPlatformAdmins)), i => [
  { name: 'Bootstrap__SeedPlatformAdmins__${i}__Email', value: seedPlatformAdmins[i].email }
  { name: 'Bootstrap__SeedPlatformAdmins__${i}__DisplayName', value: seedPlatformAdmins[i].displayName }
]))
// Slice OPS.2.2 — the E2E fixture backfill is a single boolean flag. Prod
// resolves to false so SeedE2EBackfill is a no-op and no is_e2e=true row is
// ever created outside staging.
var e2eBackfillEnvVars = [
  { name: 'Bootstrap__E2e__Enabled', value: string(bootstrapE2eTenantEnabled) }
]
var migratorEnvVars = concat(apiEnvVars, backfillEnvVars, e2eBackfillEnvVars)

module migratorJob 'modules/container-app-job.bicep' = {
  name: 'migrator-job'
  params: {
    name: 'caj-vrbook-migrator-${env}'
    location: location
    tags: tags
    environmentId: cae.outputs.id
    containerImage: migratorImage
    registryServer: acr.outputs.loginServer
    userAssignedIdentityId: mi.outputs.id
    workloadProfileName: 'Consumption'
    triggerType: 'Manual'
    replicaTimeoutSeconds: 1800
    cpu: '0.5'
    memory: '1Gi'
    envVars: migratorEnvVars
    secrets: apiSecrets
    keyVaultName: kv.outputs.name
  }
}

// ---------- Web (Next.js Container App) ----------
// Separate set of env vars so we don't expose API secrets to the web container.
// Only NEXT_PUBLIC_* values are baked into the client bundle at build time, but
// we still inject them at runtime as well for any server components.
var webEnvVars = [
  { name: 'NODE_ENV', value: 'production' }
  { name: 'PORT', value: '3000' }
  { name: 'HOSTNAME', value: '0.0.0.0' }
  { name: 'NEXT_TELEMETRY_DISABLED', value: '1' }
  { name: 'NEXT_PUBLIC_API_BASE_URL', value: 'https://${apiApp.outputs.fqdn}' }
  // OPS.M.0 — these reach Next.js server components / middleware at runtime.
  // The browser bundle (where MSAL actually executes) is sealed at `next build`
  // and reads the build-args; see cd-staging-web.yml + web/Dockerfile.
  //
  // Slice OPS.M.12.6+12.8 — per-flow authority split. `_ADMIN` points at
  // the `AdminSignUpSignIn` Entra user flow (Entra local only); `_GUEST`
  // points at `GuestSignUpSignIn` (Entra local + Google/Microsoft/
  // Facebook/Apple). The legacy `NEXT_PUBLIC_ENTRA_AUTHORITY` env var was
  // dropped in M.12.8 (this file). See ADR-0016 and
  // docs/runbooks/social_idp_setup.md §7.
  { name: 'NEXT_PUBLIC_ENTRA_AUTHORITY_ADMIN', secretRef: 'entra-web-authority-admin' }
  { name: 'NEXT_PUBLIC_ENTRA_AUTHORITY_GUEST', secretRef: 'entra-web-authority-guest' }
  { name: 'NEXT_PUBLIC_ENTRA_CLIENT_ID', secretRef: 'entra-web-client-id' }
]

module webApp 'modules/container-app.bicep' = {
  name: 'web'
  params: {
    name: 'ca-vrbook-web-${env}'
    location: location
    tags: tags
    environmentId: cae.outputs.id
    containerImage: webImage
    registryServer: acr.outputs.loginServer
    userAssignedIdentityId: mi.outputs.id
    workloadProfileName: 'Consumption'
    targetPort: 3000
    externalIngress: true
    // Slice OPS.INFRA.2 — staging + dev scale-to-zero (was min=1 for staging).
    minReplicas: isProd ? 1 : 0
    maxReplicas: isProd ? 5 : 2
    cpu: webCpu
    memory: webMemory
    envVars: webEnvVars
    secrets: [
      { name: 'entra-web-authority-admin', keyVaultSecretName: 'entra-web-authority-admin' }
      { name: 'entra-web-authority-guest', keyVaultSecretName: 'entra-web-authority-guest' }
      { name: 'entra-web-client-id', keyVaultSecretName: 'entra-web-client-id' }
    ]
    keyVaultName: kv.outputs.name
    scaleRuleType: 'http'
    httpConcurrentRequests: 50
    includeProbes: false
    enableIngress: true
  }
}

// ---------- Front Door (prod + staging only) ----------
module fd 'modules/front-door.bicep' = if (frontDoorEnabled) {
  name: 'fd'
  params: {
    env: env
    tags: tags
    originHostName: apiApp.outputs.fqdn
  }
}

// ---------- Observability Workbook ----------
// VrBook Operations dashboard. See infra/modules/workbook.bicep + docs/observability/queries.kql.
module workbook 'modules/workbook.bicep' = {
  name: 'workbook'
  params: {
    env: env
    location: location
    tags: tags
    workspaceId: law.outputs.id
    // VRB-306 — resource ids for the Postgres + Container App metric tiles.
    postgresId: pg.outputs.id
    apiAppId: apiApp.outputs.id
  }
}

// ---------- Action Group + Alert Rules (ADR 0010, EXECUTION_PLAN.md A0.1.7) ----------
module actionGroup 'modules/action-group.bicep' = {
  name: 'actionGroup'
  params: {
    env: env
    tags: tags
    alertEmail: alertEmail
  }
}

module alerts 'modules/alerts.bicep' = {
  name: 'alerts'
  params: {
    env: env
    location: location
    tags: tags
    workspaceId: law.outputs.id
    postgresId: pg.outputs.id
    redisId: deployRedis ? redis.outputs.id : ''
    actionGroupId: actionGroup.outputs.id
    // VRB-306 — App Insights + API URL for the synthetic availability test;
    // named owner stamped on each alert.
    appInsightsId: appi.outputs.id
    // dev gets no synthetic availability test (no long-lived public API); staging + prod do.
    apiBaseUrl: env == 'dev' ? '' : 'https://${apiApp.outputs.fqdn}'
    alertOwner: alertOwner
  }
}

// ---------- Outputs ----------
output apiFqdn string = apiApp.outputs.fqdn
output webFqdn string = webApp.outputs.fqdn
output frontDoorHostName string = frontDoorEnabled ? fd.outputs.endpointHostName : ''
output keyVaultUri string = kv.outputs.vaultUri
output keyVaultName string = kv.outputs.name
output acrLoginServer string = acr.outputs.loginServer
output containerAppsEnvName string = cae.outputs.name
output managedIdentityClientId string = mi.outputs.clientId
output managedIdentityPrincipalId string = mi.outputs.principalId
output postgresFqdn string = pg.outputs.fqdn
output redisHostName string = deployRedis ? redis.outputs.hostName : ''
output serviceBusEndpoint string = sb.outputs.endpoint
output signalrHostName string = sigr.outputs.hostName
output storageBlobEndpoint string = storage.outputs.blobEndpoint
output appInsightsConnectionString string = appi.outputs.connectionString
