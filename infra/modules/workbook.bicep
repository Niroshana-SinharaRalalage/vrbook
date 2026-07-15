// workbook.bicep — Azure Monitor Workbook for VrBook operational dashboard.
// Six sections: Errors, Booking Funnel, Payments, Sync Health (placeholder),
// Messaging (placeholder), Infrastructure. Queries mirror docs/observability/queries.kql.
// See EXECUTION_PLAN.md §4.2 A0.1.6.

@description('Environment short name (dev | staging | prod).')
param env string

@description('Azure region.')
param location string

@description('Resource ID of the Log Analytics workspace.')
param workspaceId string

@description('VRB-306 — Postgres Flexible Server resource ID (for the DB metric tile).')
param postgresId string = ''

@description('VRB-306 — API Container App resource ID (for the replica/restart metric tile).')
param apiAppId string = ''

@description('Common resource tags.')
param tags object = {
  env: env
  app: 'vrbook'
  costCenter: 'product'
}

var workbookContent = {
  version: 'Notebook/1.0'
  items: [
    {
      type: 1
      content: {
        json: '# VrBook Operations — ${env}\n\nLogs flow from Container Apps stdout → Log Analytics via the platform integration. Serilog writes CLEF JSON; every property is queryable as a column. Full query reference: `docs/observability/queries.kql`.'
      }
    }
    {
      type: 1
      content: {
        json: '## Errors (last 1h, grouped by source + template)'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'let parsed = ContainerAppConsoleLogs_CL | where TimeGenerated > ago(1h) | where Log_s startswith \'{"@t"\' | extend e = parse_json(Log_s); parsed | where tostring(e[\'@l\']) == "Error" | extend source = tostring(e.SourceContext), template = tostring(e[\'@mt\']) | summarize count_ = count(), latest = max(TimeGenerated) by source, template | order by count_ desc'
        size: 0
        timeContextFromParameter: 'TimeRange'
        queryType: 0
        resourceType: 'microsoft.operationalinsights/workspaces'
        visualization: 'table'
      }
    }
    {
      type: 1
      content: {
        json: '## Booking Funnel (today UTC)'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'let parsed = ContainerAppConsoleLogs_CL | where TimeGenerated > startofday(now()) | where Log_s startswith \'{"@t"\' | extend e = parse_json(Log_s); parsed | where tostring(e[\'@mt\']) startswith "Handled " | extend handler = tostring(e.RequestName) | where handler in ("PlaceBookingCommand","ConfirmBookingCommand","CancelBookingCommand","CheckInBookingCommand","CheckOutBookingCommand","RejectBookingCommand") | summarize count_ = count() by handler | order by handler asc'
        size: 0
        queryType: 0
        resourceType: 'microsoft.operationalinsights/workspaces'
        visualization: 'barchart'
      }
    }
    {
      type: 1
      content: {
        json: '## Payments — failures last 24h'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'let parsed = ContainerAppConsoleLogs_CL | where TimeGenerated > ago(1d) | where Log_s startswith \'{"@t"\' | extend e = parse_json(Log_s); parsed | where tostring(e.RequestPath) startswith "/api/v1/payments" or tostring(e.RequestName) in ("CreatePaymentIntentForBookingCommand","CapturePaymentIntentForBookingCommand","RefundForBookingCommand") | where tostring(e[\'@l\']) in ("Warning","Error") | project TimeGenerated, level = tostring(e[\'@l\']), handler = tostring(e.RequestName), msg = tostring(e[\'@m\']) | order by TimeGenerated desc | take 50'
        size: 0
        queryType: 0
        resourceType: 'microsoft.operationalinsights/workspaces'
        visualization: 'table'
      }
    }
    {
      type: 1
      content: {
        json: '## Sync Health (placeholder — populated by A6 Sync)'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'print msg = "A6 Sync not deployed yet. After A6 ships this section will show feed poll health, conflicts detected, and last-success-at per AirBnB feed."'
        size: 0
        queryType: 0
        resourceType: 'microsoft.operationalinsights/workspaces'
        visualization: 'table'
      }
    }
    {
      type: 1
      content: {
        json: '## Messaging (placeholder — populated by A7 Messaging)'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'print msg = "A7 Messaging not deployed yet. After A7 ships this section will show active SignalR connections and offline-fallback events published."'
        size: 0
        queryType: 0
        resourceType: 'microsoft.operationalinsights/workspaces'
        visualization: 'table'
      }
    }
    {
      type: 1
      content: {
        json: '## Infrastructure — slowest handlers (P95) last 1h'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'let parsed = ContainerAppConsoleLogs_CL | where TimeGenerated > ago(1h) | where Log_s startswith \'{"@t"\' | extend e = parse_json(Log_s); parsed | where tostring(e[\'@mt\']) startswith "Handled " | extend handler = tostring(e.RequestName), elapsed = toint(e.ElapsedMs) | where isnotempty(handler) | summarize p50 = percentile(elapsed, 50), p95 = percentile(elapsed, 95), p99 = percentile(elapsed, 99), count_ = count() by handler | order by p95 desc | take 25'
        size: 0
        queryType: 0
        resourceType: 'microsoft.operationalinsights/workspaces'
        visualization: 'table'
      }
    }
    // ---- VRB-306 tiles ----
    {
      type: 1
      content: {
        json: '## Traffic & latency (request-level, last 1h)'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'AppRequests | where TimeGenerated > ago(1h) | summarize requests = count() by bin(TimeGenerated, 5m) | order by TimeGenerated asc'
        size: 0
        queryType: 0
        resourceType: 'microsoft.operationalinsights/workspaces'
        visualization: 'timechart'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'AppRequests | where TimeGenerated > ago(1h) | summarize p50 = percentile(DurationMs, 50), p95 = percentile(DurationMs, 95), p99 = percentile(DurationMs, 99), total = count(), errors5xx = countif(ResultCode startswith "5"), errorRatePct = round(100.0 * countif(ResultCode startswith "5") / count(), 2)'
        size: 0
        queryType: 0
        resourceType: 'microsoft.operationalinsights/workspaces'
        visualization: 'table'
      }
    }
    {
      type: 1
      content: {
        json: '## Stripe webhook success % (last 24h)'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'AppRequests | where TimeGenerated > ago(1d) | where Url contains "/api/v1/payments/webhooks/stripe" | summarize total = count(), succeeded = countif(ResultCode startswith "2"), successPct = round(100.0 * countif(ResultCode startswith "2") / count(), 1)'
        size: 0
        queryType: 0
        resourceType: 'microsoft.operationalinsights/workspaces'
        visualization: 'table'
      }
    }
    {
      type: 1
      content: {
        json: '## Notification dispatch health (sent / failed / dead-lettered, last 6h)'
      }
    }
    {
      type: 3
      content: {
        version: 'KqlItem/1.0'
        query: 'ContainerAppConsoleLogs_CL | where TimeGenerated > ago(6h) | where Log_s startswith \'{"@t"\' | extend e = parse_json(Log_s) | where tostring(e[\'@mt\']) startswith "Notification dispatch complete" | extend sent = toint(e.Sent), failed = toint(e.Failed), dead = toint(e.DeadLettered) | summarize sent = sum(sent), failed = sum(failed), deadLettered = sum(dead) by bin(TimeGenerated, 15m) | order by TimeGenerated asc'
        size: 0
        queryType: 0
        resourceType: 'microsoft.operationalinsights/workspaces'
        visualization: 'timechart'
      }
    }
    {
      type: 1
      content: {
        json: '## Postgres — CPU % / active connections / storage % (last 1h)'
      }
    }
    {
      type: 10
      content: {
        version: 'MetricsItem/2.0'
        size: 0
        chartType: 2
        resourceType: 'microsoft.dbforpostgresql/flexibleservers'
        metricScope: 0
        resourceIds: [ postgresId ]
        timeContext: { durationMs: 3600000 }
        metrics: [
          {
            namespace: 'microsoft.dbforpostgresql/flexibleservers'
            metric: 'microsoft.dbforpostgresql/flexibleservers--cpu_percent'
            aggregation: 4
          }
          {
            namespace: 'microsoft.dbforpostgresql/flexibleservers'
            metric: 'microsoft.dbforpostgresql/flexibleservers--active_connections'
            aggregation: 4
          }
          {
            namespace: 'microsoft.dbforpostgresql/flexibleservers'
            metric: 'microsoft.dbforpostgresql/flexibleservers--storage_percent'
            aggregation: 4
          }
        ]
      }
    }
    {
      type: 1
      content: {
        json: '## API Container App — replica count & restarts (last 1h)'
      }
    }
    {
      type: 10
      content: {
        version: 'MetricsItem/2.0'
        size: 0
        chartType: 2
        resourceType: 'microsoft.app/containerapps'
        metricScope: 0
        resourceIds: [ apiAppId ]
        timeContext: { durationMs: 3600000 }
        metrics: [
          {
            namespace: 'microsoft.app/containerapps'
            metric: 'microsoft.app/containerapps--Replicas'
            aggregation: 4
          }
          {
            namespace: 'microsoft.app/containerapps'
            metric: 'microsoft.app/containerapps--RestartCount'
            aggregation: 1
          }
        ]
      }
    }
  ]
  styleSettings: {}
  '$schema': 'https://github.com/Microsoft/Application-Insights-Workbooks/blob/master/schema/workbook.json'
}

resource workbook 'Microsoft.Insights/workbooks@2023-06-01' = {
  // Workbook 'name' must be a GUID (deterministic so re-deploys update in place).
  name: guid(resourceGroup().id, 'vrbook-ops', env)
  location: location
  kind: 'shared'
  tags: tags
  properties: {
    displayName: 'VrBook Operations — ${env}'
    serializedData: string(workbookContent)
    category: 'workbook'
    sourceId: workspaceId
  }
}

output id string = workbook.id
output name string = workbook.name
