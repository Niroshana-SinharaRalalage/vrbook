// log-analytics.bicep — Log Analytics Workspace for App Insights + Container Apps logs.
// PerGB2018 SKU.
//
// Slice OPS.INFRA.2 (2026-07-09) — staging right-sizing:
//   * Retention 90d → 31d (staging/dev). Days > 31 are billed at $0.12/GB/mo.
//   * Daily quota cap: -1 (unlimited) → 0.5 GB/day for staging/dev.
//     At Azure's PerGB2018 free-tier of 5 GB/mo per workspace, staging + dev
//     should stay $0 as long as ingestion doesn't spike. The cap is a hard
//     ceiling — ingest is silently dropped once tripped for the day, so pick
//     a headroom-friendly limit rather than the free-tier exact edge.
//   * Prod stays 90d retention + no cap (unchanged).

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

@description('Retention in days. Prod = 90 (compliance / audit), staging/dev = 31 (stay under the free-tier included-retention band).')
param retentionInDays int = env == 'prod' ? 90 : 31

@description('Daily ingest cap in GB. -1 = unlimited. Prod = -1, staging/dev = 0.5 (stay under the 5 GB/mo free tier).')
param dailyQuotaGb int = env == 'prod' ? -1 : 1

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
      dailyQuotaGb: dailyQuotaGb
    }
  }
}

output id string = workspace.id
output name string = workspace.name
output customerId string = workspace.properties.customerId
