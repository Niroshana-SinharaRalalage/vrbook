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

@description('''VRB-306 — alert recipient email (the on-call address). Kept a
per-env parameter (never hard-coded) so a recipient change does not require a
code edit. Empty ⇒ no email receiver is wired and recipients stay
portal-managed. Set in the env .bicepparam (staging: the owner on-call address).''')
param alertEmail string = ''

resource ag 'Microsoft.Insights/actionGroups@2023-09-01-preview' = {
  name: 'ag-vrbook-${env}'
  location: location
  tags: tags
  properties: {
    groupShortName: 'vrbook${take(env, 3)}'
    enabled: true
    // VRB-306 — wire the on-call email when provided; useCommonAlertSchema so
    // every alert (metric + scheduled-query) renders a consistent payload.
    emailReceivers: empty(alertEmail) ? [] : [
      {
        name: 'oncall'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
    smsReceivers: []
    webhookReceivers: []
    azureAppPushReceivers: []
  }
}

output id string = ag.id
output name string = ag.name
