# VrBook — Competitive / Market-Standards Research (cited)

**Method:** `deep-research` workflow — 5 search angles → 21 sources fetched → 90 claims → 25 adversarially verified (2/3-refute kill rule) → 24 confirmed, 1 refuted. Prioritized primary sources (official docs, Stripe docs, tax guidance). Date: 2026-07-13.
**Purpose:** ground the PRD §6 competitive analysis + the Phase 3/4 design in real market evidence (replacing earlier from-memory assertions), and flag where our design should change. Feeds the independent architect review.

> Source-strength note: mechanics 1 (rooms), 4 (cancellation), 5 (tax), 6 (FX), 7 (settlement) rest on **strong primary sources** (unanimous 3-0 votes). Mechanics 2–3 (cross-supplier carts / package bundling **product** internals) are only **indirectly** evidenced — the Stripe plumbing that enables them is confirmed, but how Expedia/Booking.com assemble packages internally is not publicly documented.

---

## 1. Room-type & inventory modeling — ✅ validates our design

- **Booking.com** sells a **"roomrate" = room type × rate plan** with a per-combination **inventory count** (its ARI model: Availability, Rates, Inventory). "A room type is a label/category used to organise physical rooms"; inventory = total rooms across types; availability = the bookable portion. A **decrementing count per room-type**, not named rooms. [developers.booking.com/connectivity/docs/ari, .../understanding-room-types-and-rate-plans]
- **Guesty** models three immutable listing types: **single-unit**, **multi-unit** (interchangeable pool of identical sub-units — any available one is allocated at booking), **complex** (hotel-like, heterogeneous room types built from single/multi units). [help.guesty.com/.../9364048715421]
- **Implication:** our **RoomType + InventoryCount** decision (PHASE-3-4-DESIGN §2.1) matches the industry exactly. Guesty's trichotomy maps cleanly to our `ListingMode`: single-unit = WholeHouse, multi-unit = one RoomType(inventory N), complex = facility with N RoomTypes. **Adopt the clearer trichotomy naming.**
- **⚠️ Refinement — RATE PLANS.** The incumbents' sellable unit is room-type × **rate plan**, where the **rate plan carries the cancellation policy + prepayment + meal-plan + restrictions**. Our design attaches policy to the reservation/line but has **no explicit rate-plan concept** — yet our Q24 "refundable-rate upgrade" **is** a rate plan (e.g. "Refundable +$X" vs "Non-refundable −10%"). **Recommend:** model a `RatePlan` dimension per reservable (price × policy × prepayment), even if launch ships one default plan + the refundable upgrade. This is the standard mechanism and it makes the two cancellation models fall out naturally.

## 2. Multi-item / cross-supplier carts — plumbing validated; product internals unverified

- **Verified (Stripe):** for one-checkout-many-sellers, **only "separate charges and transfers"** can split a single charge across multiple connected accounts (direct + destination charges each bind to ONE account). Stripe names "a single cart that contains goods from different manufacturers" as the canonical use. [docs.stripe.com/connect/charges, .../separate-charges-and-transfers]
- **Unverified:** whether Expedia/Booking.com package checkout is atomic, and their internal order-assembly, are **not publicly documented** (flagged as an open question).
- One (lower-reliability) Expedia help source indicated a **vacation package is cancelled per-leg (lodging/flight/car/activity separately), each under its own policy** — i.e. atomic at *booking*, but **per-leg at cancellation**.
- **Implication:** our engine (`Order` = separate-charges-and-transfers when >1 tenant; per-item cancel) is **exactly** the correct Stripe mechanic. **✓ core payment design confirmed.** The "buy-whole fixed package" (Q37) should explicitly support **per-leg cancellation** (which our design already does) — packages are whole at purchase, granular at cancel.

## 3. OTA package bundling — same as §2

Product-internal bundling mechanics are undocumented publicly; the payment/settlement layer is the same separate-charges-and-transfers model. Our Itinerary-overlay-over-an-Order design (PHASE-3-4-DESIGN §9) is consistent with the evidence; the "fixed package, per-leg cancel" shape matches the one Expedia data point.

## 4. Cancellation engines — ✅ validates per-property; refine granularity later

- **Vrbo:** **5 named tiers** (Relaxed / Moderate / Firm / Strict / No Refund), each a **two-window refund schedule** by days-before-check-in (e.g. Relaxed = 100% ≥14d, 50% 7–14d), **configured PER LISTING**, with seasonal overrides + custom refunds (must be ≥ the policy). [help.vrbo.com/articles/what-are-the-cancellation-policy-options]
- **⚠️ REFUTED (0-3):** Airbnb does **not** clearly use the same named tiers — **do not generalize Vrbo's structure to Airbnb.**
- **Implication:** our decision — **per-property policy, two-window tiers, platform-set numbers, + refundable-rate upgrade** — matches Vrbo's proven pattern. The incumbents offer *more* named tiers than our single tiered model; that's a fine post-launch granularity increase (our engine already supports it via the tier table). ✓ our per-property + snapshot + per-item design is sound.

## 5. Marketplace-facilitator lodging tax — ✅ posture validated; ⚠️ "all states" oversimplifies

- **Vrbo** auto-collects + remits lodging tax **where legally required or under a tax agreement; hosts cannot opt out** there. [help.vrbo.com/.../What-Stay-Taxes...HomeAway-collect-and-remit]
- **But it's per-state:** a growing number of states *require* STR marketplaces to collect (e.g. **Virginia** requires OTA collect+remit; **Washington** does NOT treat platforms as facilitators for the net amount). Airbnb collects+remits in ~30 states. Crucially: **"collected ≠ remitted,"** and **hosts may retain independent registration/filing obligations** even when the platform collects. [Avalara MyLodgeTax 2024/2025]
- **Implication:** our "platform collects + remits, **all US states** via Stripe Tax" (Q25) is **directionally right but oversimplified**. Reality: (a) **facilitator status + who remits is per-state** — Stripe Tax *calculates* the tax, but does not decide whether VrBook or the host is the statutory remitter; (b) need **per-state facilitator config** + the **collected-vs-remitted** distinction + a host-obligation disclosure. **Recommend:** a P1 refinement — model facilitator/remittance per state, start with the states that mandate it, and surface host-side obligations. Confirm the remittance operating model with the accountant (already your Q25 intent).

## 6. Multi-currency + FX — ✅ capability validated; commercial FX incidence open

- **Stripe Connect multi-currency settlement:** connected accounts can **hold + payout in up to 18 currencies** without converting (subject to ~1% payout fee, same-region, minimums). [docs.stripe.com/connect/multicurrency-settlement]
- **Unresolved (open question):** whether the guest is ultimately charged in **display** vs **property** currency, and **who bears the FX spread** commercially — not publicly documented for the OTAs.
- **Implication:** our per-tenant-settlement + display-currency+FX model (PHASE-3-4-DESIGN §5) is **supported by Stripe's capability**. The **commercial FX decision** (charge currency + who absorbs the ~1% spread) is a genuine open product decision to make when the cart lands — flag it, don't assume.

## 7. Split / settlement payment models — ✅ validates our selector

- **Stripe MoR by charge type:** direct → connected account is MoR; **destination (default) → platform is MoR**; indirect + `on_behalf_of` → connected account is MoR. The MoR is "responsible for … applicable regulations and liabilities, **including sales taxes**." [docs.stripe.com/connect/merchant-of-record, .../charges]
- **Expedia** illustrates the axis: **Property/Hotel Collect** (property is MoR, traveler pays at check-in) vs **Expedia Collect** (platform is MoR). [developers.expediagroup.com/rapid/lodging/booking/property-collect]
- **Implication:** our design — **destination charge for 1 tenant, separate-charges-and-transfers for M tenants, platform as MoR for tax (marketplace facilitator)** — is precisely aligned. **✓ confirmed.** Note the coupling the research makes explicit: **MoR (charge type) and tax liability are the same axis** — because we want the *platform* to be the tax facilitator, we must keep the platform as MoR (destination/plain-indirect, **not** `on_behalf_of` the supplier). Our design already does this; the research makes the reason precise.

---

## Net verdict: does the research change the design?

**Mostly validates it.** The core moves — RoomType+inventory, per-property cancellation, separate-charges-and-transfers split payment, platform-as-MoR-for-tax, per-tenant multi-currency settlement — all match proven market patterns with primary-source backing.

**Three refinements to fold in (for the architect review to rule on):**
1. **Add a `RatePlan` dimension** (price × policy × prepayment per reservable) — the incumbent-standard mechanism; makes our two cancellation models + future granularity natural. (Design refinement.)
2. **Per-state marketplace-facilitator tax** — replace "all US states" with per-state facilitator/remittance config + "collected ≠ remitted" + host-obligation disclosure. (P1 tax-design refinement.)
3. **Make the FX commercial incidence an explicit open decision** (charge currency + who bears the ~1% spread) — resolve when the cart lands, don't assume.

**Open questions the market evidence could NOT answer (carry forward):** internal package-assembly atomicity at Expedia/Booking.com; mixed-policy cart *display* conventions; concrete per-state facilitator thresholds; commercial FX-spread bearer. None block the design; all are refinements.

**Refuted / do-not-assume:** Airbnb does not use Vrbo's five named cancellation tiers.
