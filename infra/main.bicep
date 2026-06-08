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

// ---------- Derived flags & sizing ----------
var isProd = env == 'prod'
var isStaging = env == 'staging'

// Postgres Flexible Server SKU NAME (without tier prefix). Tier passed separately via skuTier.
var pgSku = isProd ? 'Standard_D4ds_v5' : (isStaging ? 'Standard_D2ds_v5' : 'Standard_B2s')
var pgTier = isProd || isStaging ? 'GeneralPurpose' : 'Burstable'
var pgBackupRetention = isProd ? 35 : 14
var pgStorageGB = isProd ? 256 : 128

var apiMinReplicas = env == 'dev' ? 0 : 1
var apiMaxReplicas = isProd ? 10 : 5

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
module pg 'modules/postgres-flexible.bicep' = {
  name: 'pg'
  params: {
    env: env
    location: location
    tags: tags
    subnetId: net.outputs.pgSubnetId
    privateDnsZoneId: net.outputs.pgPrivateDnsZoneId
    skuName: pgSku
    skuTier: pgTier
    storageSizeGB: pgStorageGB
    backupRetentionDays: pgBackupRetention
    haEnabled: isProd
    administratorLogin: pgAdminLogin
    administratorLoginPassword: pgAdminPassword
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
  { name: 'Acs__SenderAddress', value: 'donotreply@vrbook.example.com' }
  // Identity — Microsoft Entra External ID (ADR-0012 supersedes AD B2C).
  { name: 'EntraExternalId__Instance', secretRef: 'entra-instance' }
  { name: 'EntraExternalId__TenantId', secretRef: 'entra-tenant-id' }
  { name: 'EntraExternalId__ClientId', secretRef: 'entra-api-client-id' }
  // DevAuth enabled in dev + staging (first-deploy validation path); disabled in prod.
  // Switch staging back to 'false' before exposing to anyone outside the eng team.
  { name: 'DevAuth__AllowAnonymous', value: isProd ? 'false' : 'true' }
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
  { name: 'Sync__DefaultPollIntervalMin', value: '30' }
  { name: 'Sync__StaleAlertHours', value: '2' }
  { name: 'Booking__TentativeSlaHours', value: '24' }
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
  { name: 'entra-instance', keyVaultSecretName: 'entra-instance' }
  { name: 'entra-tenant-id', keyVaultSecretName: 'entra-tenant-id' }
  { name: 'entra-api-client-id', keyVaultSecretName: 'entra-api-client-id' }
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
    cpu: '1.0'
    memory: '2Gi'
    envVars: apiEnvVars
    secrets: apiSecrets
    keyVaultName: kv.outputs.name
    scaleRuleType: 'http'
    httpConcurrentRequests: 50
  }
}

// ---------- Booking worker (Service Bus-triggered Container App) ----------
module bookingWorker 'modules/container-app.bicep' = {
  name: 'booking-worker'
  params: {
    name: 'ca-vrbook-bookingworker-${env}'
    location: location
    tags: tags
    environmentId: cae.outputs.id
    containerImage: bookingWorkerImage
    registryServer: acr.outputs.loginServer
    userAssignedIdentityId: mi.outputs.id
    workloadProfileName: 'Consumption'
    targetPort: 8080
    externalIngress: false
    minReplicas: env == 'dev' ? 0 : 1
    maxReplicas: 5
    cpu: '0.5'
    memory: '1Gi'
    envVars: apiEnvVars
    secrets: apiSecrets
    keyVaultName: kv.outputs.name
    scaleRuleType: 'kedaServiceBus'
    serviceBusNamespace: sb.outputs.namespaceFqdn
    serviceBusTopicName: 'bookings'
    serviceBusSubscriptionName: 'default'
    serviceBusMessageCount: 10
    includeProbes: false
    enableIngress: false
  }
}

// ---------- Notifications worker ----------
module notifWorker 'modules/container-app.bicep' = {
  name: 'notif-worker'
  params: {
    name: 'ca-vrbook-notifworker-${env}'
    location: location
    tags: tags
    environmentId: cae.outputs.id
    containerImage: notificationWorkerImage
    registryServer: acr.outputs.loginServer
    userAssignedIdentityId: mi.outputs.id
    workloadProfileName: 'Consumption'
    targetPort: 8080
    externalIngress: false
    minReplicas: env == 'dev' ? 0 : 1
    maxReplicas: 5
    cpu: '0.5'
    memory: '1Gi'
    envVars: apiEnvVars
    secrets: apiSecrets
    keyVaultName: kv.outputs.name
    scaleRuleType: 'kedaServiceBus'
    serviceBusNamespace: sb.outputs.namespaceFqdn
    serviceBusTopicName: 'notifications'
    serviceBusSubscriptionName: 'default'
    serviceBusMessageCount: 20
    includeProbes: false
    enableIngress: false
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

// ---------- DB migrator (manual-trigger job) ----------
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
    envVars: apiEnvVars
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
    minReplicas: env == 'dev' ? 0 : 1
    maxReplicas: isProd ? 5 : 3
    cpu: '0.5'
    memory: '1Gi'
    envVars: webEnvVars
    secrets: []
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
  }
}

// ---------- Action Group + Alert Rules (ADR 0010, EXECUTION_PLAN.md A0.1.7) ----------
module actionGroup 'modules/action-group.bicep' = {
  name: 'actionGroup'
  params: {
    env: env
    tags: tags
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
