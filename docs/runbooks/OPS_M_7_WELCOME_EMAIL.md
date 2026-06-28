# OPS.M.7 — Operator-Manual Welcome Email Runbook

**Scope:** Slice OPS.M.7 ships the onboarding wizard UI but defers the
automated `tenant.welcome` email send to **Slice 4** (when the ACS pipeline
+ `TenantNotificationHandlers` land). Until Slice 4 ships, ops sends the
welcome email **manually** after a new tenant signs up.

## Trigger

A new tenant row appears in `identity.tenants` with `status =
'PendingOnboarding'`. Detect via:

```sql
SELECT id, slug, display_name, created_at
FROM identity.tenants
WHERE status = 'PendingOnboarding'
  AND created_at > NOW() - INTERVAL '24 hours';
```

The on-call rotation owns this query. Run it once per business day.

## Email contents

For each row, send the following message body (substitute the placeholders):

> Subject: Welcome to VrBook, {DisplayName}
>
> Hi {OwnerFirstName},
>
> You're set up on VrBook as `{Slug}`. Three quick steps to go live:
>
> 1. Add your first property
> 2. Connect Stripe to get paid
> 3. Review your settings
>
> Open your dashboard: `https://app.vrbook.com/admin/onboarding`
>
> Reply to this email if you need help.
>
> — The VrBook team

## Stripe deep-link

The `/admin/onboarding` route surfaces the **Connect Stripe** button as
the active step once the first property exists. The button itself is wired
to the OPS.M.5 endpoints (`POST /admin/tenants/{id}/stripe/onboard` →
`POST .../stripe/account-link` → redirect to Stripe-hosted form). Ops does
NOT need to send the Stripe URL manually — the wizard handles it.

## Slice 4 swap

When Slice 4 ships the ACS pipeline + `TenantNotificationHandlers`:

1. Add a new MediatR handler under
   `src/Modules/VrBook.Modules.Notifications/Application/Handlers/`
   subscribing to `TenantCreated` (already raised by `Tenant.Create`).
2. Render the welcome email against the new `tenant.welcome` template
   (sits alongside the existing booking templates).
3. Delete this runbook.

Per OPS.M.7 §3.8 (D8): no backend rewiring beyond the new handler + new
template row. The wizard URL is the same; the email content above maps
1:1 to the template fields.

## Audit

Each manual send must be logged in `ops-log` Slack channel with the
tenant id + send timestamp. Slice 4 backfills these into
`notifications.notification_log` as `Source = ops_manual` rows.
