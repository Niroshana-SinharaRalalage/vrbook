# Notification dispatch failures

**Triggering alert:** `alert-vrbook-{env}-dead-letter-accrual` (DeadLetter rows > 5 in 15m) OR ops-side spot check of stuck `Sending` rows past the 5-min lease cutoff.

**Sev:** Sev3 (email delivery is best-effort; individual failures don't page).

**Owner:** A9 (Notifications module).

Deferred sections cover the OPS.M.7 tenant-welcome, guest.welcome, review-request, booking lifecycle emails, and owner-side alerts — everything queued through `notification_log` under the Slice 4 / 4.V2 pipeline.

---

## 1. Symptom

One of:
- Grafana widget "Notification DeadLetter accrual (24h)" reads > 5 rows.
- Grafana widget "Notification Sending stuck > 5 min" reads > 0.
- An operator manually reported a missing email (tenant welcome, review request, booking confirmation).
- ACS-side dashboard shows repeated 429/5xx or a domain reputation dip.

## 2. First 5 minutes (containment)

Confirm scope before diagnosis:

```sql
-- How many rows in each terminal / stuck state right now?
SELECT status, count(*)
FROM notifications.notification_log
WHERE created_at > NOW() - INTERVAL '24 hours'
GROUP BY status
ORDER BY count(*) DESC;
```

Expected shape: `Sent >> Queued > Failed >= 0, DeadLetter < 5, Sending < 3`. If `Sending > 3` AND rows have `dispatch_started_at < NOW() - INTERVAL '5 minutes'`, the worker crashed mid-send; jump to §5.

If DeadLetter is disproportionately from ONE `kind` (e.g., only `TenantWelcome`), the template rendering pipeline for that kind is broken; jump to §4.

If DeadLetter is spread across all kinds and ACS 5xx logs are elevated, the ACS resource is unhealthy; jump to §6.

## 3. Diagnostic queries

**Recent failures with error text:**

```sql
SELECT id, kind, status, retry_count, LEFT(last_error, 200) AS err_head,
       recipient_email, created_at
FROM notifications.notification_log
WHERE status IN (2, 3) -- Failed, DeadLetter
  AND created_at > NOW() - INTERVAL '2 hours'
ORDER BY created_at DESC
LIMIT 30;
```

**Stuck `Sending` rows past the 5-min lease cutoff (the C2 lease mechanism resets these on the next worker tick; if they linger, the worker cron isn't running):**

```sql
SELECT id, kind, dispatch_started_at, recipient_email
FROM notifications.notification_log
WHERE status = 4 -- Sending
  AND dispatch_started_at < NOW() - INTERVAL '5 minutes'
ORDER BY dispatch_started_at ASC
LIMIT 20;
```

If this returns rows, verify the Container App Job:

```
az containerapp job show \
  -n cj-vrbook-notification-dispatch-staging \
  -g rg-vrbook-staging \
  --query '{state:properties.provisioningState, executionCount:properties.executionCount}'

# Recent executions:
az containerapp job execution list \
  -n cj-vrbook-notification-dispatch-staging \
  -g rg-vrbook-staging \
  --query '[0:5].{status:properties.status, startTime:properties.startTime}'
```

**ACS-side dashboard:** Azure Portal → Communication Services → `acs-vrbook-{env}` → Email → Insights → Delivery status. Filter to last 4h. Watch: `Send-Rate` (target 50/min sustainable), `Failed-Rate` (should be < 5% baseline).

**Log Analytics — worker exceptions:**

```kusto
ContainerAppConsoleLogs_CL
| where ContainerAppName_s startswith "cj-vrbook-notification-dispatch"
| where TimeGenerated > ago(2h)
| where Log_s contains "Exception" or Log_s contains "Failed"
| project TimeGenerated, Log_s
| order by TimeGenerated desc
| take 50
```

## 4. Template rendering error (single-kind DeadLetter)

**Symptom:** DeadLetter rows all share a single `kind` (e.g., every `TenantWelcome` fails); `last_error` mentions `template`, `Mustache`, or `Stubble.Core`.

**Cause:** A recent template edit introduced a Mustache syntax error, an unreferenced variable in a `{{#Section}}`, or a missing partial. `NotificationTemplatesRenderTests` should catch this pre-merge; if it slipped through, ops-side triage.

**Steps:**

1. Inspect the failing template:
   ```
   git show HEAD:src/Modules/VrBook.Modules.Notifications/Templates/<name>.mustache
   ```
2. Compare payload shape with `Templates/Samples/<name>.json` — any keys referenced by the template must appear in the sample fixture. Check the corresponding handler queues the same key set.
3. If the fix is a hot-patch: correct the template + fixture, run `dotnet test --filter NotificationTemplatesRenderTests`, cut a `develop` commit + `main` cherry-pick, re-deploy. Then reset DeadLetter rows via `POST /api/v1/admin/notifications/{id}/retry` — see §7.
4. If the fix requires payload contract change: file a bug + reset the failing rows to Queued with a `NotBeforeUtc = NOW() + INTERVAL '1 hour'` buffer so they don't re-fail during the deploy.

## 5. Worker crashed / cron not firing

**Symptom:** `Sending` rows accumulate; `dispatch_started_at` is > 5 min old; DeadLetter is NOT growing (rows just aren't being processed).

**Cause:** The Container App Job cron (`*/2 * * * *`) missed a tick, OR the job hit a persistent startup failure (managed identity, ACR pull, network).

**Steps:**

1. Force a manual invocation:
   ```
   az containerapp job start \
     -n cj-vrbook-notification-dispatch-staging \
     -g rg-vrbook-staging
   ```
2. Watch the execution:
   ```
   az containerapp job execution list \
     -n cj-vrbook-notification-dispatch-staging \
     -g rg-vrbook-staging \
     --query '[0].{status:properties.status, startTime:properties.startTime, endTime:properties.endTime}'
   ```
3. If it exits `Failed`, tail its logs:
   ```
   az containerapp job execution show \
     -n cj-vrbook-notification-dispatch-staging \
     -g rg-vrbook-staging \
     --job-execution-name <execution-name>
   ```
   Common causes:
   - ACR pull denied → managed identity role reassignment.
   - Postgres unreachable → check `psql-vrbook-{env}-v2` firewall + IP allow-list.
   - ACS connection string missing → KV secret rotation drift.
4. If the manual run succeeds, sit back and let the C2 lease-reset mechanism recover the stuck `Sending` rows on the next tick. Confirm via §3's stuck-rows query returning empty within 10m.
5. If the job is broken for > 1h, escalate — the deferred email queue drains slowly, and a delayed booking-confirmation email is a Sev2-adjacent user complaint.

## 6. ACS-side outage / rate limit

**Symptom:** DeadLetter is broad across kinds; ACS Insights dashboard shows an elevated failure rate or a `Failed-4xx` spike; `last_error` mentions `429`, `RateLimit`, `MessageRejected`, or `RecipientDomainBlocked`.

**Cause:** ACS Azure-side incident, ACS Free Tier quota hit (1000 emails/24h), or ACS domain reputation dip.

**Steps:**

1. Check the Azure Status page (`status.azure.com`) for a Communication Services incident in the same region.
2. If Free Tier quota is the culprit:
   ```
   az communication show \
     -n acs-vrbook-{env} \
     --query "properties.dataLocation"
   ```
   Portal → the ACS resource → Monitoring → Metrics → filter to "Email/EmailDeliveredCount" over 24h. If close to 1000, bump the tier via portal (Cost & Quota → Change plan → Standard). Non-code action, ~5min.
3. If ACS 429 storm is starving booking-critical dispatch, pause the worker temporarily:
   ```
   az containerapp job stop -n cj-vrbook-notification-dispatch-staging -g rg-vrbook-staging
   ```
   Then manually retry critical rows via `/admin/notifications` (see §7). Restart the worker after the storm resolves:
   ```
   az containerapp job start -n cj-vrbook-notification-dispatch-staging -g rg-vrbook-staging
   ```
4. If the domain is `AzureManagedDomain` and reputation is dipping, the mid-term fix is the OPS.8 custom-domain + DKIM slice. Short term: throttle sending rate by adjusting the cron to `*/5 * * * *` and let the queue backfill overnight.

## 7. Admin-driven retry

For any Failed or DeadLetter row an operator wants to re-send after root-cause fix:

- Web: `/admin/notifications` → filter by status → click "Retry" on the row. Requires `tenant_admin` role in the row's tenant (M.17 gate). NULL-tenant rows (guest-flow emails) require PlatformAdmin (M.17 handler branch).
- API: `POST /api/v1/admin/notifications/{id}/retry` — same guards.
- SQL (break-glass, PlatformAdmin only):
  ```sql
  UPDATE notifications.notification_log
     SET status = 0, -- Queued
         retry_count = 0,
         last_error = NULL,
         dispatch_started_at = NULL
   WHERE id = '<row-id>'
     AND status IN (2, 3); -- guard against reset of Sent rows
  ```
  The next worker tick picks it up.

## 8. Escalation

- **Sev3 → Sev2** if: DeadLetter accrual crosses 50 rows in 4h AND booking-critical kinds are affected (`BookingConfirmed`, `BookingRejected`, `RefundIssued`).
- **Sev2 → Sev1** if: booking-critical emails haven't dispatched for > 4h AND a guest complaint has surfaced (rare — usually customers self-report through support).

Page order: A9 (Notifications) → A0 (infra) → Solutions Architect.

---

## Related

- [`OPS_M_16_TURNOVER_AWARE_COMPLETION_PLAN.md`](../OPS_M_16_TURNOVER_AWARE_COMPLETION_PLAN.md) — `BookingCompleted.Trigger` is what drives the manual/sweep fork in review.request emails.
- [`SLICE_4_PLAN_V2.md`](../SLICE_4_PLAN_V2.md) — the Slice 4 v2 residuals plan; 4.V2.5 is where this runbook lands.
- [`adr/0011-azure-communication-services-email.md`](../adr/0011-azure-communication-services-email.md) — ACS provider decision.
- [`booking-sla-worker-silent.md`](booking-sla-worker-silent.md) — sibling Container App Job runbook for a different worker.
