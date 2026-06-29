// acr.bicep — Premium Azure Container Registry.
// Admin user disabled, anonymous pull disabled — pulls authenticated via Managed Identity.
//
// OPS.M.10.2 ops-fix (2026-06-29) — removed `trustPolicy` block. Azure
// deprecated Content Trust (Docker Content Trust / Notary v1) on ACR;
// `policies.trustPolicy.status: 'enabled'` now returns
// `ContentTrustUnsupported` and fails every Bicep deploy. Removal date
// per Microsoft: March 31, 2028 (full removal); the API began rejecting
// the property earlier than that. The original §14.3 A08 intent
// (signed container images) is still valid but should migrate to
// Notary v2 / OCI artifact signatures via cosign — tracked as a
// separate ops slice (post-OPS.M.10.2).

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

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: acrName
  location: location
  tags: tags
  sku: {
    name: 'Premium'
  }
  properties: {
    adminUserEnabled: false
    anonymousPullEnabled: false
    publicNetworkAccess: 'Enabled'
    zoneRedundancy: 'Disabled'
    policies: {
      // OPS.M.10.2 ops-fix — trustPolicy removed (see header).
      retentionPolicy: {
        days: 30
        status: 'enabled'
      }
      quarantinePolicy: {
        status: 'disabled'
      }
      exportPolicy: {
        status: 'enabled'
      }
    }
    encryption: {
      status: 'disabled'
    }
  }
}

output id string = acr.id
output name string = acr.name
output loginServer string = acr.properties.loginServer
