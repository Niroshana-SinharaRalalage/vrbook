# Runbook — Stripe dispute opened

> Severity: **Sev3** → email + dashboard banner.
> Owner: A5 (Payments).

## Symptom

`charge.dispute.created` webhook received. Booking transitions to `Disputed`.

## First 5 minutes

1. Verify the booking transitioned in `/admin/bookings/{id}` → Timeline.
2. Open the Stripe Express portal link for the connected account.
3. Note the `evidenceDueBy` from the alert payload.

## Workflow

Phase 1 does NOT automate evidence submission (see proposal §9.6). The owner
gathers messages, photos, and house rules acknowledgement from the booking detail
page and uploads in the Stripe dashboard.

## Escalation

- Disputes > $1k → notify client + lead.
- Pattern of disputes from same guest → consider blocking the guest in admin.
