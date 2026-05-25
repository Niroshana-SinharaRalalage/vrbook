// key-vault.bicep — Standard SKU Key Vault for VRBook secrets.
// Soft-delete and purge protection are ON (required for production secret hygiene
// and prevents accidental destruction during dev tear-down).
// Uses RBAC authorisation (no access policies); trusted Azure services bypass enabled
// so Container Apps managed identities can reach KV through the Azure backbone.

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

@description('Tenant ID for KV access.')
param tenantId string = subscription().tenantId

var vaultName = 'kv-vrbook-${env}'

resource vault 'Microsoft.KeyVault/vaults@2024-04-01-preview' = {
  name: vaultName
  location: location
  tags: tags
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
      ipRules: []
      virtualNetworkRules: []
    }
  }
}

output id string = vault.id
output name string = vault.name
output vaultUri string = vault.properties.vaultUri
