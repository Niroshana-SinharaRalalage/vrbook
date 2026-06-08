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
