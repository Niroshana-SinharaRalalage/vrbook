// acr.bicep — Azure Container Registry.
// Admin user disabled, anonymous pull disabled — pulls authenticated via Managed Identity.
//
// Slice OPS.INFRA.2 (2026-07-09) — staging right-sizing. Premium ($50/mo)
// was overprovisioned; no Premium features (geo-replication / private
// endpoint / content trust) were wired. Basic ($5/mo) with 10 GB included
// storage covers VrBook's ~5-8 GB of images with headroom. Prod stays on
// Standard for retention-policy support; can bump to Premium at prod
// cutover if geo-replication or private-endpoint requirements land.
//
// OPS.M.10.2 ops-fix (2026-06-29) — removed `trustPolicy` block. Azure
// deprecated Content Trust (Docker Content Trust / Notary v1) on ACR;
// `policies.trustPolicy.status: 'enabled'` now returns
// `ContentTrustUnsupported` and fails every Bicep deploy.

@description('Environment short name (dev | staging | prod).')
param env string

@description('Azure region.')
param location string

@description('Common resource tags.')
param tags object = {
  env: env
  app: 'vrbook'
  costCenter: 'product'
}

var acrName = 'crvrbook${env}'

// Slice OPS.INFRA.2 — cheapest-workable tier per env. Basic (staging/dev)
// = $5/mo, 10 GB storage. Standard (prod) = $20/mo, 100 GB, retention
// policy support.
var acrSku = env == 'prod' ? 'Standard' : 'Basic'

// Retention policy + zone redundancy require Premium (per Azure docs).
// Basic tier gets neither; policies object omitted below so Bicep
// doesn't fail on unsupported-feature validation.
var acrSupportsPolicies = acrSku == 'Standard' || acrSku == 'Premium'

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  tags: tags
  sku: {
    name: acrSku
  }
  properties: {
    adminUserEnabled: false
    anonymousPullEnabled: false
    publicNetworkAccess: 'Enabled'
    zoneRedundancy: 'Disabled'
    policies: acrSupportsPolicies ? {
      retentionPolicy: {
        days: 30
        status: 'enabled'
      }
      exportPolicy: {
        status: 'enabled'
      }
    } : {}
    encryption: {
      status: 'disabled'
    }
  }
}

output id string = acr.id
output name string = acr.name
output loginServer string = acr.properties.loginServer
