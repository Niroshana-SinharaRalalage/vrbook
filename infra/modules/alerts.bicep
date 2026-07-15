// alerts.bicep — Azure Monitor alert rules per ADR 0010.
// Original 8 (5 live + 3 A4.1/A6/A9 placeholders). VRB-306 adds the go-live set
// with real thresholds (as params), a named owner + runbook link per alert, and
// a synthetic search→quote availability test:
//   9  request-level API P95 > 1s (5m)          — api-5xx-spike
//   10 Stripe webhook signature-verify fail (400) — payment-webhook-failure
//   11 notification dispatch failures             — notification-dispatch-failures
//   12 notification worker silent (not draining)  — notification-dispatch-failures
//   13 DB migrator Job failure                    — migrator-job-failure
//   14 synthetic availability (search) failing    — api-5xx-spike
// Severity scale: 0=Critical, 1=Error, 2=Warning, 3=Informational, 4=Verbose.
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

// ---- VRB-306 params ----
@description('Resource ID of the Application Insights component (availability-test scope).')
param appInsightsId string = ''

@description('Base URL of the API for the synthetic availability test (e.g. https://<api-fqdn>). Empty ⇒ the availability test + its alert are skipped.')
param apiBaseUrl string = ''

@description('Named owner label recorded as a tag on every VRB-306 alert (the on-call). The email itself lives on the action group.')
param alertOwner string = 'on-call'

@description('Request-level API P95 latency threshold, milliseconds (PRD SLO = 1000).')
param p95LatencyMs int = 1000

@description('Postgres CPU alert threshold, percent.')
param pgCpuPct int = 80

@description('Stripe webhook signature-verify (HTTP 400) failure count over 5m that trips the alert.')
param webhookSigFailThreshold int = 1

@description('Notification dispatch failures (failed + dead-lettered) over 15m that trips the alert.')
param notifDrainFailThreshold int = 0

// Owner + runbook stamped on each VRB-306 alert so the on-call and its triage
// doc are discoverable straight from the alert resource.
var ownerTag = { owner: alertOwner }

// ---------- 1/8 — 5xx spike on API ----------
resource alert5xxSpike 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-vrbook-${env}-5xx-spike'
  location: location
  tags: union(tags, ownerTag, { runbook: 'api-5xx-spike' })
  properties: {
    displayName: 'API 5xx rate > 1% over 10m'
    description: 'Spike in server errors. Likely deploy regression or downstream outage. Sev2 — page on-call. Owner: ${alertOwner}. Runbook: docs/runbooks/api-5xx-spike.md'
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
          // For numberOfEvaluationPeriods > 1, Azure requires the query to project
          // a TimeGenerated column. We bin by 5m so the summarize keeps it.
          query: 'ContainerAppConsoleLogs_CL | where Log_s startswith \'{"@t"\' | extend e = parse_json(Log_s) | where tostring(e[\'@mt\']) startswith "Handled " | extend handler = tostring(e.RequestName), elapsed = toint(e.ElapsedMs) | where isnotempty(handler) | summarize p95 = percentile(elapsed, 95) by handler, bin(TimeGenerated, 5m) | where p95 > 1000'
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
  tags: union(tags, ownerTag, { runbook: 'postgres-cpu-high' })
  properties: {
    description: 'Postgres CPU > ${pgCpuPct}% for 10 minutes. Owner: ${alertOwner}. Runbook: docs/runbooks/postgres-cpu-high.md. Sev2.'
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
          threshold: pgCpuPct
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

// ====================================================================
// VRB-306 — go-live alert set (real thresholds · named owner · runbook)
// ====================================================================

// ---------- 9 — request-level API P95 > threshold (5m) ----------
// Complements the handler-P95 rule: this is the request-level SLO (PRD §8),
// which also captures middleware/auth/queueing time the handler timer misses.
resource alertRequestP95 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-vrbook-${env}-request-p95'
  location: location
  tags: union(tags, ownerTag, { runbook: 'api-5xx-spike' })
  properties: {
    displayName: 'API request P95 > ${p95LatencyMs}ms over 5m'
    description: 'Request-level P95 latency over the PRD SLO (1s). Owner: ${alertOwner}. Runbook: docs/runbooks/api-5xx-spike.md. Sev3.'
    severity: 3
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    scopes: [ workspaceId ]
    criteria: {
      allOf: [
        {
          query: 'AppRequests | summarize p95 = percentile(DurationMs, 95) by bin(TimeGenerated, 5m) | where p95 > ${p95LatencyMs}'
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

// ---------- 10 — Stripe webhook signature-verify failure (HTTP 400) ----------
// A bad signature returns 400 (not 5xx), so the 5xx-burst rule (5/8) misses it.
// A run of 400s means a wrong signing secret after a rotation, or a spoof probe.
resource alertWebhookSigFail 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-vrbook-${env}-webhook-sig-fail'
  location: location
  tags: union(tags, ownerTag, { runbook: 'payment-webhook-failure' })
  properties: {
    displayName: 'Stripe webhook signature-verify failures (HTTP 400) >= ${webhookSigFailThreshold} in 5m'
    description: 'Bad-signature webhook posts return 400. A run implies a wrong webhook signing secret (post-rotation) or a spoof attempt. Owner: ${alertOwner}. Runbook: docs/runbooks/payment-webhook-failure.md. Sev2.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    scopes: [ workspaceId ]
    criteria: {
      allOf: [
        {
          query: 'AppRequests | where Url contains "/api/v1/payments/webhooks/stripe" | where ResultCode == "400" | count'
          timeAggregation: 'Count'
          operator: 'GreaterThanOrEqual'
          threshold: webhookSigFailThreshold
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: [ actionGroupId ] }
  }
}

// ---------- 11 — notification dispatch failures ----------
// Reads the worker's per-tick structured log:
//   "Notification dispatch complete. ... failed={Failed} deadLettered={DeadLettered}"
// Sum of failed + dead-lettered over 15m > threshold ⇒ guests may miss emails.
resource alertNotifDispatchFail 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-vrbook-${env}-notif-dispatch-fail'
  location: location
  tags: union(tags, ownerTag, { runbook: 'notification-dispatch-failures' })
  properties: {
    displayName: 'Notification dispatch failures (failed+dead-lettered > ${notifDrainFailThreshold}) over 15m'
    description: 'The dispatch worker reported failed/dead-lettered notifications; booking emails may not be delivered. Owner: ${alertOwner}. Runbook: docs/runbooks/notification-dispatch-failures.md. Sev2.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    scopes: [ workspaceId ]
    criteria: {
      allOf: [
        {
          query: 'ContainerAppConsoleLogs_CL | where Log_s startswith \'{"@t"\' | extend e = parse_json(Log_s) | where tostring(e[\'@mt\']) startswith "Notification dispatch complete" | extend failed = toint(e.Failed), dead = toint(e.DeadLettered) | summarize total = sum(failed) + sum(dead) by bin(TimeGenerated, 5m) | where total > ${notifDrainFailThreshold}'
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

// ---------- 12 — notification worker silent (queue not draining) ----------
// The dispatch job runs on cron and logs every tick even with nothing to send.
// Zero "dispatch complete" lines in 15m ⇒ the job isn't firing ⇒ Queued rows
// pile up undrained. This is the "drain lag" signal without a custom gauge.
resource alertNotifWorkerSilent 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-vrbook-${env}-notif-worker-silent'
  location: location
  tags: union(tags, ownerTag, { runbook: 'notification-dispatch-failures' })
  properties: {
    displayName: 'Notification worker silent (no dispatch tick) for 15m'
    description: 'No "Notification dispatch complete" line in 15m — the cron job is not running, so Queued notifications are not draining. Owner: ${alertOwner}. Runbook: docs/runbooks/notification-dispatch-failures.md. Sev2.'
    severity: 2
    enabled: true
    evaluationFrequency: 'PT15M'
    windowSize: 'PT15M'
    scopes: [ workspaceId ]
    criteria: {
      allOf: [
        {
          query: 'ContainerAppConsoleLogs_CL | where Log_s startswith \'{"@t"\' | extend e = parse_json(Log_s) | where tostring(e[\'@mt\']) startswith "Notification dispatch complete" | count'
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

// ---------- 13 — DB migrator Job failure ----------
// The migrator job (caj-vrbook-migrator-${env}) always ends with a design-time
// HostAbortedException by design — that is NOT a failure. A real failure logs an
// Error/Fatal from the migrator or EF Core that is not that abort.
resource alertMigratorFail 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-vrbook-${env}-migrator-fail'
  location: location
  tags: union(tags, ownerTag, { runbook: 'migrator-job-failure' })
  properties: {
    displayName: 'DB migrator job error (non-benign) in last 1h'
    description: 'The migrator job logged a non-HostAborted Error/Fatal — a migration likely failed and the deploy is unhealthy. Owner: ${alertOwner}. Runbook: docs/runbooks/migrator-job-failure.md. Sev1.'
    severity: 1
    enabled: true
    evaluationFrequency: 'PT15M'
    windowSize: 'PT1H'
    scopes: [ workspaceId ]
    criteria: {
      allOf: [
        {
          query: 'ContainerAppConsoleLogs_CL | where Log_s startswith \'{"@t"\' | extend e = parse_json(Log_s) | where tostring(e.SourceContext) startswith "VrBook.Migrator" or tostring(e.SourceContext) startswith "Microsoft.EntityFrameworkCore" | where tostring(e[\'@l\']) in ("Error", "Fatal") | where tostring(e[\'@mt\']) !contains "HostAborted" and tostring(e.ExceptionDetail) !contains "HostAbortedException" | count'
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

// ---------- 14 — synthetic search availability test + its alert ----------
// The public property-search GET is the head of the search→quote funnel. A
// standard web test can only exercise the GET leg (the quote POST would need a
// multi-step/custom TrackAvailability test = app code, out of this lane) — see
// docs/runbooks/api-5xx-spike.md + the PR notes. Skipped unless both the API URL
// and the App Insights id are supplied (so dev, which passes neither, no-ops).
resource searchAvailabilityTest 'Microsoft.Insights/webtests@2022-06-15' = if (!empty(apiBaseUrl) && !empty(appInsightsId)) {
  name: 'webtest-vrbook-${env}-search'
  location: location
  tags: union(tags, ownerTag, {
    'hidden-link:${appInsightsId}': 'Resource'
  })
  kind: 'standard'
  properties: {
    SyntheticMonitorId: 'webtest-vrbook-${env}-search'
    Name: 'API search availability — ${env}'
    Description: 'VRB-306 funnel head: GET the public property-search endpoint every 5m from 2 regions.'
    Enabled: true
    Frequency: 300
    Timeout: 30
    Kind: 'standard'
    RetryEnabled: true
    Locations: [
      { Id: 'us-va-ash-azr' }
      { Id: 'us-il-ch1-azr' }
    ]
    Request: {
      RequestUrl: '${apiBaseUrl}/api/v1/properties'
      HttpVerb: 'GET'
      ParseDependentRequests: false
    }
    ValidationRules: {
      ExpectedHttpStatusCode: 200
      SSLCheck: true
      SSLCertRemainingLifetimeCheck: 7
    }
  }
}

resource alertAvailability 'Microsoft.Insights/metricAlerts@2018-03-01' = if (!empty(apiBaseUrl) && !empty(appInsightsId)) {
  name: 'alert-vrbook-${env}-availability-search'
  location: 'global'
  tags: union(tags, ownerTag, { runbook: 'api-5xx-spike' })
  properties: {
    description: 'Synthetic search availability failing from >=1 location — the API/search path is unreachable. Owner: ${alertOwner}. Runbook: docs/runbooks/api-5xx-spike.md. Sev1.'
    severity: 1
    enabled: true
    scopes: [ searchAvailabilityTest.id, appInsightsId ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.WebtestLocationAvailabilityCriteria'
      webTestId: searchAvailabilityTest.id
      componentId: appInsightsId
      failedLocationCount: 1
    }
    actions: [ { actionGroupId: actionGroupId } ]
  }
}
