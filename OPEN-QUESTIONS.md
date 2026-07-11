# VrBook — Open Questions

Ambiguous business rules, product decisions, and unknowns surfaced during specification. **Do not guess these** — they need an owner decision. Each is referenced by the story/PRD section that depends on it. Status: 🔴 open · 🟡 assumed-pending-confirmation · 🟢 answered.

> Populated in Phase 1 (code audit). Grows through Phases 2–3. Answers get folded back into the PRD/stories and marked 🟢 with the decision + date.

## Product / business rules

| # | 🔴 | Question | Why it matters | Current code behavior |
|---|---|---|---|---|
| Q1 | 🔴 | What is the correct **Tentative booking hold window** — 24h (code) or 6h (Bicep comment / older docs)? | Drives the expiry sweep + guest UX | Hard-coded 24h (`Booking.cs:119`); config key ignored (G2) |
| Q2 | 🔴 | Loyalty tiers: confirm thresholds (Bronze 1 / Silver 3 / Gold 6 stays?) and the **actual guest benefit** per tier (discount %? perks?) | Loyalty is shipped but the discount resolver + tier value are thin | `LoyaltyAccount.cs` hard-codes 3/6; `Loyalty:Enabled` gate exists |
| Q3 | 🔴 | **Cancellation & refund policy** matrix — what policies (Flexible/Moderate/Strict) mean in refund terms, and who sets them (per-property? per-tenant?) | Payment refund path + guest-facing policy surface | `CancellationPolicy` enum exists on Booking; refund logic partial |
| Q4 | 🔴 | **Platform fee** model — default 1500 bps (15%)? Per-tenant override only by platform admin? Is it shown to owners? | Revenue model; Stripe `ApplicationFeeAmount` | `PlatformFeeBps` default 1500; platform-admin-set |
| Q5 | 🔴 | **Taxes/fees** — who owns tax calculation? (`StubTaxCalculator` exists.) Which jurisdictions at launch? Is tax owner-configured or platform-computed? | Legal/financial correctness; checkout total | Stubbed (`StubTaxCalculator`) |
| Q6 | 🔴 | **Manual capture** — is owner-approval-before-charge (current: place→Tentative→owner confirm→capture) the intended UX at launch, or auto-capture (instant book)? | Core funnel + conversion | Manual capture is the Phase-1 default |
| Q7 | 🔴 | Guest **self-service tenant sign-up** — Phase 2 (invite-only now). Confirm launch is invite-only tenants? | Onboarding scope | Invite-only; schema supports self-serve |
| Q8 | 🔴 | Which **social login** providers must work at launch (Google works; FB/Apple/MS pending portal setup)? | Auth scope + operator work | Google wired; others deferred |
| Q9 | 🟡 | Multi-currency: is display + settlement single-currency-per-tenant at launch (assumed), or must a guest pay in their own currency? | Pricing display, Stripe settlement | Per-tenant `DefaultCurrency`; no FX |

## Compliance / legal (need decisions before launch)

| # | 🔴 | Question |
|---|---|---|
| Q10 | 🔴 | **PCI scope** — confirm Stripe Elements/hosted flows keep us in SAQ-A (no card data touches our servers)? |
| Q11 | 🔴 | **GDPR/CCPA** — data-retention periods (bookings, messages, PII), right-to-erasure flow, DPA with sub-processors (Stripe, Azure)? |
| Q12 | 🔴 | **Cookie consent** — required banner + categories? Which analytics/tracking need consent? |
| Q13 | 🔴 | **Legal surfaces** — Terms of Service, Privacy Policy, per-tenant cancellation policy display, and who authors them? |
| Q14 | 🔴 | **Tax/VAT** handling + invoicing/receipts obligations per market? |

## Non-functional targets (need numbers)

| # | 🔴 | Question |
|---|---|---|
| Q15 | 🔴 | **Availability target** (99.5%? 99.9%) + RTO / RPO for disaster recovery? |
| Q16 | 🔴 | **Concurrency/load target** at launch (the k6 gate is 50 RPS / P95<1s — is that the real target)? |
| Q17 | 🔴 | **Core Web Vitals budget** for the booking funnel (LCP/INP/CLS thresholds)? |
| Q18 | 🔴 | **Accessibility** — is WCAG 2.2 AA a hard launch gate or a best-effort target? |
| Q19 | 🔴 | **i18n** — English-only at launch, or which locales? |
| Q20 | 🔴 | **Support model** — hypercare window, on-call, incident SLAs, first-week review owner? |

## Scope confirmations

| # | 🔴 | Question |
|---|---|---|
| Q21 | 🔴 | Are the **501 stubs** (property image upload, booking admin queue, message attachments) in-scope for launch or deferrable? |
| Q22 | 🔴 | Is the **outbox→Service Bus relay** needed for launch, or is in-process dispatch acceptable at launch scale? |
| Q23 | 🔴 | Confirm **Phase 3/4** (hotel rooms, multi-unit cart, OTA bundling) are strictly post-launch (no launch dependency)? |
