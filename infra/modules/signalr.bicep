// signalr.bicep — Azure SignalR Service, Serverless mode (per §15.1).
// Standard 1 unit. Used for real-time messaging between guest/owner.
// In Serverless mode, the API doesn't host SignalR hubs — clients negotiate against
// the API which returns a SignalR Service access token.

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

@description('Capacity in units.')
param capacity int = 1

@description('Key Vault name to write the connection string into (signalr-cs secret). See SLICE7_PLAN §2.7.')
param keyVaultName string

var sigrName = 'sr-vrbook-${env}'

resource sigr 'Microsoft.SignalRService/signalR@2024-03-01' = {
  name: sigrName
  location: location
  tags: tags
  sku: {
    name: 'Standard_S1'
    tier: 'Standard'
    capacity: capacity
  }
  kind: 'SignalR'
  properties: {
    features: [
      {
        flag: 'ServiceMode'
        value: 'Serverless'
      }
      {
        flag: 'EnableConnectivityLogs'
        value: 'true'
      }
      {
        flag: 'EnableMessagingLogs'
        value: 'false'
      }
    ]
    cors: {
      allowedOrigins: [
        '*'
      ]
    }
    tls: {
      clientCertEnabled: false
    }
    publicNetworkAccess: 'Enabled'
    disableLocalAuth: false
  }
}

// Slice 7 — write the connection string into KV automatically. Replaces the
// 'pending-bicep-deploy' placeholder that 10-store-secrets.ps1 seeds at
// provision time. Mirrors the ACS pattern (infra/modules/acs.bicep:72-79).
resource kv 'Microsoft.KeyVault/vaults@2024-04-01-preview' existing = {
  name: keyVaultName
}

resource signalrSecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: kv
  name: 'signalr-cs'
  properties: {
    value: sigr.listKeys().primaryConnectionString
    contentType: 'text/plain'
  }
}

output id string = sigr.id
output name string = sigr.name
output hostName string = sigr.properties.hostName
output connectionAuthName string = 'AccessKey' // placeholder name; KV holds the actual connection string per §23.3
