# VrBook — Product Requirements Document

**Status:** COMPLETE — for GATE 2 review (Phase 2 of the spec program). Pass 2a (implemented) + 2b (planned, from [`PHASE-3-4-DESIGN.md`](../architecture/PHASE-3-4-DESIGN.md)) + 2c (competitive) + personas/journeys + NFR + operational + compliance + metrics + out-of-scope. All decisions locked in [`OPEN-QUESTIONS.md`](../../OPEN-QUESTIONS.md). Prioritization: **MoSCoW**.
**Grounding:** current behavior cited against [`docs/architecture/CURRENT-STATE.md`](../architecture/CURRENT-STATE.md).

---

## 1. Product vision & positioning

**VrBook is a commission-free, direct-booking platform for vacation rentals and small hotels.** Owners list properties and take bookings + payments directly from guests, escaping the 15–20% OTA (Airbnb/Booking.com) commission. It is a **multi-tenant SaaS**: each business (tenant) runs independently with its own listings, Stripe Connect account, calendars, and branding, while VrBook charges a small **platform fee** (default 15%, platform-admin-configurable) instead of an OTA-scale commission.

**The wedge:** owners keep the guest relationship and margin; guests get the same property for less. **The moat we build toward:** a cross-business cart + OTA package bundling (Phase 3/4) that lets guests assemble multi-property, multi-supplier trips in one checkout — capability the direct-booking niche lacks today.

**Locked design principle:** standardize the framework (policy engine, tax, currency-display, split-payment); localize the values per property. This enables a cross-business cart at any time without forcing tenants onto identical rules.

---

## 2. Personas

| Persona | Who | Primary goals |
|---|---|---|
| **Guest** | Traveler booking a stay | Find a property, get a transparent quote (incl. taxes/fees), book + pay securely, manage/cancel bookings, message the host, review after stay; (Phase 3/4) assemble a multi-property/multi-supplier trip in one checkout |
| **Host / Property Owner** (`tenant_admin`) | The business listing properties | Onboard + connect Stripe, create/manage listings (incl. rooms), set pricing rules + fees + cancellation policy, manage calendar + iCal sync, approve/reject/confirm bookings, message guests, moderate reviews, pull revenue/occupancy reports |
| **Platform Admin** | VrBook operator | Onboard/suspend tenants, set per-tenant platform fee, configure global cancellation-tier numbers + tax posture, manage amenities catalog, pre-seed admins, monitor the platform |
| **Travel Agency** (Phase 4, a tenant variant) | Tour operator composing packages | Build fixed itineraries spanning stays + flights + cars + activities from manual legs + on-platform suppliers; sell as one product with per-leg cancellation + split settlement |

---

## 3. End-to-end journeys (happy paths)

**Guest booking (launch):** search `/properties` → property detail + JSON-LD (`/properties/[slug]`) → pick dates/guests → anonymous quote (nightly + fees + **Stripe Tax**) → sign in (email/pw or Google) → place booking (card **authorized**, status **Tentative**, 48h SLA) → host confirms → **card captured** → confirmation email (from a DKIM-verified domain) → stay → review + loyalty tick. Cancellation follows the property's chosen policy (tiered or refundable-upgrade).

**Host lifecycle:** invite → Entra-local admin sign-in (pre-seeded) → onboarding wizard → Stripe Connect Express onboarding → create listing (whole-property or room-types) + photos + pricing rules + fees + cancellation policy → receive Tentative bookings → confirm/reject in ≤3 clicks → calendar + iCal in/out → messaging → reports.

**Platform admin:** invite tenants, set fees, configure global cancellation tiers + tax, suspend/reactivate, monitor.

**Guest cross-business cart (Phase 3/4):** browse across businesses → add items from multiple tenants to a cart → one **atomic** checkout in the guest's **display currency** → one PaymentIntent → **N transfers** to each supplier (native currency), platform keeps fees → per-item cancellation. **Agency package (Phase 4):** agency builds a fixed itinerary (stays + flights + cars + activities) → guest buys whole → per-leg cancellation + split settlement.

---

## 4. Pass 2a — Implemented capabilities (as-built)

Extracted from code; each marked ✅ Implemented / ◑ Partial / ⛔ Stubbed. Full citations in CURRENT-STATE.md.

### Guest / booking
- ✅ Property **search** (anonymous, SSR, SEO/JSON-LD), **detail by slug**, **availability** query.
- ✅ Anonymous **price quote** (base + weekend + rules + fees). ◑ **Tax** not yet integrated (Stripe Tax is a Must-story).
- ✅ **Booking lifecycle**: hold → place (Tentative, race-safe via Postgres serializable + `SELECT FOR UPDATE`) → confirm/reject/cancel/check-in/check-out/complete. Manual capture.
- ✅ **My bookings** + booking detail + cancel.
- ✅ **Reviews** (one per booking, owner reply, moderation) + **loyalty** tier tracking (◑ no guest benefit yet, by decision).
- ✅ **Messaging** guest↔host thread per confirmed booking. ⛔ attachments (501).
- ✅ **Payments**: Stripe Connect Express, destination charges, manual capture, refunds, signature-verified webhooks w/ idempotency. ◑ disputes log-only.

### Host / tenant admin
- ✅ **Onboarding wizard** + Stripe Connect Express onboarding/readiness.
- ✅ **Listings** CRUD. ⛔ **image upload/order/delete via API (501)** — a Must-fix (a listing needs photos).
- ✅ **Pricing**: plan (base/weekend/min-max stay) + 3 rule kinds (DateRangeOverride / LastMinute / LengthOfStay) w/ drag-reorder; ◑ fees model exists; ◑ cancellation-policy config is a Must-story (2 models).
- ✅ **Calendar** + availability blocks + **iCal** inbound poll (per-host rate-limited) + outbound feed.
- ✅ **Booking queue** via list + detail confirm/reject. ⛔ dedicated admin queue + manual booking (501).
- ✅ **Reviews moderation**, **notifications retry**, **sync conflicts**, **channel feeds** CRUD.
- ✅ **Reports**: occupancy / revenue / ADR / source + CSV + realtime dashboard pill.

### Platform admin
- ✅ **Tenant console** (list/detail/suspend/reactivate/platform-fee/memberships), **admin pre-seed**, **amenities catalog** CRUD.
- ⛔ **toggles/alerts** (501); **feature-flag runtime** is a no-op stub; **Admin bounded-context module** is a stub.

### Platform / cross-cutting
- ✅ **Multi-tenancy**: tenant aggregate + memberships, `tenant_id` on every owned table, **Postgres RLS** isolation + platform-admin bypass, cross-tenant isolation test pack.
- ✅ **Auth**: Entra External ID (CIAM) + MSAL, admin-Entra-local vs guest-social split, DB-authoritative roles, admin pre-seed gate.
- ✅ **Notifications**: ACS email + templates + queue/dispatch worker.
- ✅ **Realtime**: SignalR (booking push). ◑ Redis holds available but not deployed.
- ✅ **Observability**: Serilog + App Insights + PII redaction.
- ⚠️ **Outbox**: transactional write + in-process dispatch; **cross-process relay not built** (acceptable at launch scale, by decision).

---

## 5. Pass 2b — Planned features (Phase 3 / Phase 4)

> **Design-now / implement-later** per owner directive. Full technical design: [`docs/architecture/PHASE-3-4-DESIGN.md`](../architecture/PHASE-3-4-DESIGN.md). The unifying architecture is one engine, N front-ends: **split today's `Booking` into a guest-scoped `Order` (checkout container) + tenant-scoped `Reservation`s** referencing a polymorphic `Reservable` (`Property | Room | Flight | Car | Activity`). Today's whole-house booking becomes an `Order` with one `Reservation` — **zero guest-facing change**. Priorities are all **Could (post-launch)** by owner decision; the *design* is a Must-do-now deliverable (this program).

### 5.0 Foundation reshape (Phase 3 prerequisite)
Split `Booking → Order + Reservation`; add `ReservableKind`/`ReservableId`; new `ordering` schema (guest-scoped) + `app.user_id` RLS GUC; keep single-property bookings behaving identically. **Cheap-now / expensive-later** items to land first: the `ordering` schema + `app.user_id` GUC, and `ReservableKind` on `Reservation`.

### 5.1 Phase 3 — Hotel-style rooms
- **Capability:** owner chooses per property — whole-house listing (today) OR **Room Types with an inventory count** ("5× Deluxe King").
- **Details:** `RoomType` = child of `Facility` (renamed `Property`); per-room-type pricing, count-based availability, capacity, photos, iCal; amenities at both levels; reviews at facility level. A room booking = `Reservation(Room)`, one per sellable unit.
- **Notable work:** the `properties → facilities` multi-schema rename wave (~2 days across catalog/pricing/reviews/sync/messaging), backward-compatible via `ListingMode=WholeHouse` default + view shims.

### 5.2 Phase 3 — Multi-unit + cross-business cart
- **Capability:** guest assembles items from **one or many tenants** → one **atomic** (all-or-nothing) checkout → one PaymentIntent → **N transfers** to N supplier Connect accounts; **per-item cancel**; mixed-currency via display-currency + FX.
- **Details:** `Order` drafts accumulate `Reservation`s; atomic Place extends the existing serializable-txn guard to N cross-tenant lines; payment switches to Stripe **separate charges & transfers** when >1 tenant; the Order is the only cross-tenant object and it's **guest-scoped**, so per-tenant RLS never changes (the per-statement binding + iterate-per-scope idiom already exist).

### 5.3 Phase 4 — OTA package bundling
- **Capability:** a travel-agency tenant composes a **fixed itinerary** (stay + flight + car + activity legs) from **manual legs + on-platform supplier tenants**, sold whole, with **per-leg cancellation** + split settlement + FX.
- **Details:** `Itinerary` = agency-owned overlay over a Phase-3 `Order`; legs are `Reservation`s with new `ReservableKind`s; supplier relationship is a **new `tenant_supplier_relationships` table** (not an enum); transfer fan-out gains an agency-margin layer. **Reuses the Phase-3 cart/payment/refund engine** — no new checkout primitive. External flight/car APIs are a later resolver (seam designed now).

### 5.4 Cross-cutting engines (serve launch + Phase 3/4)
- **Cancellation/refund engine** (Must at launch, extended in Phase 3): 2 owner-selected models (tiered w/ platform-set global tiers; refundable-rate upgrade), snapshotted per line, per-item resolution.
- **Stripe Tax + marketplace-facilitator** (Must at launch): per-line-jurisdiction tax on the platform account; platform collects+remits.
- **Split-payment pipeline** (Phase 3): separate-charges-and-transfers, per-tenant fee, per-item refund + transfer reversal.
- **Currency/FX** (Phase 3): settlement/charge/display roles, FX for display only, snapshot at Place.

**Open technical questions** (not product — from the design §10): manual-capture-across-N settlement timing, room-inventory lock location, fee-reversal accounting precision, outbox-relay dependency, `app.user_id` fail-safe-deny, FX rate source. Resolved during implementation slices.

> **Validated 2026-07-13** against cited market research ([`COMPETITIVE-RESEARCH.md`](COMPETITIVE-RESEARCH.md)) + an independent architect review. The design's 11 corrections are in [`PHASE-3-4-DESIGN.md`](../architecture/PHASE-3-4-DESIGN.md) §0.5 — **read that block first**. Two review findings are **launch Must-fixes** (added to §6 below); the rest are Phase-3/4 story-level refinements (add a RatePlan dimension, per-state facilitator tax, explicit FX-incidence decision, inventory counter-row lock, 48h SLA, mixed-policy cart display).

---

## 6. Pass 2c — Competitive expansion (recommendations)

Benchmarked against Airbnb, Booking.com, Vrbo, and direct-booking/PMS tooling (Lodgify, Hostaway, Guesty). Focus: capabilities that **defend the commission-free value prop** or close a table-stakes gap. Priority = MoSCoW for launch; Phase = when.

| Candidate | What it is | Why it matters for direct-booking | Effort | Priority |
|---|---|---|---|---|
| **Stripe Tax + fee transparency** | Automated lodging/sales tax + clear fee breakdown at quote | Legal correctness + trust; OTAs already show all-in pricing | M | **Must (launch)** |
| **Cancellation-policy engine (2 models)** | Owner-selected policy + platform tiers + refundable upgrade | Table stakes; drives conversion + refund correctness | M | **Must (launch)** |
| **Listing photos (upload/manage)** | The 501 image endpoints + gallery mgmt UI | A photoless listing doesn't sell — hard blocker | M | **Must (launch)** |
| **Real application-fee reversal on refund** (review C4) | Actually call `ApplicationFeeRefundService` + persist `fee_reversal_cents` — today it's metadata-only, so platform fees aren't clawed back on refund | Refund correctness / platform revenue integrity | S | **Must (launch)** |
| **Reconcile MoR / tax posture** (review C5) | Drop `OnBehalfOf=supplier` on the single-tenant charge so the platform is genuinely merchant-of-record for the marketplace-facilitator tax posture | Tax-liability correctness; else supplier is legally MoR, contradicting Stripe-Tax-as-facilitator | S | **Must (launch)** |
| **Mobile-first booking funnel** | Responsive nav + mobile checkout (currently no mobile nav) | >60% of travel traffic is mobile | M | **Must (launch)** |
| **SEO + direct-traffic engine** | Sitemap, canonical, structured data (partly present), per-property SEO metadata, fast Core Web Vitals | Direct booking LIVES on organic + owner's own traffic; this is the moat vs OTAs | M | **Must (launch)** |
| **Guest reviews w/ host response** | ✅ built | Trust/conversion | — | (done) |
| **Instant Book (auto-capture) option** | Owner opt-in to skip manual approval | Conversion; Airbnb default | S | Should (post-launch) |
| **Multi-photo + floor plans + map** | Richer listing media | Parity | M | Should |
| **Damage deposit / security hold** | Stripe auth-hold for incidentals | Owner protection | M | Should |
| **Promo codes / discounts** | Owner-issued codes | Direct-channel marketing lever | M | Should |
| **Channel manager (2-way API push)** | Beyond iCal — Airbnb/Booking API sync | Prevents double-booking for multi-channel owners | L | Could (Phase 2) |
| **Cross-business cart / OTA packages** | Phase 3/4 | The differentiator | XL | Could (Phase 3/4) |
| **Dynamic pricing suggestions** | Demand-based rate hints | Revenue uplift; PMS parity | L | Could |
| **Native mobile apps** | iOS/Android | Retention | XL | Won't (launch) |

---

## 7. Non-functional requirements

- **Performance:** booking-funnel **Core Web Vitals** LCP < 2.5s, INP < 200ms, CLS < 0.1; API **P95 < 1s** at **50 RPS** sustained (validated by k6 against a prod-sized target).
- **Security:** OWASP ASVS baseline; Stripe **SAQ-A** (hosted/Elements — no card data on our servers); WAF (prod Front Door); dependency + image + secret scanning (Trivy/ZAP one-time pre-launch, then recurring); fail-fast config validation (currently missing — Must-fix); no test-auth backdoors (arch-test enforced).
- **Accessibility:** **WCAG 2.2 AA** as a best-effort target (not a hard launch block); baseline roles/aria present, needs an audit + mobile nav + focus management.
- **i18n:** **English-only** at launch; currency **single-per-tenant** at launch, display-currency+FX with the Phase 3 cart.
- **SEO:** SSR public pages, JSON-LD, canonical URLs, sitemap.xml + robots.txt (verify/add), per-property meta — **launch-critical for direct traffic**.
- **Data retention:** booking/financial 7y; messages 2y post-checkout; PII erasure on request (financial carve-out); account soft→hard delete.

---

## 8. Operational requirements

- **Availability:** **99.5%** target. **RPO ≤ 1h** (Postgres PITR), **RTO ≤ 4h** — restore must be **tested**, not assumed (current gap).
- **Backup/restore:** PG Flexible Server automated backups + a **tested** restore drill.
- **Observability:** structured logs (Serilog), App Insights metrics + dashboards, **alert rules with real thresholds + an owner** (P95 latency, error rate, webhook failures, PG CPU, notification-dispatch failures) — compensating control for the deferred k6/ZAP CI gates.
- **Deployment:** **a prod pipeline must exist** (current gap — only staging), blue-green/slots + post-deploy smoke, **tested rollback** (current gap).
- **Support:** **2-week hypercare** post-launch, daily review week 1, owner + engineering on-call, incident/runbook process.

---

## 9. Compliance & legal

- **PCI:** SAQ-A via Stripe hosted/Elements — confirmed scope.
- **Privacy:** **CCPA/CPRA + GDPR-ready** (US-first, global guests). Retention per §7. "Delete my data" flow. Sub-processor DPAs (Stripe, Azure).
- **Cookie consent:** banner with necessary vs analytics categories.
- **Legal surfaces:** **VrBook drafts** Terms of Service, Privacy Policy, and the per-property cancellation-policy display; **governing law: Ohio, USA**; owner reviews. (Entity name TBC for the drafts.)
- **Tax:** **Stripe Tax**; VrBook as **marketplace facilitator** collects + remits US lodging/sales tax (per-state remittance confirmed with accountant); emailed receipts with tax breakdown.

---

## 10. Success metrics (launch)

- **Activation:** % invited tenants that complete Stripe onboarding + publish ≥1 listing.
- **Funnel:** search → quote → booking conversion; Tentative→Confirmed rate; time-to-confirm.
- **Revenue:** GBV, platform-fee revenue, ADR, occupancy.
- **Reliability:** uptime ≥ 99.5%, P95 < 1s, webhook success ≥ 99.9%, email deliverability (DKIM/DMARC pass) ≥ 99%.
- **Quality:** blocking-smoke pass rate, zero un-triaged HIGH security findings.

---

## 11. Out of scope (launch)

Self-serve tenant signup (invite-only), Facebook/Apple/Microsoft social login, native mobile apps, channel-manager 2-way API, dynamic-pricing engine, outbox→Service Bus relay, Redis hold store, multi-currency FX (arrives with Phase 3 cart), Phase 3/4 implementation (designed now, built later), loyalty guest benefits.

---

*GATE 2: owner approves scope + priorities before Phase 3 (user stories). The P0/P1 items in [`docs/ops/CURRENT-GAPS.md`](../ops/CURRENT-GAPS.md) each become their own story (owner directive).*
