# 10. Observability Stack — Serilog + App Insights + Log Analytics

- Status: Accepted
- Date: 2026-01-15
- Deciders: Solutions Architecture
- Tags: observability, logging, metrics, alerting, pii

## Context and Problem Statement

Phase 1 ships in 8 weeks with no dedicated SRE and a small on-call rotation. The platform's single most important invariant — zero double-bookings — must be observable in production with a small, fast-to-build set of dashboards and alerts. The §17 observability plan is unusually concrete for a proposal: §17.1 specifies Serilog with App Insights sink and a defined PII redaction policy; §17.2 lists 12 custom metrics with dimensions; §17.3 calls for a single Azure Workbook with six sections; §17.4 lists 8 alert rules with severities and channels; §17.5 stubs the on-call playbook.

The §3.3 topology shows Application Insights connected to Log Analytics — workspace-based App Insights, KQL-queryable, integrated with the rest of Azure Monitor. The §15.1 SKU table provisions App Insights workspace-based and Log Analytics PerGB2018 with 90-day retention.

The §14.3 OWASP checklist commits A02 to "no PII in logs (Serilog destructuring filter)" and §17.1 says: "PII filter: `email`, `phone`, `address` redacted at sink level via `IDestructuringPolicy` unless the log scope explicitly opts in (identity ops)." The §14.5 audit-log policy applies the same PII redaction to the append-only audit table.

## Decision Drivers

- **Azure-native stack** — the rest of the platform is on Azure; using Azure Monitor minimises cross-tool context-switching for on-call.
- **One query language for logs and metrics** — KQL on Log Analytics covers both.
- **Structured logging is mandatory** — text-blob logs are unusable at the scale of even a Phase 1 booking funnel.
- **PII redaction at the sink** — not at the call site. Engineers should not have to remember to redact; the pipeline should do it.
- **Distributed-trace correlation** — booking flow touches API + Stripe webhook + Booking Worker + Notification Worker, all separate processes. `traceId` and `correlationId` must thread through.
- **Workbook-as-code** — the §17.3 dashboard must be version-controlled and deployable via Bicep so changes are reviewable, not portal-clicked.
- **Alert rules must be in IaC** — §17.4's 8 alert rules ship with the infra and evolve in PRs.

## Considered Options

- **Serilog → Application Insights → Log Analytics (chosen)** — Structured .NET logging via Serilog, shipped to App Insights, surfaced via Log Analytics workspace.
- **OpenTelemetry → App Insights via OTel exporter** — Vendor-neutral instrumentation, App Insights backend.
- **OpenTelemetry → Self-hosted Grafana + Loki + Tempo + Prometheus** — Fully open-source stack on Container Apps.
- **OpenTelemetry → Datadog / New Relic / Honeycomb** — Commercial APM, vendor-managed.
- **Microsoft.Extensions.Logging defaults → App Insights SDK** — No Serilog; rely on built-in logger.

## Decision Outcome

Chosen option: **"Serilog → Application Insights → Log Analytics"**, because (a) Serilog's structured-property model fits the §17.1 requirement (typed properties `userId`, `traceId`, `bookingId`, `propertyId`, `correlationId`) idiomatically, (b) the `IDestructuringPolicy` extension point is the natural place for the §17.1 PII-redaction policy, (c) App Insights + Log Analytics is the integrated Azure-native option that requires zero additional infrastructure, (d) KQL covers every query and alert pattern the §17 plan needs, and (e) the §23.2 versions table already pins Serilog 4.x and `Sinks.ApplicationInsights`.

### Positive Consequences

- Every log entry is structured JSON with consistent properties. `traceCorrelationId` (`Activity.Current.TraceId`) is automatically attached by App Insights SDK.
- The §17.1 properties (`userId`, `bookingId`, `propertyId`, `correlationId`) are added via `LogContext.PushProperty(...)` in a request-scoped middleware. Every log emitted within the scope inherits them.
- PII destructuring at the sink: a `PiiRedactionDestructuringPolicy` registered on the Serilog logger recognises types `Email`, `PhoneNumber`, `Address` and emits the redacted form (`a***@***.com`, etc.) unless the active `LogContext` contains `Pii.Allow = true` (set by identity-operations endpoints only).
- Workbook (§17.3) authored as a Bicep `Microsoft.Insights/workbooks` resource — the workbook JSON lives in the repo; changes are PRs; deploy via `cd-staging`/`cd-prod`.
- Alert rules (§17.4) as Bicep `Microsoft.Insights/scheduledQueryRules` (KQL-backed) and `Microsoft.Insights/metricAlerts` (metric-backed). Action groups (`on-call`, `email`) are also Bicep resources.
- Distributed tracing: App Insights SDK auto-instruments HttpClient and Service Bus client. The Stripe webhook call carries a `traceparent` header from the API; the Booking Worker receives the trace context from the Service Bus message properties.
- 12 custom metrics from §17.2 emitted via `TelemetryClient.GetMetric(name, dimensionNames...).TrackValue(value, dim1, dim2)` — no SDK shim beyond what App Insights provides.

### Negative Consequences / Trade-offs

- **App Insights pricing is volume-based.** Log Analytics PerGB2018 at 90-day retention is acceptable for Phase 1 traffic but needs revisiting at scale. Mitigated by Serilog `MinimumLevel.Override("Microsoft", LogEventLevel.Warning)` (drop framework noise), sampling of `Information`-level handler entry/exit logs (§17.1), and explicit log-volume budget review at end of W7.
- **Vendor lock-in to App Insights.** Switching to a different APM would require re-writing custom-metric emission and re-authoring the workbook. Mitigated by Serilog being sink-agnostic (a second sink could be added without code change) and by the alert rules being expressible in other KQL-compatible backends. Trade-off accepted: a unified Azure-native stack is worth more than portability we don't need.
- **KQL learning curve.** Engineers used to PromQL or SQL must learn KQL. Mitigated by §17.5 runbooks containing the canonical queries; over time the team builds familiarity.
- **Serilog destructuring filter for PII** is a code path that must be reviewed carefully — a bug here leaks PII to App Insights. Mitigated by integration tests that log entities with PII and assert the emitted log is redacted (the test connects an in-memory Serilog sink and inspects the structured event).

## Pros and Cons of the Options

### Serilog → App Insights → Log Analytics

- Good, because integrated Azure-native stack — no separate ingestion infra.
- Good, because Serilog's `IDestructuringPolicy` is the right hook for PII redaction.
- Good, because KQL covers logs, metrics, traces, and alerting uniformly.
- Good, because Workbooks and alert rules deploy as Bicep — observability as code.
- Good, because §23.2 already pins the libraries.
- Bad, because App Insights pricing scales with volume.
- Bad, because vendor lock-in to App Insights queries.

### OpenTelemetry → App Insights via OTel exporter

- Good, because vendor-neutral instrumentation — same SDK works against any OTel backend.
- Good, because future migration off App Insights is just an exporter swap.
- Bad, because OTel structured-property ergonomics in .NET are less mature than Serilog's — `LogContext.PushProperty` has no clean OTel equivalent today.
- Bad, because PII destructuring is harder — OTel's processor model is more verbose than `IDestructuringPolicy`.
- Bad, because the App Insights OTel exporter doesn't yet support every App Insights feature (custom metrics dimensions, live metrics stream) at full parity.
- Acceptable trade-off in a different time horizon; not for Phase 1.

### OpenTelemetry → self-hosted Grafana + Loki + Tempo + Prometheus

- Good, because fully open source, no per-GB pricing.
- Good, because PromQL + LogQL are mature query languages.
- Bad, because operating that stack ourselves (storage, retention, HA, upgrades) is a full SRE workstream — exactly what we don't have headcount for.
- Bad, because we'd be running infrastructure to monitor our infrastructure — a known recursive failure mode at our staffing level.

### OpenTelemetry → Datadog / New Relic / Honeycomb

- Good, because best-in-class observability UX and powerful query languages.
- Good, because no Azure-side observability infra to operate.
- Bad, because per-host or per-event pricing is *meaningfully* higher than App Insights at Phase 1 scale.
- Bad, because adds another vendor relationship and another credential to manage.
- Bad, because we lose the §15.1 implicit benefit of App Insights being on the same Azure subscription as everything else (a single `az monitor` CLI surface).

### Microsoft.Extensions.Logging defaults → App Insights SDK

- Good, because zero extra dependencies — `ILogger<T>` everywhere.
- Bad, because `Microsoft.Extensions.Logging` has no equivalent of Serilog's enrichers, `LogContext`, or `IDestructuringPolicy`. PII redaction would require a custom `ILogger` wrapper — reinventing the Serilog wheel poorly.
- Bad, because structured properties are passed as `{namedField}` placeholders, which is more error-prone than Serilog's typed enrichment.

## Structured Properties Contract (§17.1)

Every log entry produced inside a request scope MUST have the following properties when applicable:

| Property | Type | Set by | Notes |
|---|---|---|---|
| `userId` | guid (B2C `oid`) | auth middleware | Empty for anonymous requests |
| `traceId` | string (W3C) | App Insights auto | Distributed trace ID |
| `spanId` | string (W3C) | App Insights auto | Per-operation span ID |
| `correlationId` | guid | middleware (from `X-Correlation-Id` header or generated) | Threads through Service Bus messages |
| `bookingId` | guid | handler `LogContext.PushProperty` | When the operation concerns a booking |
| `propertyId` | guid | handler `LogContext.PushProperty` | When the operation concerns a property |
| `tenantId` | guid | middleware (Phase 1: constant) | Phase 2 multi-tenant ready |

Implementation pattern:

```csharp
public sealed class CorrelationMiddleware
{
    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
    {
        var correlationId = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                            ?? Guid.NewGuid().ToString();
        ctx.Response.Headers["X-Correlation-Id"] = correlationId;
        using (LogContext.PushProperty("correlationId", correlationId))
        using (LogContext.PushProperty("userId", ctx.User.FindFirst("oid")?.Value ?? ""))
        {
            await next(ctx);
        }
    }
}
```

A handler that loads a booking pushes the booking-specific properties:

```csharp
using (LogContext.PushProperty("bookingId", booking.Id))
using (LogContext.PushProperty("propertyId", booking.PropertyId))
{
    _logger.LogInformation("Confirming booking via {Trigger}", trigger);
    // …
}
```

## PII Redaction Policy (§17.1, §14.5)

`PiiRedactionDestructuringPolicy : IDestructuringPolicy`:

- `Email` → emits `{ "type": "Email", "redacted": "a***@e***.com" }` (preserves first char of local-part and first char of domain TLD).
- `PhoneNumber` → emits `{ "type": "Phone", "redacted": "***-***-1234" }` (last 4 digits only).
- `Address` → emits `{ "type": "Address", "city": "Cleveland", "state": "OH" }` (no street, no postal).
- `FullName` → emits `{ "type": "Name", "redacted": "J. D." }` (initials).
- Opt-out: handlers in the identity module set `LogContext.PushProperty("Pii.Allow", true)` for the duration of an identity operation (verification email send, etc.). The destructuring policy reads the active `LogContext` and bypasses redaction when this flag is set. All such scopes are documented and reviewed.

The same destructuring policy is applied to the §14.5 audit-log writer before the `before`/`after` JSONB columns are persisted.

## Workbook Sections (§17.3)

The single Azure Workbook has six sections, all deployable from Bicep:

1. **Booking Funnel** — search → hold → place → tentative → confirmed counts; conversion rates; drop-off histogram.
2. **Payments** — payment-intent created/succeeded/failed; refund volume; dispute count; webhook latency P50/P95.
3. **Sync Health** — per-property feed health (last successful poll, sync duration P95); conflict-detected rate; channel-source split.
4. **Messaging** — messages sent/delivered; SignalR connection count; email fallback rate (recipient-offline->10m).
5. **Errors** — 5xx rate by route; top exception types; unhandled-exception count.
6. **Infrastructure** — CPU/mem/RPS per Container App; Postgres CPU/IO/connections; Redis evictions; Service Bus queue depth.

## Alert Rules (§17.4)

The 8 alert rules from §17.4 deploy as Bicep `scheduledQueryRules` and `metricAlerts` resources, each with severity and an action group (`on-call`, `email`, `dashboard`):

| Alert | Implementation | Sev | Channel |
|---|---|---|---|
| Payment webhook 5xx burst | Metric alert on `requests/failed` filtered to `/webhooks/stripe` route, threshold > 5 in 5m | Sev2 | on-call |
| Sync feed unhealthy | KQL scheduled query: `customEvents \| where name == 'sync.feed.health' \| where customDimensions.value == '0' \| where timestamp > ago(2h)` returning > 0 rows | Sev3 | email |
| 5xx rate on API | Metric alert on `requests/failed` ratio > 1% over 10m | Sev2 | on-call |
| Booking SLA worker silent | KQL: absence of `BookingConfirmed`-via-SLA event in 6h | Sev3 | email |
| Postgres CPU | Azure Monitor metric `cpu_percent` > 80% sustained 10m | Sev2 | on-call |
| Redis evictions | Azure Monitor metric `evictedkeys` > 0 in 5m | Sev3 | email |
| Stripe dispute opened | KQL on `customEvents \| where name == 'payment.dispute.opened'` returning > 0 | Sev3 | email + dashboard |
| Conflict detected | KQL on `customEvents \| where name == 'booking.conflict.detected'` returning > 0 | Sev3 | dashboard banner + owner email |

Sev2 paging goes through PagerDuty action group (§15.2 prod row). Sev3 email goes to the team distribution list.

Each Sev2 alert links to a runbook in `/docs/runbooks/` (§17.5): `payment-webhook-failure.md`, `sync-feed-stale.md`, `api-5xx-spike.md`, `postgres-cpu-high.md`. Each runbook follows the Symptom → First-5-Minutes → Diagnostic queries (KQL) → Likely causes → Remediation steps → Escalation template.

## Links

- [Proposal §3.3 Azure Resource Topology](../../BookingApp_Proposal.md)
- [Proposal §14.3 OWASP A02 (no PII in logs)](../../BookingApp_Proposal.md)
- [Proposal §14.5 Audit Logging](../../BookingApp_Proposal.md)
- [Proposal §15.1 Per-Resource Details (App Insights, Log Analytics)](../../BookingApp_Proposal.md)
- [Proposal §17 Observability Plan](../../BookingApp_Proposal.md)
- [Proposal §23.2 Library/Framework versions (Serilog 4.x)](../../BookingApp_Proposal.md)
- [Serilog Application Insights sink](https://github.com/serilog-contrib/serilog-sinks-applicationinsights)
- [Application Insights workspace-based docs](https://learn.microsoft.com/azure/azure-monitor/app/create-workspace-resource)
