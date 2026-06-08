// action-group.bicep — single Azure Monitor action group for alert routing.
// Recipients are intentionally empty in Bicep so they can be configured per
// environment in the portal (or via per-env Bicep params later). Alert rules
// reference this group's resource ID; flipping a recipient on/off does NOT
// require a Bicep deploy.

@description('Environment short name.')
param env string

@description('Resource group region (resource group is global; param is for tagging).')
param location string = 'global'

@description('Common resource tags.')
param tags object = {
  env: env
  app: 'vrbook'
  costCenter: 'product'
}

resource ag 'Microsoft.Insights/actionGroups@2023-09-01-preview' = {
  name: 'ag-vrbook-${env}'
  location: location
  tags: tags
  properties: {
    groupShortName: 'vrbook${take(env, 3)}'
    enabled: true
    emailReceivers: []
    smsReceivers: []
    webhookReceivers: []
    azureAppPushReceivers: []
  }
}

output id string = ag.id
output name string = ag.name
