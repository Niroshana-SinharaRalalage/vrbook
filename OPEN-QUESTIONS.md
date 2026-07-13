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

---

## Round 1 decisions (owner, 2026-07-13) — 🟢

| # | Decision |
|---|---|
| Q1 | 🟢 **48h** Tentative hold window (make configurable; fix the dead `Booking:TentativeSlaHours` key + hard-coded 24h → G2 story). |
| Q2 | 🟢 Loyalty gives **no guest benefit yet** — tiers tracked, no discount/perk at launch. |
| Q3 | 🟢 **Flexible** policy only at launch; set **per property** (owner-configured). *Concrete refund rule → Round 2 Q24.* |
| Q4 | 🟢 Platform fee configured by **platform admin** (current behavior). |
| Q5 | 🟢 **Platform-calculated tax**, **USA** first. *Engine + marketplace-facilitator model → Round 2 Q25.* |
| Q6 | 🟢 **Manual capture** at launch. |
| Q7 | 🟢 **Invite-only** tenants at launch. |
| Q8 | 🟢 **Email+password + Google** at launch; FB/Apple/MS post-launch. |
| Q9 | 🟢 **Single currency per tenant, no FX** at launch. |
| Q10 | 🟢 **Stripe hosted/Elements** (SAQ-A) — card data never touches our servers. |
| Q11 | 🟢 Follow **industry standard** GDPR/CCPA — I propose the retention/erasure policy for review. *→ Round 2 Q26.* |
| Q12 | 🟢 Cookie consent — I propose the **industry-standard** approach for review. *→ Round 2 Q26.* |
| Q13 | 🟢 **I draft** Terms / Privacy / Cancellation; owner reviews. *Entity + governing law → Round 2 Q27.* |
| Q14 | 🟢 Receipts/VAT — I **recommend** (emailed receipts w/ tax breakdown). *→ Round 2 Q25.* |
| Q15 | 🟢 **99.5%** availability. *RTO/RPO → Round 2 Q28.* |
| Q16 | 🟢 **50 RPS / P95<1s** is the launch target. |
| Q17 | 🟢 Core Web Vitals: **LCP<2.5s, INP<200ms, CLS<0.1**. |
| Q18 | 🟢 **WCAG 2.2 AA** as the industry-standard target. *Hard gate vs best-effort → Round 2 Q28.* |
| Q19 | 🟢 **English only** at launch. |
| Q20 | 🟢 **Hypercare** window after launch. *Details → Round 2 Q28.* |
| Q21 | 🟢 The 501 stubs (image upload, admin booking queue, message attachments) are **launch-critical** → stories. |
| Q22 | 🟢 **In-process** event dispatch is fine at launch scale (outbox→Service Bus relay deferred). |
| Q23 | 🟢 **Phase 3/4: design + document NOW, defer only implementation.** General rule: post-launch defers *code*, not *design/docs*. |

**Program rule (owner, 2026-07-13):** every P0/P1 gap in `CURRENT-GAPS.md` becomes its own user story. Post-launch = deferred implementation only; design & documentation happen now.

---

## Round 2 — design clarifications (open 🔴)

### Launch scope
- **Q24 (Cancellation/refund rule):** concrete "Flexible" terms? *Proposed: full refund if cancelled ≥ 7 days before check-in; 50% if 2–7 days; no refund < 48h. Applies to captured (Confirmed) bookings; Tentative/unconfirmed always fully released.*
- **Q25 (Tax + receipts):** which tax engine (Stripe Tax vs Avalara/TaxJar vs owner-entered rates)? Is VrBook the **marketplace facilitator** that collects+remits US lodging tax, or does the owner remit? Which US states at launch (all, or a starter set)? Is tax applied to fees too? Emailed receipts w/ tax breakdown — confirm.
- **Q26 (Privacy/GDPR/CCPA/cookies):** launch is US-first but guests may be global. *Proposed: CCPA/CPRA + GDPR-ready; retention — financial/booking records 7y, messages 2y post-checkout, PII erasure on request (financial carve-out), soft-delete→hard-delete accounts; cookie consent banner (necessary/analytics categories).* Confirm markets + retention numbers.
- **Q27 (Legal entity):** legal entity name + governing-law state for the ToS/Privacy drafts?
- **Q28 (Ops NFR):** RTO/RPO target (*proposed: RPO ≤ 1h via Postgres PITR, RTO ≤ 4h*); WCAG 2.2 AA — hard launch gate or best-effort? hypercare — duration + on-call owner (*proposed: 2 weeks, daily review week 1, owner+eng on-call*)?

### Phase 3 — Hotel-style rooms
- **Q29 (Model):** a `Property` has **0..N Rooms**; 0 rooms = whole-property booking (current), N rooms = book individual rooms — confirm this "Property = Facility with optional rooms" shape?
- **Q30 (Room-type vs individual room):** model **Room Types with an inventory count** (hotel style: "5× Deluxe King") or **named individual rooms**? *Proposed: Room Type + inventory (industry standard, supports overbooking control).*
- **Q31 (What's per-room):** which are per-room vs facility-level — pricing, availability/calendar, photos, capacity, amenities, reviews, iCal feed? *Proposed: pricing + availability + capacity + photos per room-type; amenities both levels; reviews at facility level; iCal per room-type.*

### Phase 3 — Multi-unit cart
- **Q32 (Scope):** **same-tenant only** (multiple rooms/properties from one business in one checkout); cross-tenant is Phase 4 — confirm?
- **Q33 (Payment):** same-tenant → **one PaymentIntent to one Connect account** (no split) — confirm (splits are Phase 4)?
- **Q34 (Atomicity):** if one item becomes unavailable at checkout — **fail the whole cart atomically**, or book what's available? *Proposed: atomic all-or-nothing.*
- **Q35 (Partial cancel):** can a guest cancel **one item** and keep the rest, or all-or-nothing? *Proposed: per-item cancel allowed.*

### Phase 4 — OTA package bundling
- **Q36 (Supplier model — the big one):** are Flights/Cars/Activities/extra-Stays (a) **manually entered** by the travel-agency tenant as priced line items (no live inventory), (b) supplied by **other VrBook tenants** (cross-tenant, revenue-split), or (c) **external APIs** (Amadeus/GDS etc.)? *Proposed: (a) + (b) — agency composes manual legs AND on-platform supplier stays/activities; external flight/car APIs post-Phase-4.*
- **Q37 (Sell as):** itinerary sold as a **fixed package** (buy the whole thing) or a **customizable cart** (guest picks legs)? *Proposed: agency builds a fixed package; guest books it whole.*
- **Q38 (Payment + FX):** single payment to the **agency tenant** who settles suppliers off-platform, or **split at checkout** across supplier Connect accounts? Guest pays in **one currency** (agency's) with per-supplier FX handled at settlement? *Proposed: split via Stripe multi-destination where suppliers are on-platform; guest pays agency currency.*
- **Q39 (Cancellation):** **per-leg** cancellation policies (each leg its own rule), aggregated into the itinerary — confirm?

---

## Round 2 decisions (owner, 2026-07-13) — 🟢

| # | Decision |
|---|---|
| Q24 | 🟢 **Two cancellation models, owner picks per property.** (1) Tiered refund (full ≥7d / 50% 2–7d / none <48h) with the **thresholds+percentages configured by platform admin** (global values). (2) **Refundable-rate upgrade**: guest pays extra at booking for a full-refund option; refund request must arrive before check-in; without the extra payment the booking is **non-refundable**. |
| Q25 | 🟢 **Stripe Tax**; **platform collects** (marketplace facilitator); remittance model confirmed with accountant per state; **all US states** via the engine. Emailed receipts w/ tax breakdown. |
| Q26 | 🟢 Proposed privacy/retention/cookies accepted (CCPA/CPRA + GDPR-ready; 7y financial, 2y messages, erasure w/ carve-out; consent banner). |
| Q27 | 🟢 Legal entity governing law: **Ohio, USA**. |
| Q28 | 🟢 Proposed ops targets accepted (RPO ≤1h PITR, RTO ≤4h; WCAG 2.2 AA best-effort; 2-week hypercare). |
| Q29 | 🟢 Owner **chooses per property**: advertise as **one whole-house listing** OR list **individual rooms** (room-type units). A whole-house-with-rooms that isn't rented per-room stays a single listing. |
| Q30 | 🟢 **Room Type + inventory count** model. |
| Q31 | 🟢 Per Q29: pricing/availability/capacity/photos/iCal per room-type when in room mode; reviews at facility level; amenities both. |
| Q32 | ⚠️ **PIVOT — cross-business cart is a first-class capability, available at any time.** Wants standardized rules where cross-business carts are impacted. **Re-asking impacted questions → Round 3.** |
| Q33 | 🟢 **Payment splitting required** (Stripe multi-destination). |
| Q34 | 🟢 Atomic all-or-nothing cart. |
| Q35 | 🟢 Per-item cancel allowed. |
| Q36 | 🟢 OTA suppliers: **(a) manual agency legs + (b) on-platform supplier tenants** now; external flight/car APIs later. |
| Q37 | 🟢 Agency builds a **fixed package**; guest buys it whole. |
| Q38 | 🟢 **Split via Stripe multi-destination**; guest pays agency currency. *(Incumbent-model explainer provided; FX → Round 3 Q9R.)* |
| Q39 | ⚠️ Reframed → Round 3 Q39R (single global policy vs per-item-aggregated framework). |

---

## Round 3 — cross-business-cart impact (open 🔴)

Cross-business cart (guest assembles items from multiple independent businesses → one atomic checkout) is now first-class. Impacted questions:

- **Q32R (scope/timing):** cross-business cart = a **guest-assembled multi-seller cart**, designed now, implemented in Phase 3/4 (not a launch feature) — confirm? Is it the **same mechanism** as the agency OTA package (Q37) or a separate guest-facing flow?
- **Q9R (currency/FX):** a cross-business cart can mix tenant currencies → adopt **guest display-currency + FX for display, per-tenant settlement currency** (Booking.com/Expedia model). Overrides Q9's "no FX." *Proposed: yes; single-tenant launch bookings stay single-currency, the FX/display layer lands with the cart.*
- **Q3R/Q24R (cancellation placement):** keep cancellation **per-property** (owner picks a model; platform sets tier numbers), attached to each line item and **shown per-item in the cart** — rather than one identical global policy? *Proposed: per-item.*
- **Q39R (framework vs identical policy):** standardize the **framework/engine** (one policy engine, one tax model, one currency-display model, one split-payment model) with **per-property values + per-item display**, NOT one identical policy platform-wide (which drives owners away and no incumbent does). Accept, or do you specifically want one identical policy for all tenants?

---

## Round 3 decisions (owner, 2026-07-13) — 🟢 (accepted all recommendations)

| # | Decision |
|---|---|
| Q32R | 🟢 Cross-business cart = **guest-assembled multi-seller cart**; **designed now, implemented in Phase 3/4**, not a launch feature. **Same underlying engine** as the agency OTA package (line-item model + split payment) — two front-ends (guest cart + agency package builder). |
| Q9R | 🟢 **Guest display-currency + FX for display, per-tenant settlement currency** (Booking.com/Expedia model). Overrides Q9 "no FX." Launch: single-tenant bookings stay single-currency; the FX/display layer lands with the cart. |
| Q3R/Q24R | 🟢 Cancellation **per-property** (owner picks one of the 2 models; platform sets tier numbers), **attached per line item, shown per-item in the cart** — not one identical global policy. |
| Q39R | 🟢 **One framework, per-property values, per-item display** (matches incumbents). NOT one identical policy platform-wide. |

**Design principle (locked 2026-07-13):** *standardize the framework/machinery (policy engine, tax model, currency-display, split-payment), localize the values (per-property).* This is what enables a cross-business cart at any time while preserving tenant autonomy. All Phase 3/4 design follows from it.

**All product/design questions are now resolved.** Remaining unknowns are technical-design (architect) not product — captured in the Phase 3/4 design doc.
