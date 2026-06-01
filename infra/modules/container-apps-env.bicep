// container-apps-env.bicep — Workload-profile Container Apps Environment, VNet-integrated.
// Per proposal §15.1: Consumption + Dedicated D4 workload profiles.
// internal=false (external ingress) — Front Door fronts the API in prod; dev/staging
// reach the API directly via the env default domain.

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

@description('Resource ID of the apps subnet (must be delegated to Microsoft.App/environments).')
param infrastructureSubnetId string

@description('Log Analytics customer id (GUID) for diagnostic stream.')
param logAnalyticsCustomerId string

@description('Log Analytics workspace shared key — sourced via listKeys() on the workspace.')
@secure()
param logAnalyticsSharedKey string

@description('Whether the Dedicated D4 profile should be present (prod = yes for steady-state, dev = consumption only to control cost).')
param includeDedicatedProfile bool = false

var envName = 'cae-vrbook-${env}'

var workloadProfiles = includeDedicatedProfile ? [
  {
    name: 'Consumption'
    workloadProfileType: 'Consumption'
  }
  {
    name: 'd4'
    workloadProfileType: 'D4'
    minimumCount: 1
    maximumCount: 3
  }
] : [
  {
    name: 'Consumption'
    workloadProfileType: 'Consumption'
  }
]

resource cae 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: envName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey: logAnalyticsSharedKey
      }
    }
    vnetConfiguration: {
      internal: false
      infrastructureSubnetId: infrastructureSubnetId
    }
    workloadProfiles: workloadProfiles
    zoneRedundant: false
  }
}

output id string = cae.id
output name string = cae.name
output defaultDomain string = cae.properties.defaultDomain
output staticIp string = cae.properties.staticIp
