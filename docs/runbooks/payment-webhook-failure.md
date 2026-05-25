# Runbook — Stripe webhook 5xx burst

> Alert: `payment.webhook.5xx > 5 in 5 min`. Severity: **Sev2** → on-call.
> Owner: A5 (Payments). Replaces this stub when the webhook handler ships.

## Symptom

Stripe webhook handler at `POST /api/v1/payments/webhooks/stripe` is returning 5xx;
Stripe is retrying with exponential backoff. Bookings may stall in `Draft` because the
`payment_intent.succeeded` event is not being processed.

## First 5 minutes

1. Acknowledge in PagerDuty / on-call channel.
2. Check the [API container app revision history](https://portal.azure.com) — is there a recent revision rollout? Roll back if yes.
3. Check `payment.webhook_events` table for `processing_status = 'failed'` rows in the last 15 min.
4. If Stripe dashboard shows the events as undelivered, the platform is dropping them at the network layer — escalate to infra.

## Diagnostic queries

```kusto
// App Insights — failed webhook executions
requests
| where timestamp > ago(30m)
| where url endswith "/payments/webhooks/stripe"
| where resultCode startswith "5"
| project timestamp, resultCode, duration, customDimensions.stripe_event_id, customDimensions.stripe_event_type
| order by timestamp desc

// App Insights — exceptions in webhook scope
exceptions
| where timestamp > ago(30m)
| where customDimensions.RequestName == "StripeWebhookHandler"
| project timestamp, type, outerMessage, customDimensions
```

## Likely causes

| Cause | Confirm by | Fix |
|---|---|---|
| Stripe signing secret rotated | Recent Stripe dashboard activity | Update `Stripe__WebhookSecret` in Key Vault, restart API |
| DB unavailable | App Insights `dependencies` shows Postgres failing | See [`postgres-cpu-high.md`](./postgres-cpu-high.md) |
| Idempotency table contention | Lock waits in pg_stat_activity | Add index on `webhook_events(stripe_event_id)` if missing |
| Deploy regression | New revision rolled out in same window | Rollback revision |

## Remediation

1. **Rollback** the API revision if a deploy is the suspected cause: `az containerapp revision activate -n api-vrbook-prod -g rg-vrbook-prod --revision <previous>`.
2. **Replay** failed events: re-process rows in `payment.webhook_events` where `processing_status = 'failed'` — the handler is idempotent on `stripe_event_id`.
3. **Validate** by triggering a test event via `stripe trigger payment_intent.succeeded` (in staging) or by checking that real events flow.

## Escalation

- If unresolved in 30 min → page engineering lead.
- If revenue impact > $1k or > 50 stalled bookings → notify client.
