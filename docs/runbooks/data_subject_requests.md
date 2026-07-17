# Runbook — Data-Subject Requests (GDPR / CCPA)

**Owner:** Platform operator · **SLA:** 30 days (GDPR) / 45 days (CCPA) from verified request.
**Status:** VRB-311 launch posture — **documented operator process** (no automated self-serve
deletion pipeline yet; that is a post-launch backlog item). Placeholder contacts pending owner.

## Intake

Requests arrive by email to **privacy@vrbook.example.com** (placeholder inbox), typically composed
via the form on `/legal/privacy` (`#your-rights`). Request types: **access, export, correct, delete**.

## Process

1. **Log** the request (date, type, requester email) in the privacy request register.
2. **Verify identity** — confirm the requester controls the account email (send a verification link
   / match against `identity.users`). Do not action unverified requests.
3. **Locate data** across systems, scoped by `user_id` / email:
   - `identity.*` (users, tenant_memberships), `booking.*` (bookings, holds), `payment.*` (Stripe
     customer/charges — via Stripe dashboard), `messaging.*`, `reviews.*`, `loyalty.*`.
   - Analytics (Application Insights): pseudonymous; note retention below.
4. **Fulfil**:
   - **Access / Export** — compile the records; deliver securely to the verified email.
   - **Correct** — update the record via the admin surface / SQL with an audit note.
   - **Delete** — delete/anonymize personal data **except** where a legal hold applies (see below).
5. **Respond** within the SLA; record completion in the register.

## Legal-hold / retention exceptions (do NOT delete)

- **Financial records** (bookings + payments) are retained per tax/financial-record obligations even
  after a delete request — anonymize PII where possible but keep the transaction record.
- Records under active dispute / chargeback / fraud investigation.
- Anything required by a lawful legal hold.

Document any withheld data and the legal basis in the response.

## Sub-processors (reference in responses)

- **Stripe** — payment data; deletion coordinated via Stripe (their DPA governs card data).
- **Microsoft Azure** — hosting/storage/email/analytics; deletion via the respective services.

## Post-launch backlog

Automated self-serve deletion (identity resolution + cascade across modules + legal-hold carve-outs)
— deferred from VRB-311 per architect/TL, tracked for a future compliance slice.
