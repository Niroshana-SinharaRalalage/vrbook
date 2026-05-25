// log-analytics.bicep — Log Analytics Workspace for App Insights + Container Apps logs.
// PerGB2018 SKU with 90-day retention per proposal §15.1.

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

@description('Retention in days (proposal §15.1: 90 days).')
param retentionInDays int = 90

var workspaceName = 'law-vrbook-${env}'

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    workspaceCapping: {
      dailyQuotaGb: -1
    }
  }
}

output id string = workspace.id
output name string = workspace.name
output customerId string = workspace.properties.customerId
