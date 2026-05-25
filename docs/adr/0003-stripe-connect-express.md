# 3. Stripe Connect Express

- Status: Accepted
- Date: 2026-01-15
- Deciders: Solutions Architecture
- Tags: payments, stripe, third-party, multi-tenancy

## Context and Problem Statement

The platform takes money on behalf of the property owner. Phase 1 launches with a single owner (the client themselves), but the §22.4 Phase 2 roadmap is explicit: "Multi-Tenant SaaS … Per-tenant Stripe Connect accounts (already supported by Express)." The Stripe model chosen now must therefore (a) work for a single owner today, (b) survive multiple owners later without re-platforming, (c) keep the platform on the hook for an acceptable liability surface, and (d) leave the *guest-facing* checkout experience entirely under the platform's branding.

Stripe Connect offers three integration shapes — Direct Charges, Destination Charges (Standard accounts), and Connect with Express accounts — that trade off operational ownership of tax forms, dashboards, negative-balance liability, and onboarding UX. The §9.1 recommendation table lays this out:

| Concern | Standard | Express |
|---|---|---|
| Owner onboarding | Stripe-hosted full dashboard | Stripe-hosted minimal onboarding; platform owns most UX |
| 1099-K tax forms | Stripe handles | Stripe handles |
| Negative balance liability | Connected account | **Platform** (acceptable here — single owner = client itself) |
| Branding | Stripe-branded | Platform-branded |
| Phase-2 multi-owner readiness | Possible but UX-light | **Better** — platform can mediate UX uniformly |

The proposal concludes: "For a single-owner Phase 1 the difference is small, but Express is strictly more aligned with the architecture's multi-tenant-ready posture. We onboard one connected account for the client; future owners can be added without re-platforming."

## Decision Drivers

- Phase 2 multi-tenant readiness (§22.4) — the choice we make now must not require a payments migration later.
- Branding: guest-facing checkout is the direct-booking *moat* (§2.1 — repeat guests are why we exist); a Stripe-branded dashboard for the *owner* is acceptable, a Stripe-branded *guest* experience is not.
- 1099-K and W-9 handling — we do not want to build US tax-form generation; Stripe must own it.
- Negative-balance liability — in Phase 1 the platform *is* the owner so the liability is to ourselves. This is the moment to accept the trade-off, because at multi-tenant scale Express's platform-liability posture is what lets us mediate the owner UX uniformly (chargeback flows, refund flows, dispute evidence prompts).
- Onboarding friction — owners must onboard, but they are a tiny population in Phase 1 and a controlled population in Phase 2 (the platform decides who can onboard).
- Operational maturity of the Stripe.NET SDK — the §23.2 version table pins Stripe.net 47.x; Connect Express has been first-class for years.

## Considered Options

- **Stripe Connect Express** — Platform creates connected accounts on behalf of owners; Stripe hosts a minimal onboarding flow; platform owns the rest of the UX; platform liable for negative balance; Stripe handles 1099-K.
- **Stripe Connect Standard** — Owners create their own full Stripe accounts; Stripe hosts the full dashboard; owner liable for negative balance; Stripe handles 1099-K.
- **Stripe Connect with Direct Charges** — Each charge is created on the connected account directly; platform takes an `application_fee_amount`.
- **Plain Stripe (no Connect)** — All money flows into one Stripe account belonging to the platform; the platform pays the owner out of band (ACH, accounting).

## Decision Outcome

Chosen option: **"Stripe Connect Express"**, because it is the only option that simultaneously (a) keeps the guest checkout under our brand, (b) hands the W-9 and 1099-K problem to Stripe, (c) leaves the platform with mediating control over owner UX (essential for Phase 2 multi-tenancy), and (d) is supported by Stripe.NET's mature first-class APIs.

### Positive Consequences

- Guest checkout uses Stripe.js Elements on our domain with our branding — no Stripe-hosted page in the booking flow.
- Owner onboarding uses Stripe's hosted *Express onboarding link* (a short-lived URL we generate via `stripe.accounts.create` + `stripe.accountLinks.create`) — Stripe handles KYC, ID verification, bank-account collection, W-9 collection. We never touch SSN or bank routing numbers in our DB.
- 1099-K generation, mailing, and IRS filing are entirely on Stripe.
- A single integration shape works for one owner today and N owners tomorrow — Phase 2 multi-tenancy adds `accounts/create` calls to the owner-onboarding flow, no payments rewrite.
- Stripe Tax (§9.2) integrates with Connect Express transparently — calculations on the platform account, settlement via destination charges.
- Disputes (§9.6) flow into the platform; the owner uses an Express portal link to view dispute details. This is the right division of labour for a managed-services posture.

### Negative Consequences / Trade-offs

- **Negative balance is the platform's liability.** If a refund-after-payout exceeds the connected account's balance, Stripe debits the platform's account. In Phase 1 this is self-debt (platform = owner). In Phase 2 we will need an owner-deposit or reserve policy to manage this for third-party owners.
- The owner sees less of the raw Stripe dashboard than they would with Standard — some prefer the full dashboard. Mitigated by the Express portal link providing payout history, payment details, and dispute evidence upload.
- Express's onboarding requires the platform's branding to be configured in Stripe (logo, colours, support email). One-time setup in W5.
- We are coupled to Stripe — switching payment processors is a substantial undertaking. Accepted: Stripe is the industry standard for marketplaces, and any successor would require a similar integration.

## Pros and Cons of the Options

### Stripe Connect Express

- Good, because guest UX is fully platform-branded.
- Good, because owner UX is mostly platform-mediated, with Stripe handling the bits we cannot legally handle (tax forms, KYC, ID verification).
- Good, because §9.6 dispute flow ("the owner receives an alert and uses the Stripe Dashboard via Express portal link to upload evidence") is a first-class Express capability.
- Good, because §22.4 SaaS multi-tenancy is a configuration change, not a re-platform.
- Bad, because negative balance is the platform's liability — needs a reserve policy in Phase 2.
- Bad, because we cannot show the owner the full self-serve Stripe dashboard — only the Express portal subset.

### Stripe Connect Standard

- Good, because the owner owns the full Stripe relationship — least platform liability.
- Good, because owner gets the full Stripe dashboard out of the box.
- Bad, because the owner UX is *Stripe's* UX, not ours — we cannot impose uniform refund flows, payout schedules, or dispute prompts across owners in Phase 2.
- Bad, because Standard accounts in some jurisdictions surface Stripe branding in payment receipts and statements that *do* leak to the guest.
- Bad, because onboarding is heavier — owners must complete the full Stripe account creation, not a minimal Express flow.
- Bad, because the multi-tenant SaaS posture is "worse" per the §9.1 table.

### Stripe Connect with Direct Charges

- Good, because each transaction settles directly to the owner's account — clearest tax/reporting story per-owner.
- Bad, because the charge is *on the connected account*, which means the guest's statement descriptor and the dispute UX live with the owner — leaks Stripe-or-owner branding into the guest experience.
- Bad, because the platform must thread `application_fee_amount` through every charge and refund — more places to get wrong.
- Bad, because Stripe Tax integration with Direct Charges is more complex than with destination charges on the platform account.

### Plain Stripe (no Connect)

- Good, because simplest possible integration for Phase 1's single owner.
- Bad, because all money lands in the platform's bank account — the platform becomes a money-transmitter in many US states the moment it pays an owner who isn't the platform itself.
- Bad, because 1099-K generation becomes the platform's problem in Phase 2.
- Bad, because the architecture explicitly stages for multi-owner SaaS (§22.4) — building plain Stripe now and migrating to Connect later is the most expensive possible path.
- Bad, because Stripe's own Connect documentation specifically warns against this pattern for marketplace-shaped products.

## Implementation Notes

- One Connect Express account is created at W5 during owner onboarding for the Phase 1 owner (the client). `accounts.create({ type: 'express', country: 'US', capabilities: { card_payments: ..., transfers: ... } })`.
- Booking checkout (§9.3) creates a PaymentIntent with `transfer_data.destination = <connected_account_id>` (destination charge pattern) and `application_fee_amount = 0` for Phase 1 (the platform takes no per-booking fee — that's a Phase 2 SaaS pricing decision per §22.4).
- Webhook events both at platform level (`payment_intent.succeeded`, `charge.refunded`, `charge.dispute.created`) and at connected-account level (forwarded via Connect webhook endpoint) are stored in the `payment.webhook_events` idempotency table keyed by `stripe_event_id` (§9.7).
- The owner sees a "Payments" page in the admin dashboard with key metrics and a deep link to the Stripe Express portal (`accounts.createLoginLink`) for the full Stripe-hosted owner view.
- All Stripe writes carry an `Idempotency-Key` header derived from a per-operation deterministic key (`booking:{bookingId}:pi-create`, `booking:{bookingId}:refund:{refundAttempt}`) — required by §3.4 "Idempotency keys" pattern table.

## Tax-Form Handling — Why Stripe Owns It

US payment processors that pay out to individuals or businesses are required to file Form 1099-K when annual gross volume exceeds threshold ($600 nationally from 2024, with state variation). Building 1099-K generation in-house means:

- Tracking gross volume per connected account per calendar year.
- Collecting and validating W-9s (TIN + name match against IRS records).
- Generating Form 1099-K PDFs and mailing them by January 31.
- Electronic filing with the IRS via FIRE system.
- State-level reporting variations.

Express assigns all of this to Stripe. The connected account holder completes the W-9 in Stripe's hosted onboarding; Stripe tracks volume; Stripe mails the 1099-K. In Phase 2 multi-tenant SaaS this is the difference between scaling owners freely versus hiring a tax-ops function.

## Phase 2 Multi-Tenant Path

The §22.4 Phase 2 SaaS roadmap item — "Per-tenant Stripe Connect accounts (already supported by Express)" — is realised by:

1. Owner self-service registration creates a Stripe Express account via `accounts.create({ type: 'express', country: <owner-country>, … })`.
2. Owner completes hosted onboarding via `accountLinks.create({ type: 'account_onboarding' })`.
3. Owner's `stripe_connected_account_id` is stored on `identity.users` with `tenant_id`.
4. Per-property `stripe_connected_account_id` resolves to the owning tenant's account.
5. Booking checkout's PaymentIntent uses that account as `transfer_data.destination`.
6. `application_fee_amount = ceil(booking_total * platform_fee_pct)` is the SaaS revenue line.

None of these steps require a payments rewrite — they are configuration data and a few new screens on top of the Phase 1 integration. That is the precise meaning of "multi-tenant ready" in §22.4.

## Negative-Balance Risk Management

The §9.1 table is explicit that Express puts negative-balance liability on the platform. The scenarios that produce negative balance:

- Refund after payout: payout sweeps the connected account's balance to the bank account on a schedule. A subsequent refund debits an empty connected account; Stripe debits the platform.
- Dispute lost after payout: chargeback hits the platform after funds have been swept.
- Fraud reversal: ACH or card-network reversal arrives weeks after the booking.

In Phase 1 with one owner (= the client themselves), negative balance is self-debt. The platform funds the refund/chargeback from its operating account. Operationally noisy, financially neutral.

In Phase 2 with N owners, this becomes the platform's exposure. Two mitigations are designed for now and built then:

1. **Payout-hold window** — `payout_schedule.delay_days` on the connected account can be tuned upward (e.g., 7 days) so refunds within a short window debit the connected account's still-held balance, not the platform.
2. **Reserve policy** — Stripe Connect supports a `reserve` configuration that holds a percentage of incoming volume against future chargebacks. We expose this in the Phase 2 owner-onboarding flow.

Designing the data model now with `payout_schedule` and `reserve_policy_id` columns on `payment.connected_accounts` means the Phase 2 build is a configuration layer over an already-supported field set.

## Links

- [Proposal §9.1 Recommended Connect Model](../../BookingApp_Proposal.md)
- [Proposal §9.6 Dispute Handling](../../BookingApp_Proposal.md)
- [Proposal §9.7 Webhook Idempotency](../../BookingApp_Proposal.md)
- [Proposal §22.4 Multi-Tenant SaaS](../../BookingApp_Proposal.md)
- [Stripe Connect Express docs](https://stripe.com/docs/connect/express-accounts)
- [Stripe Connect account types comparison](https://stripe.com/docs/connect/accounts)
