// alerts.bicep — 8 alert rules per ADR 0010. Five are live today; three are
// placeholders that activate when their owning agent ships (A4.1 SLA, A6 Sync,
// A9 Notifications). Placeholder queries are written so they evaluate to zero
// today (no source data) and don't fire false positives. Severity scale:
//   0=Critical, 1=Error, 2=Warning, 3=Informational, 4=Verbose
// See EXECUTION_PLAN.md §4.2 A0.1.7 and BookingApp_Proposal.md §15.

@description('Environment short name.')
param env string

@description('Azure region.')
param location string

@description('Common resource tags.')
param tags object = {
  env: env
  app: 'vrbook'
  costCenter: 'product'
}

@description('Resource ID of Log Analytics workspace (used as scope for query alerts).')
param workspaceId string

@description('Resource ID of the Postgres Flexible Server (metric scope).')
param postgresId string

@description('Resource ID of Redis cache (metric scope). Pass empty when Redis is not deployed.')
param redisId string = ''

@description('Resource ID of the action group all alerts route to.')
param actionGroupId string

// ---------- 1/8 — 5xx spike on API ----------
resource alert5xxSpike 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-vrbook-${env}-5xx-spike'
  location: location
  tags: tags
  properties: {
    displayName: 'API 5xx rate > 1% over 10m'
    description: 'Spike in server errors. Likely deploy regression or downstream outage. Sev2 — page on-call.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT10M'
    scopes: [ workspaceId ]
    criteria: {
      allOf: [
        {
          query: 'AppRequests | where ResultCode startswith "5" | summarize failures = count(), total = count() by bin(TimeGenerated, 5m) | extend rate = 100.0 * failures / total | where rate > 1'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [ actionGroupId ] }
  }
}

// ---------- 2/8 — P95 handler latency > 1s ----------
resource alertP95Latency 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-vrbook-${env}-p95-latency'
  location: location
  tags: tags
  properties: {
    displayName: 'Any MediatR handler P95 > 1000ms over 10m'
    description: 'Handler exceeding SLO. Triage with the slow-handlers-p95 KQL. Sev3.'
    severity: 3
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT10M'
    scopes: [ workspaceId ]
    criteria: {
      allOf: [
        {
          query: 'ContainerAppConsoleLogs_CL | where Log_s startswith \'{"@t"\' | extend e = parse_json(Log_s) | where tostring(e[\'@mt\']) startswith "Handled " | extend handler = tostring(e.RequestName), elapsed = toint(e.ElapsedMs) | where isnotempty(handler) | summarize p95 = percentile(elapsed, 95) by handler | where p95 > 1000'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: { numberOfEvaluationPeriods: 2, minFailingPeriodsToAlert: 2 }
        }
      ]
    }
    actions: { actionGroups: [ actionGroupId ] }
  }
}

// ---------- 3/8 — Postgres CPU sustained > 80% ----------
resource alertPostgresCpu 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-vrbook-${env}-postgres-cpu'
  location: 'global'
  tags: tags
  properties: {
    description: 'Postgres CPU > 80% for 10 minutes. Triage with postgres-cpu-high runbook. Sev2.'
    severity: 2
    enabled: true
    scopes: [ postgresId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT10M'
    targetResourceType: 'Microsoft.DBforPostgreSQL/flexibleServers'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'pg-cpu'
          metricNamespace: 'Microsoft.DBforPostgreSQL/flexibleServers'
          metricName: 'cpu_percent'
          operator: 'GreaterThan'
          threshold: 80
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupId } ]
  }
}

// ---------- 4/8 — Redis evictions detected ----------
// Only created when Redis is deployed. The Sync hold and search caching depend on this.
resource alertRedisEvictions 'Microsoft.Insights/metricAlerts@2018-03-01' = if (!empty(redisId)) {
  name: 'alert-vrbook-${env}-redis-evictions'
  location: 'global'
  tags: tags
  properties: {
    description: 'Redis evicting keys — capacity pressure. Triage with redis-evictions runbook. Sev3.'
    severity: 3
    enabled: true
    scopes: [ redisId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    targetResourceType: 'Microsoft.Cache/Redis'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'evictions'
          metricNamespace: 'Microsoft.Cache/Redis'
          metricName: 'evictedkeys'
          operator: 'GreaterThan'
          threshold: 0
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupId } ]
  }
}

// ---------- 5/8 — Stripe webhook failure burst ----------
resource alertWebhookFail 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-vrbook-${env}-webhook-fail'
  location: location
  tags: tags
  properties: {
    displayName: 'Stripe webhook 5xx burst (>5 in 5m)'
    description: 'Webhook 5xx burst — runbook payment-webhook-failure. Sev2.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    scopes: [ workspaceId ]
    criteria: {
      allOf: [
        {
          query: 'AppRequests | where Url contains "/api/v1/payments/webhooks/stripe" | where ResultCode startswith "5" | count'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 5
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [ actionGroupId ] }
  }
}

// ---------- 6/8 — SLA worker silent (A4.1 placeholder) ----------
// Fires if no booking confirmations through the SLA path in 6h.
// Placeholder until A4.1 ships the SLA worker; intentionally enabled:false so it
// doesn't cry wolf during the window where the worker exists but has no SLA path
// to exercise.
resource alertSlaWorkerSilent 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-vrbook-${env}-sla-silent'
  location: location
  tags: tags
  properties: {
    displayName: '[A4.1 placeholder] SLA worker silent for 6h'
    description: 'No SLA-driven booking confirmations in 6h. Disabled until A4.1 ships. Sev3.'
    severity: 3
    enabled: false
    evaluationFrequency: 'PT30M'
    windowSize: 'PT6H'
    scopes: [ workspaceId ]
    criteria: {
      allOf: [
        {
          query: 'ContainerAppConsoleLogs_CL | where Log_s startswith \'{"@t"\' | extend e = parse_json(Log_s) | where tostring(e.SourceContext) startswith "VrBook.Workers.Booking" | count'
          timeAggregation: 'Count'
          operator: 'Equal'
          threshold: 0
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [ actionGroupId ] }
  }
}

// ---------- 7/8 — Sync feed stale (A6 placeholder) ----------
resource alertSyncStale 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-vrbook-${env}-sync-stale'
  location: location
  tags: tags
  properties: {
    displayName: '[A6 placeholder] iCal feed stale > 2x interval'
    description: 'AirBnB feed not polled successfully. Disabled until A6 ships. Sev3.'
    severity: 3
    enabled: false
    evaluationFrequency: 'PT15M'
    windowSize: 'PT2H'
    scopes: [ workspaceId ]
    criteria: {
      allOf: [
        {
          query: 'ContainerAppConsoleLogs_CL | where Log_s startswith \'{"@t"\' | extend e = parse_json(Log_s) | where tostring(e.SourceContext) startswith "VrBook.Modules.Sync" | where tostring(e[\'@mt\']) contains "poll succeeded" | count'
          timeAggregation: 'Count'
          operator: 'Equal'
          threshold: 0
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [ actionGroupId ] }
  }
}

// ---------- 8/8 — Notification template render failure (A9 placeholder) ----------
resource alertTemplateFail 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-vrbook-${env}-template-fail'
  location: location
  tags: tags
  properties: {
    displayName: '[A9 placeholder] Mustache template render failure'
    description: 'Notification template failed to render. Disabled until A9 ships. Sev2.'
    severity: 2
    enabled: false
    evaluationFrequency: 'PT5M'
    windowSize: 'PT10M'
    scopes: [ workspaceId ]
    criteria: {
      allOf: [
        {
          query: 'ContainerAppConsoleLogs_CL | where Log_s startswith \'{"@t"\' | extend e = parse_json(Log_s) | where tostring(e.SourceContext) startswith "VrBook.Workers.Notifications" | where tostring(e[\'@l\']) == "Error" | where tostring(e[\'@m\']) contains "template" | count'
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [ actionGroupId ] }
  }
}
