// managed-identity.bicep — User-assigned managed identity shared by API + workers.
// Role assignments:
//   Key Vault Secrets User           on the KV     — pull secrets referenced by Container Apps
//   Storage Blob Data Contributor    on storage    — read/write blobs (no SAS needed for MI)
//   Azure Service Bus Data Owner     on SB ns      — listen/send + management for KEDA scalers
//   AcrPull                          on ACR        — pull images from the registry

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

@description('Key Vault resource ID to grant Secrets User on.')
param keyVaultId string

@description('Storage account resource ID to grant Blob Data Contributor on.')
param storageAccountId string

@description('Service Bus namespace resource ID to grant Data Owner on.')
param serviceBusNamespaceId string

@description('ACR resource ID to grant AcrPull on.')
param acrId string

var identityName = 'id-vrbook-${env}'

// Built-in role definition IDs (constant across tenants).
var roles = {
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  storageBlobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  serviceBusDataOwner: '090c5cfd-751d-490a-894a-3ce6f1109419'
  acrPull: '7f951dda-4ed3-4680-a7ca-43fe172d538d' // built-in AcrPull role definition GUID
}

resource mi 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: identityName
  location: location
  tags: tags
}

// We split scopes so each assignment targets the right resource.
resource kv 'Microsoft.KeyVault/vaults@2024-04-01-preview' existing = {
  name: last(split(keyVaultId, '/'))
}

resource sa 'Microsoft.Storage/storageAccounts@2024-01-01' existing = {
  name: last(split(storageAccountId, '/'))
}

resource sbns 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: last(split(serviceBusNamespaceId, '/'))
}

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' existing = {
  name: last(split(acrId, '/'))
}

resource kvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: kv
  name: guid(kv.id, mi.id, roles.keyVaultSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
    principalId: mi.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource blobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sa
  name: guid(sa.id, mi.id, roles.storageBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataContributor)
    principalId: mi.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource sbRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: sbns
  name: guid(sbns.id, mi.id, roles.serviceBusDataOwner)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusDataOwner)
    principalId: mi.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource acrRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr
  name: guid(acr.id, mi.id, roles.acrPull)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
    principalId: mi.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

output id string = mi.id
output name string = mi.name
output clientId string = mi.properties.clientId
output principalId string = mi.properties.principalId
