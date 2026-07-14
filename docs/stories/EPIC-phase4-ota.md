# EPIC — Phase 4: OTA Package / Itinerary Bundling (VRB-5xx)

> **Every story here inherits the global [Definition of Ready + Definition of Done](../ENGINEERING-RULES.md#definition-of-ready-before-you-write-the-first-test).** Before code: **claim the story on [`BOARD.md`](BOARD.md)** (first-push-wins), read it + its `blocked-by`, and **grep for an existing implementation before building one**. TDD; **write API contract tests for every endpoint you touch and keep the VRB-300 suite green**; stay in your lane ([`../plan/EXECUTION-PLAN.md`](../plan/EXECUTION-PLAN.md)); reuse the Phase-3 engine, don't fork it; on finish **self-heal the board + docs**. Operating model: [`../AGENT-PLAYBOOK.md`](../AGENT-PLAYBOOK.md). Build to the **corrected** design ([`../architecture/PHASE-3-4-DESIGN.md`](../architecture/PHASE-3-4-DESIGN.md) §0.5). Each story's own DoD is *in addition to* the global one.

**Status:** Design-complete, implementation-deferred (owner directive 2026-07-13 — Q23: *Phase 3/4 design + document NOW, defer only code*). **Priority:** Could (post-launch, strictly after Phase 3). **Author:** Platform enterprise architect.
**Companions (read first):** [`../architecture/PHASE-3-4-DESIGN.md`](../architecture/PHASE-3-4-DESIGN.md) §9 + **§0.5 corrections C1–C11**, [`../architecture/PHASE-3-4-DESIGN-REVIEW.md`](../architecture/PHASE-3-4-DESIGN-REVIEW.md), [`../product/COMPETITIVE-RESEARCH.md`](../product/COMPETITIVE-RESEARCH.md) §2/§3, [`../../OPEN-QUESTIONS.md`](../../OPEN-QUESTIONS.md) Q36–Q39.

---

## Why this epic reuses the Phase 3 engine (do NOT invent new checkout primitives)

Phase 4 is **an overlay, not a new engine.** Every OTA capability falls out of the Phase 3 foundation reshape (`docs/architecture/PHASE-3-4-DESIGN.md` §1, §7-step-4):

- An **itinerary is a Phase-3 `Order`** (`ordering.orders`, guest-scoped) with an agency-owned `Itinerary` overlay attached. "Buy the whole package" (Q37, `IsFixed=true`) = placing the parent Order **atomically** through the *existing* `PlaceOrder` serializable-transaction path — the exact atomic all-or-nothing cart from Phase 3 §2.2 (Q34).
- Each **leg is a Phase-3 `Reservation`** (`booking.reservations`, tenant-scoped) mapped 1:1, carrying its own `Policy` snapshot, `SettlementCurrency`, and status machine (`Tentative→Confirmed→…`, unchanged from `Booking.cs:141–284`).
- **Per-leg cancellation** (Q39) = the Phase-3 per-item cancel (§2.2, §6), aggregated by the Itinerary. No new refund path.
- **Cross-tenant settlement** rides the **CORRECTED** Phase-3 payment pipeline (C1): PI authorizes at Place → resolve every leg by the 48h SLA (C10) → **one partial capture** = Σ(confirmed legs) via `AmountToCapture` → **transfer per confirmed supplier**. Phase 4 adds exactly one thing: an **agency-margin layer** in the transfer fan-out (`supplierNet = lineTotal − platformFee − agencyMargin`), making it N+1 transfers instead of N.
- **FX** reuses Phase-3 §5's three-role model (Settlement / Charge / Display) + the C9 charge-currency/spread decision — unchanged.
- **RLS** reuses the "Order is the cross-tenant object, everything else is single-tenant" property (§4, corrected by C2: the PI is a *second* cross-tenant object, moved to platform/orchestration scope via `IRlsBypassDbContextFactory`).

**Hard dependency:** this epic is blocked-by the entire Phase 3 stack — the `Order`/`Reservation` split, `ReservableKind`/`ReservableId` polymorphism, `ordering` schema + `app.user_id` GUC, separate-charges-and-transfers with `payment.transfers`, the corrected capture pipeline (C1), PI platform re-scoping (C2), per-tenant flush in one serializable txn (C3), FX (§5, C9), and the cancellation engine (§6). Nothing here ships until Phase 3 is on staging.

**Owner decisions locked (OPEN-QUESTIONS Round 2/3, 2026-07-13):** Q36 = manual agency legs **(a)** + on-platform supplier tenants **(b)** now, external flight/car APIs later; Q37 = agency builds a **fixed package**, guest buys whole; Q38 = **split via Stripe multi-destination**, guest pays agency currency; Q39 = **per-leg** policies aggregated into the itinerary. Research confirms: packages are **atomic-at-purchase but per-leg-cancellable** (COMPETITIVE-RESEARCH §2/§3, the one Expedia data point).

---

## Summary table

| ID | Title | Est | Reuses (Phase-3 primitive) | Blocked-by |
|----|-------|-----|-----------------------------|-----------|
| VRB-500 | `Itinerary` aggregate + agency overlay over an Order | L | `ordering.orders`, `Order` atomic Place | Phase 3 foundation |
| VRB-501 | Leg polymorphism — `ReservableKind {Flight,Car,Activity}` + `IReservableResolver` seam | M | `ReservableKind`/`ReservableId`, resolver-per-kind | VRB-500 |
| VRB-502 | Agency-manual leg resolver (agency-owned reservable) | M | `Reservation`, quote/availability probe | VRB-501 |
| VRB-503 | On-platform-supplier leg resolver (cross-tenant resolve) | M | `MyBookingsHandler` per-tenant scope, resolver | VRB-501, VRB-505 |
| VRB-504 | External flight/car API resolver **seam** (adapter deferred, Q36) | S | `IReservableResolver` extension seam | VRB-501 |
| VRB-505 | `identity.tenant_supplier_relationships` + relationship-backed `ITenantStripeContextLookup` | M | `ITenantStripeContextLookup` contract (unchanged) | Phase 3 payments |
| VRB-506 | Cross-tenant revenue split — agency-margin layer, N+1 transfers | L | Corrected capture pipeline (C1), `payment.transfers` | VRB-500, VRB-505 |
| VRB-507 | Itinerary FX across supplier settlement currencies | M | FX §5 three-role model + C9 | VRB-500, VRB-506 |
| VRB-508 | Per-leg cancellation aggregated into the itinerary (Q39) | M | Per-item cancel + cancellation engine §6 | VRB-500, VRB-506 |
| VRB-509 | RLS — Itinerary→AgencyTenantId, legs→supplier, PI→platform (C2) | M | Per-statement GUC, `IRlsBypassDbContextFactory` | VRB-500, VRB-506 |
| VRB-510 | Agency package-builder UI (compose, sequence, set margin, publish) | L | Design-system, admin surface | VRB-500..503 |
| VRB-511 | Guest itinerary view + checkout UI | M | `StripePaymentForm`, Order checkout | VRB-500, VRB-506, VRB-507 |
| VRB-512 | Observability — N+1 transfer failures + margin accounting ledger | M | App Insights, `payment.transfers` | VRB-506, VRB-508 |

---

### VRB-500 — `Itinerary` aggregate: agency-owned overlay over a Phase-3 Order
- **Epic:** Phase 4 (OTA Bundling) · **Priority:** Could (post-launch) · **Estimate:** L
- **Narrative:** As a **travel-agency tenant**, I want to define a fixed package (an `Itinerary`) that overlays an ordinary Phase-3 `Order` and groups sequenced legs, so that a guest can buy a curated multi-leg trip in one atomic checkout without VrBook inventing a parallel checkout stack.
- **Acceptance criteria:**
  - **Given** an agency tenant, **when** it creates an itinerary, **then** an `Itinerary` row is persisted with `AgencyTenantId`, `OrderId` (FK to a `Draft` `ordering.orders`), `Title`, `IsFixed=true`, and an empty `_legs` set (`docs/architecture/PHASE-3-4-DESIGN.md` §9).
  - **Given** an itinerary, **when** a leg is added, **then** exactly one `ItineraryLeg` is created **1:1** with an Order `Reservation`, carrying `SequenceOrder` (int, unique within itinerary) and `SourceKind ∈ {AgencyManual, OnPlatformSupplier}`.
  - **Given** a published itinerary, **when** a guest places the parent Order, **then** the whole Order is placed **atomically** via the existing `PlaceOrder` serializable-txn path (`PlaceBookingHandler.cs:152–225` generalized) — all legs reserved or none (Q34/Q37); a single failed leg returns 409 identifying the leg and reserves nothing.
  - **Given** `IsFixed=true`, **when** a guest views the itinerary, **then** legs cannot be individually added/removed by the guest (fixed package) — only cancelled per-leg after purchase (VRB-508).
  - **Invariant:** the `Itinerary` holds **no Money total on the root** — totals are computed projections over the legs (§1.2, §5); the Order remains the checkout container.
- **TDD plan:**
  - *Unit:* `ItineraryTests.Add_leg_maps_1to1_to_reservation`; `..._sequence_order_unique_within_itinerary`; `..._is_fixed_blocks_guest_leg_mutation`; `..._no_money_total_on_root`.
  - *Integration (Testcontainers Postgres):* `ItineraryPlaceOrderTests.Place_fixed_package_is_atomic_all_legs_or_none`; `..._one_unavailable_leg_rolls_back_whole_order_409`.
  - *E2E:* `itinerary-place-atomic.e2e` (agency publishes → guest buys whole → all legs Tentative).
- **Technical notes:** New `Itinerary` aggregate + `ItineraryLeg` entity in a new `VrBook.Modules.Ota` module (or `ordering`-adjacent; architect to confirm placement so the `Order`↔`Itinerary` FK stays intra-schema-boundary-clean). New `ordering.itineraries` + `ordering.itinerary_legs` tables (itinerary co-locates with the Order it overlays; leg is a thin overlay pointing at a `booking.reservations` row by `ReservationId`, cross-schema loose ref like `Booking.PropertyId` today). Enum `ItineraryLegSourceKind { AgencyManual=0, OnPlatformSupplier=1 }`. Events: `ItineraryDrafted`, `ItineraryLegAdded`, `ItineraryPublished`. API: `POST /agency/itineraries`, `POST /agency/itineraries/{id}/legs`, `POST /agency/itineraries/{id}/publish`, `GET /agency/itineraries/{id}`, `GET /itineraries/{id}` (guest view). **Reuse:** the parent Order + its `PlaceOrder` handler are unchanged; the Itinerary is metadata + sequencing on top.
- **UI/UX spec:** Backend + aggregate story; UI in VRB-510/511. Emit machine-readable `sequence_order` and `source_kind` on the DTO so both front-ends render deterministic leg order.
- **Configuration:** `Ota:Enabled` flag (default **false** all envs). `Ota:MaxLegsPerItinerary` (default 12). No env secrets.
- **Rollout:** Feature flag `Ota:Enabled=false` everywhere at merge. Migration order: after all Phase-3 migrations (`ordering` schema must exist). Backward-compat: additive tables only; zero impact on non-OTA Orders. Rollback: drop `ordering.itineraries`/`itinerary_legs` (no FK from Order → Itinerary; the overlay points *down*, so Orders survive an Itinerary drop).
- **Observability:** Log `itinerary_id`, `agency_tenant_id`, `order_id`, `leg_count` on draft/publish/place. Metric `ota.itinerary.placed`.
- **Definition of Done:** unit+integration green → architect review of module placement → staging: agency creates + publishes + guest buys a 3-leg fixed package → prod verified (flag on for a pilot agency) → monitored 1 week.
- **Dependencies:** **blocked-by** Phase 3 foundation reshape (Order/Reservation split, `ordering` schema, atomic PlaceOrder). **blocks** VRB-501, 506, 508, 509, 510, 511.
- **Parallelisation:** Lane **A (domain)**. Owns `Modules.Ota/Domain/Itinerary.cs`, `ItineraryLeg.cs`, `ordering` itinerary migrations.

---

### VRB-501 — Leg polymorphism: `ReservableKind {Flight, Car, Activity}` + `IReservableResolver` per kind
- **Epic:** Phase 4 (OTA Bundling) · **Priority:** Could (post-launch) · **Estimate:** M
- **Narrative:** As a **travel-agency tenant**, I want legs of type Flight, Car, and Activity alongside Stay legs, so that a package can bundle heterogeneous travel components on one order without special-casing checkout per type.
- **Acceptance criteria:**
  - **Given** the `enum ReservableKind { Property=0, Room=1, Flight=2, Car=3, Activity=4 }` (extends the Phase-3 `{Property, Room}` per `docs/architecture/PHASE-3-4-DESIGN.md` §1.4), **when** a leg is added of any kind, **then** an `IReservableResolver` registered for that kind returns `(title, settlementCurrency, availabilityProbe, priceQuote)` and checkout stays kind-agnostic.
  - **Given** a Flight/Activity leg (point-in-time), **when** persisted, **then** `Reservation.Stay` is **null** (§1.3 "null for point-in-time legs"); a Car leg carries a date range in `Stay`.
  - **Given** an unknown/unregistered `ReservableKind`, **when** a leg add is attempted, **then** the command fails fast with `ota.reservable_kind_unsupported` (no partial itinerary).
  - **Extension-seam invariant:** adding a new leg type requires **only** a new `ReservableKind` value + a resolver registration — **no change** to `Order`, payment, or RLS (§1.4).
- **TDD plan:**
  - *Unit:* `ReservableResolverRegistryTests.Resolves_by_kind`; `..._unregistered_kind_throws`; `FlightLeg_has_null_stay`; `CarLeg_has_date_range_stay`.
  - *Integration:* `ItineraryLegQuoteTests.Mixed_kind_legs_quote_independently` (Stay+Flight+Car+Activity each quote in their own settlement currency).
  - *E2E:* covered by VRB-510/511 mixed-kind package build.
- **Technical notes:** Extend `ReservableKind` enum (currently a design construct — grep confirms it exists only in docs, introduced by the Phase-3 foundation story; this story appends `Flight/Car/Activity`). New `IReservableResolver { ReservableKind Kind; Task<ReservableDescriptor> ResolveAsync(Guid reservableId, ...); }` with a keyed-DI registry. `ReservableDescriptor(title, settlementCurrency, availabilityProbe, priceQuote)`. Resolvers land in VRB-502 (agency-manual), VRB-503 (on-platform), VRB-504 (external seam). **Reuse:** mirrors the Phase-3 pricing quote-engine resolution "by `(kind, reservableId)`" (§2.1) and the loose cross-module `(Kind, Id)` reference idiom.
- **UI/UX spec:** N/A (contract story). Descriptor `title`/`settlementCurrency` feed the builder + guest views.
- **Configuration:** `Ota:EnabledReservableKinds` (default `["Flight","Car","Activity"]`; external kinds gated by VRB-504's own flag). Under `Ota:Enabled=false`.
- **Rollout:** Enum values are additive (append-only — never renumber; `Property=0`/`Room=1` immutable). Migration: none (enum is code + int column already present from Phase 3). Rollback: resolvers unregister cleanly; enum values may remain unused.
- **Observability:** Metric `ota.leg.resolved{kind}`; log resolver latency per kind (external resolvers are the risk surface).
- **Definition of Done:** unit+integration green → review → staging: add one leg of each kind to a draft itinerary → prod pilot → monitored.
- **Dependencies:** **blocked-by** VRB-500. **blocks** VRB-502, 503, 504.
- **Parallelisation:** Lane **A (domain)**. Owns `Contracts/Enums/ReservableKind.cs` (append), `IReservableResolver` + registry.

---

### VRB-502 — Agency-manual leg resolver (agency-owned reservable)
- **Epic:** Phase 4 (OTA Bundling) · **Priority:** Could (post-launch) · **Estimate:** M
- **Narrative:** As a **travel-agency tenant**, I want to hand-enter a priced leg (a flight, car, or activity I arrange off-platform) with no live inventory, so that I can bundle components suppliers haven't listed on VrBook (Q36-a).
- **Acceptance criteria:**
  - **Given** an agency composing a leg with `SourceKind=AgencyManual`, **when** it enters title, price, currency, date/point-in-time, and capacity, **then** the manual resolver returns a `ReservableDescriptor` whose `availabilityProbe` **always succeeds** (no live inventory) and `priceQuote` is the agency-entered amount.
  - **Given** a manual leg, **when** the parent Order is placed, **then** the leg's `Reservation.TenantId = AgencyTenantId` (the agency owns the reservable) — settlement flows to the agency's own Connect account, no supplier transfer.
  - **Given** a manual leg, **when** cancelled (VRB-508), **then** its policy is the **agency-selected** `CancellationPolicy` snapshot (agency picks per leg from the §6 two-model engine).
- **TDD plan:**
  - *Unit:* `AgencyManualResolverTests.Availability_always_available`; `..._quote_is_agency_entered_amount`; `..._reservation_tenant_is_agency`.
  - *Integration:* `AgencyManualLegPlaceTests.Manual_leg_settles_to_agency_connect_account_no_transfer` (Testcontainers).
  - *E2E:* `agency-manual-leg.e2e` (agency adds a manual flight → guest buys → agency-only settlement).
- **Technical notes:** `AgencyManualReservableResolver : IReservableResolver` (Kind-agnostic within the manual set — a manual leg carries its own kind for display but availability is a no-op). Manual reservable stored as an agency-owned row (`ota.agency_manual_reservables`: `AgencyTenantId`, `Kind`, `Title`, `Amount`, `Currency`, `StartsAt?`, `EndsAt?`, `Capacity`). **Reuse:** the resulting `Reservation` is an ordinary tenant-scoped booking row where the "supplier" *is* the agency; the transfer fan-out (VRB-506) sees a single-tenant leg and applies **zero** agency margin (agency == supplier).
- **UI/UX spec:** Builder form fields in VRB-510: title, kind, price+currency, date range OR point-in-time, capacity, policy-model picker. Inline validation (price > 0, currency ISO-4217, end ≥ start).
- **Configuration:** none beyond `Ota:Enabled`.
- **Rollout:** Additive `ota.agency_manual_reservables` table. Rollback: drop table (only affects unpublished/unsold manual legs).
- **Observability:** Metric `ota.leg.manual.created`; log `agency_tenant_id`, `kind`, `amount_cents`, `currency`.
- **Definition of Done:** unit+integration green → review → staging manual-leg walk → prod pilot → monitored.
- **Dependencies:** **blocked-by** VRB-501. **blocks** VRB-506, VRB-508 (policy on manual legs), VRB-510.
- **Parallelisation:** Lane **B (resolvers)**. Owns `AgencyManualReservableResolver` + `ota.agency_manual_reservables`.

---

### VRB-503 — On-platform-supplier leg resolver (cross-tenant resolve)
- **Epic:** Phase 4 (OTA Bundling) · **Priority:** Could (post-launch) · **Estimate:** M
- **Narrative:** As a **travel-agency tenant**, I want to add a leg that resolves to **another VrBook tenant's** live listing (a supplier's stay or activity), so that the guest's package includes real on-platform inventory settled directly to that supplier (Q36-b).
- **Acceptance criteria:**
  - **Given** an active `tenant_supplier_relationships` row (VRB-505) between the agency and supplier, **when** the agency adds a leg with `SourceKind=OnPlatformSupplier` pointing at the supplier's reservable, **then** the resolver reads the supplier's listing **in the supplier's tenant scope** (per-tenant `BackgroundTenantScope`, the `MyBookingsHandler.cs:41–55` idiom) and returns the supplier's live `priceQuote` + `availabilityProbe` + `settlementCurrency`.
  - **Given** no active supplier relationship, **when** the agency tries to add that supplier's leg, **then** the command fails `ota.supplier_relationship_inactive` (an agency cannot resell an arbitrary tenant).
  - **Given** an on-platform leg at Place, **then** the leg's `Reservation.TenantId = supplierTenantId` and enters the supplier's own `Tentative→Confirmed` approval queue (the supplier confirms/rejects, per the corrected capture pipeline C1).
  - **Given** the supplier rejects or the 48h SLA (C10) expires, **then** only that leg is dropped from the partial capture; siblings unaffected.
- **TDD plan:**
  - *Unit:* `OnPlatformSupplierResolverTests.Requires_active_relationship`; `..._reads_supplier_scope`; `..._quote_is_supplier_live_price`.
  - *Integration (cross-tenant, Testcontainers):* `OnPlatformLegPlaceTests.Leg_enters_supplier_approval_queue`; `..._supplier_reject_drops_only_that_leg`; `..._sla_expiry_excludes_leg_from_partial_capture`.
  - *E2E:* `on-platform-supplier-leg.e2e` (agency bundles supplier B's activity → supplier B confirms in their queue → capture includes it).
- **Technical notes:** `OnPlatformSupplierReservableResolver : IReservableResolver`. Uses `BackgroundTenantScope(supplierTenantId)` to read `catalog`/`pricing` in the supplier's RLS scope; gates the read on an **active** relationship (VRB-505). The produced `Reservation` is ordinary tenant-scoped-to-supplier — a supplier querying `booking.reservations` sees their leg and is blind to siblings (§4, free). **Reuse:** identical cross-tenant read primitive as the Phase-3 cross-business cart (§2.2); no new RLS.
- **UI/UX spec:** Builder (VRB-510) supplier picker lists only tenants with an active relationship; shows live supplier price + currency; disables add if relationship inactive.
- **Configuration:** none beyond `Ota:Enabled` + relationship data (VRB-505).
- **Rollout:** No schema (reads existing supplier data + VRB-505 relationship). Rollback: unregister resolver.
- **Observability:** Metric `ota.leg.onplatform.resolved{supplier_tenant_id}`; log relationship check + supplier scope hop latency.
- **Definition of Done:** unit+integration+cross-tenant green → review → staging cross-tenant bundle walk → prod pilot (two consenting tenants) → monitored.
- **Dependencies:** **blocked-by** VRB-501, VRB-505. **blocks** VRB-506, VRB-510.
- **Parallelisation:** Lane **B (resolvers)**. Owns `OnPlatformSupplierReservableResolver`.

---

### VRB-504 — External flight/car API resolver seam (adapter deferred)
- **Epic:** Phase 4 (OTA Bundling) · **Priority:** Could (post-launch) · **Estimate:** S
- **Narrative:** As a **platform architect**, I want the external-inventory resolver designed as a first-class seam with the concrete GDS/airline/car adapter deferred, so that a future Amadeus/GDS integration slots in with no change to Order/payment/RLS (Q36-c, "external flight/car APIs post-Phase-4").
- **Acceptance criteria:**
  - **Given** the `IReservableResolver` contract, **when** an external resolver is registered for `Flight`/`Car`, **then** it conforms to the *same* `ResolveAsync → ReservableDescriptor` shape as manual/on-platform resolvers — the seam is proven by a `NotImplementedExternalResolver` stub that throws `ota.external_resolver_not_configured` behind an off-by-default flag.
  - **Given** `Ota:ExternalResolvers:Enabled=false` (all envs), **when** an agency attempts an external leg, **then** the UI hides the option and the API rejects with a clear "coming soon" problem type — no half-built code path reachable in prod.
  - **Documented seam:** an ADR/design note records the external adapter's future obligations (live availability, price locking, ticketing/booking-ref capture, its own settlement model — external suppliers are **not** Connect accounts, so revenue split VRB-506 does **not** apply; agency-as-MoR-reseller path documented as future work).
- **TDD plan:**
  - *Unit:* `ExternalResolverSeamTests.Stub_conforms_to_IReservableResolver`; `..._throws_not_configured_when_flag_off`.
  - *Integration:* none (deferred adapter).
  - *E2E:* none.
- **Technical notes:** `ExternalReservableResolverStub : IReservableResolver` for `Flight`/`Car`, throwing `NotSupportedException`-mapped-to-problem behind the flag. No external SDK dependency added now. **Reuse:** the seam *is* the `IReservableResolver` extension point from VRB-501 — this story only proves a third resolver family plugs in without touching the engine.
- **UI/UX spec:** Builder shows external Flight/Car options as **disabled with a "coming soon" tooltip** when the flag is off (WCAG 2.2 AA: disabled control has `aria-disabled` + visible reason text, not color-only).
- **Configuration:** `Ota:ExternalResolvers:Enabled` (default **false** all envs). Placeholder for future `Amadeus:*` / `TravelportCar:*` secrets — seeded as `pending-identity-setup` in KV **only if/when** the adapter lands (per the KV-bind-before-Bicep rule); nothing referenced from Bicep now.
- **Rollout:** Pure seam; no migration. Rollback: remove stub registration.
- **Observability:** Metric `ota.leg.external.attempted_while_disabled` (signals demand for the deferred adapter).
- **Definition of Done:** unit green → architect review of the seam contract + future-work ADR → merged flag-off → no prod exposure (flag off). (No staging/prod feature verification — deferred adapter.)
- **Dependencies:** **blocked-by** VRB-501. **blocks** nothing (future external adapter epic).
- **Parallelisation:** Lane **B (resolvers)**. Owns `ExternalReservableResolverStub` + seam ADR.

---

### VRB-505 — `tenant_supplier_relationships` + relationship-backed `ITenantStripeContextLookup`
- **Epic:** Phase 4 (OTA Bundling) · **Priority:** Could (post-launch) · **Estimate:** M
- **Narrative:** As a **platform**, I want agency↔supplier links modeled as a **relationship table** (not an enum), so that an agency can resell an on-platform supplier under a negotiated margin, and Stripe routing resolves through the relationship — **without changing the `ITenantStripeContextLookup` contract** (pre-committed in its doc-comment).
- **Acceptance criteria:**
  - **Given** the new table `identity.tenant_supplier_relationships (agency_tenant_id, supplier_tenant_id, status ∈ {Pending,Active,Suspended}, agency_margin_bps, created_at, …)`, **when** an agency requests a supplier link and the supplier accepts, **then** `status` transitions `Pending→Active` and both directions are queryable.
  - **Given** `ITenantStripeContextLookup.GetAsync(tenantId)` (contract at `src/VrBook.Contracts/Interfaces/ITenantStripeContextLookup.cs:15–17`), **when** re-implemented against the relationship data, **then** its **return shape is unchanged** — `TenantStripeContext(TenantId, StripeAccountId, PlatformFeeBps, DefaultCurrency)` — because "a booking still resolves to one supplier even in the multi-supplier topology" (doc-comment lines 10–13). Existing callers (`CreatePaymentIntentForBookingHandler`, `RefundForBookingHandler`, `HandleStripeWebhookCommand`) compile and pass unchanged.
  - **Given** an inactive/suspended relationship, **when** an agency tries to bundle that supplier (VRB-503) or settle to it (VRB-506), **then** it is rejected `ota.supplier_relationship_inactive`.
  - **Given** `agency_margin_bps`, **then** it is the sole source of the agency margin used by VRB-506 (`supplierNet = lineTotal − platformFee − agencyMargin`).
- **TDD plan:**
  - *Unit:* `TenantSupplierRelationshipTests.Pending_to_active_on_accept`; `..._suspend_blocks_resell`; `MarginBps_validated_0_to_10000`.
  - *Integration:* `RelationshipStripeContextLookupTests.Contract_return_shape_unchanged`; `..._existing_refund_and_webhook_callers_unaffected` (Testcontainers, RLS on).
  - *E2E:* relationship accept/suspend via agency + supplier admin UIs (VRB-510).
- **Technical notes:** New `TenantSupplierRelationship` entity in `Modules.Identity`; migration adds `identity.tenant_supplier_relationships` (RLS: readable by *both* the agency and supplier tenant — a two-party row; policy `agency_tenant_id = app.tenant_id OR supplier_tenant_id = app.tenant_id OR app.is_platform_admin`). Re-implement the `ITenantStripeContextLookup` impl in Identity to source routing through the relationship graph while keeping `TenantStripeContext` identical. **Do not** change the interface (`ITenantStripeContextLookup.cs`). Note: the doc-comment names `tenant_connect_accounts`; the design (§9) settled on `tenant_supplier_relationships` — reconcile the doc-comment naming to the shipped table name in this story. **Reuse:** the interface is the seam; only the impl swaps (exactly as pre-committed).
- **UI/UX spec:** Agency admin: "Suppliers" management page — request link, set `agency_margin_bps`, view status. Supplier admin: "Reseller requests" — accept/decline/suspend. Both under `admin/` surface, `AdminSidebar` entries. WCAG 2.2 AA: status as text+icon (not color-only); margin input labeled with bps→% helper.
- **Configuration:** `Ota:DefaultAgencyMarginBps` (default 0). Under `Ota:Enabled=false`.
- **Rollout:** Additive `identity.tenant_supplier_relationships` table (+ additive RLS policy; existing isolation policies untouched, §8 pattern). Migration runs in Identity (first in cross-schema order — the [cross-schema migration trap] does not apply here since the table lives in `identity` and references only `identity.tenants`). Backward-compat: the relationship-backed lookup returns the same shape, so single-tenant Phase 1–3 payments are byte-identical. Rollback: revert impl to the pre-Phase-4 `IdentityDbContext.Tenants` lookup + drop the table.
- **Observability:** Metric `ota.supplier_relationship.status{status}`; alert on lookup regressions (compare context resolution latency pre/post swap). Log every margin change with actor + old/new bps for audit.
- **Definition of Done:** unit+integration green (**including the existing refund/webhook caller regression suite**) → architect review of the contract-preserving swap → staging: link two tenants, verify existing single-tenant refund still works → prod pilot → monitored.
- **Dependencies:** **blocked-by** Phase 3 payments (`payment.transfers`, corrected pipeline). **blocks** VRB-503, VRB-506.
- **Parallelisation:** Lane **C (identity/payments)**. Owns `Modules.Identity/…/TenantSupplierRelationship*`, the `ITenantStripeContextLookup` impl, `identity.tenant_supplier_relationships` migration.

---

### VRB-506 — Cross-tenant revenue split: agency-margin layer, N+1 transfers on the corrected capture pipeline
- **Epic:** Phase 4 (OTA Bundling) · **Priority:** Could (post-launch) · **Estimate:** L
- **Narrative:** As the **platform**, I want the transfer fan-out to add an agency-margin layer so that, on one guest PaymentIntent, each confirmed supplier is paid `supplierNet = lineTotal − platformFee − agencyMargin`, the agency receives its aggregated margin, and the platform keeps its fee — riding the **corrected** Phase-3 capture pipeline (C1), not the rejected capture-on-first-confirm.
- **Acceptance criteria:**
  - **Given** a placed itinerary Order, **when** legs resolve by the 48h SLA (C10) — each supplier Confirms / Rejects / expires — **then** the pipeline does **exactly one partial capture** = Σ(confirmed leg `lineTotal`) via Stripe `AmountToCapture` (C1 — **never** capture-on-first-confirm, which would charge for unapproved legs, DESIGN-REVIEW R3).
  - **Given** N confirmed on-platform-supplier legs under one agency, **when** capture completes, **then** the fan-out issues **N supplier transfers** (`supplierNet` each) **+ 1 agency transfer** (Σ `agencyMargin` across those legs) = **N+1 transfers on one PI** (§9 "N+1 transfers on one PI"), plus the platform retains Σ `platformFee`.
  - **Given** `supplierNet = lineTotal − platformFee − agencyMargin`, **then** `platformFee = ApplicationFeeCents(lineTotal, tenant.PlatformFeeBps)` and `agencyMargin = lineTotal × relationship.agency_margin_bps / 10000` (VRB-505); the three components + rounding reconcile to `lineTotal` exactly (no cents lost).
  - **Given** an **agency-manual** leg (VRB-502, supplier==agency), **then** `agencyMargin = 0` for that leg (no self-transfer) and it settles to the agency Connect account directly.
  - **Given** the PI is a **second cross-tenant object** (C2), **then** it is read/written via `IRlsBypassDbContextFactory` at platform/orchestration scope; `payment.transfers` rows carry the per-tenant axis (agency vs each supplier).
  - **Given** a leg cancelled after capture (VRB-508), **then** its refund **reverses that leg's supplier transfer AND the proportional slice of the agency-margin transfer** — no sibling leg or the platform fee of siblings affected.
- **TDD plan:**
  - *Unit:* `RevenueSplitTests.SupplierNet_equals_lineTotal_minus_platformFee_minus_agencyMargin`; `..._components_reconcile_to_lineTotal_no_cents_lost`; `..._manual_leg_has_zero_margin`; `AgencyMarginAggregation_sums_across_legs_of_one_agency`.
  - *Integration (cross-tenant split payment, Testcontainers + Stripe test mode):* `ItineraryCaptureTests.Single_partial_capture_equals_sum_confirmed_legs`; `..._N_supplier_transfers_plus_one_agency_transfer`; `..._rejected_leg_excluded_from_capture_and_transfers`; `..._per_leg_refund_reverses_supplier_and_proportional_agency_margin`; `..._PI_read_via_rls_bypass_platform_scope`.
  - *E2E:* `itinerary-revenue-split.e2e` (2 suppliers + 1 agency margin → verify 3 transfers + platform fee on one PI in Stripe test dashboard).
- **Technical notes:** Extends the Phase-3 `IStripeGateway.CreateTransferAsync(...)` fan-out with an agency-margin transfer per itinerary. `StripeGateway` currently has **no transfer path** (verified: `StripeGateway.cs` only does destination charges `TransferData.Destination` + `ApplicationFeeAmount` + `OnBehalfOf` at lines 65–72; separate-charges-and-transfers arrives in Phase 3). Capture uses `PaymentIntentCaptureOptions.AmountToCapture` (not the current parameterless `CaptureAsync`, `StripeGateway.cs:87`). New `payment.transfers` rows gain a `role ∈ {Supplier, AgencyMargin}` + `itinerary_id` column. Idempotency: `StripeIdempotency.ForTransfer(reservationId)` (supplier legs) + `ForAgencyMargin(itineraryId)` (the single aggregated agency transfer). **Reuse:** the entire capture→transfer pipeline is Phase-3's (C1-corrected); Phase 4 adds one transfer row type + the margin arithmetic. **Depends on C4/C5 being fixed at launch** — real application-fee reversal (`ApplicationFeeRefundService`) must exist so per-leg refunds claw back the platform fee correctly.
- **UI/UX spec:** No direct UI. Guest checkout (VRB-511) shows one total in the agency's charge currency; the split is invisible to the guest. Agency dashboard (VRB-510) shows per-itinerary expected margin.
- **Configuration:** `Ota:AgencyMarginTransferEnabled` (default false). Reuses `Payment:*` Stripe config. Under `Ota:Enabled=false`.
- **Rollout:** Additive `payment.transfers` columns (`role`, `itinerary_id`) — nullable, backfill N/A (no OTA rows pre-launch). Migration after Phase-3 `payment.transfers` exists. Rollback: agency-margin transfers are additive; disabling the flag reverts to pure supplier fan-out (Phase-3 behavior). **Never** re-introduce capture-on-first-confirm.
- **Observability:** **Critical.** Metric `ota.transfer.created{role}`, `ota.transfer.failed{role}` (N+1 transfer failure is the headline risk). Alert on any transfer failure after a successful capture (funds captured but not fully distributed = money stuck on platform). Structured margin-accounting log per itinerary: `itinerary_id`, `captured_cents`, `Σ platform_fee`, `Σ agency_margin`, `Σ supplier_net`, reconciliation delta (must be 0). Dashboard in VRB-512.
- **Definition of Done:** unit+integration+cross-tenant split green → architect review of the money-reconciliation math (delta==0 proof) → staging: place 2-supplier itinerary, confirm capture + 3 transfers in Stripe test mode → prod pilot with real reconciliation audit → monitored 2 weeks with the margin-accounting alert armed.
- **Dependencies:** **blocked-by** VRB-500, VRB-505, and Phase-3 separate-charges-and-transfers + C1 capture + C4 fee-reversal. **blocks** VRB-507, VRB-508, VRB-511, VRB-512.
- **Parallelisation:** Lane **C (identity/payments)**. Owns the transfer fan-out extension, `payment.transfers` deltas, capture-amount logic.

---

### VRB-507 — Itinerary FX across supplier settlement currencies
- **Epic:** Phase 4 (OTA Bundling) · **Priority:** Could (post-launch) · **Estimate:** M
- **Narrative:** As a **guest**, I want to pay for a multi-supplier package in one currency (the agency's charge currency) while each supplier settles in their own currency, so that a cross-border itinerary is one comprehensible total — reusing the Phase-3 FX model, no new machinery.
- **Acceptance criteria:**
  - **Given** legs in mixed supplier settlement currencies, **when** the itinerary is quoted, **then** each leg's `Money` stays in its **settlement currency**; the guest sees one **display/charge total** in the agency's currency computed via `IFxRateProvider` (Phase-3 §5), and the FX rate is **locked at Place** in `Order.FxRateSnapshot` for reproducible receipts/refunds.
  - **Given** the C9 commercial decision (charge currency + who bears the ~1% cross-currency-transfer spread), **when** implemented, **then** the resolved policy from the Phase-3 cart (guest via marked-up display **or** platform margin **or** supplier net) is applied **identically** here — Phase 4 does not re-open C9, it inherits it.
  - **Given** a per-leg refund (VRB-508), **then** the refund computes in the **leg's settlement currency** (no FX round-trip loss, §5) using the locked snapshot.
  - **Given** a single-supplier, single-currency itinerary, **then** FX is skipped (charge==settlement==display, rate=1) — same launch fast-path as Phase 3.
- **TDD plan:**
  - *Unit:* `ItineraryFxTests.Display_total_sums_legs_via_fx_at_read_time`; `..._fx_locked_at_place`; `..._refund_computed_in_settlement_currency`; `..._single_currency_skips_fx`.
  - *Integration:* `ItineraryFxSnapshotTests.Receipt_reproducible_from_locked_snapshot` (Testcontainers).
  - *E2E:* `itinerary-fx-display.e2e` (USD agency, EUR + GBP suppliers → one USD total).
- **Technical notes:** Reuses `IFxRateProvider` (cached, daily refresh, §5/Q6) + `Order.FxRateSnapshot` — **no new FX component**. `Money.Add` still refuses cross-currency (`Money.cs:29`), forcing per-currency `Money` collections + a display projection over the itinerary legs. The C9 spread-incidence config is a shared Phase-3 setting; Phase 4 reads it. **Reuse:** 100% Phase-3 §5; this story only wires the itinerary's leg collection into the existing display projection + confirms the spread policy applies to the N+1 transfer split.
- **UI/UX spec:** Guest itinerary view (VRB-511): one bold total in charge currency; expandable per-leg breakdown shows each leg's native settlement currency + the applied rate ("€120 ≈ $131 at 1.09"). Agency builder shows the same. WCAG 2.2 AA: rate disclosure is text, not tooltip-only; currency codes spelled in `aria-label`.
- **Configuration:** reuses `Fx:*` (provider, refresh cadence) + the C9 `Payment:FxSpreadBearer` setting from Phase 3. Under `Ota:Enabled=false`.
- **Rollout:** No new schema (reuses `Order.FxRateSnapshot`). Rollback: single-currency fast-path unaffected; multi-currency itineraries gated by the flag.
- **Observability:** Metric `ota.itinerary.fx_applied`; log locked rate + spread-bearer per itinerary for receipt reproducibility + margin reconciliation (ties to VRB-506's delta==0).
- **Definition of Done:** unit+integration green → review that C9 policy is inherited not re-derived → staging mixed-currency itinerary → prod pilot → monitored.
- **Dependencies:** **blocked-by** VRB-500, VRB-506, and Phase-3 FX (§5) + the C9 decision. **blocks** VRB-511.
- **Parallelisation:** Lane **C (identity/payments)** for the split-side; Lane **D (frontend)** for the display projection. Owns the itinerary FX display projection + refund-currency wiring.

---

### VRB-508 — Per-leg cancellation aggregated into the itinerary
- **Epic:** Phase 4 (OTA Bundling) · **Priority:** Could (post-launch) · **Estimate:** M
- **Narrative:** As a **guest**, I want to cancel a single leg of my package under that leg's own policy while keeping the rest, so that a package behaves the way OTAs do — **atomic at purchase, per-leg at cancellation** (Q39; COMPETITIVE-RESEARCH §2 Expedia data point).
- **Acceptance criteria:**
  - **Given** a purchased itinerary, **when** the guest cancels one leg, **then** the Phase-3 **per-item cancel** runs for exactly that `Reservation` — its snapshotted `CancellationPolicy` (§6, Tiered or Refundable-rate-upgrade) resolves the refund; siblings are untouched.
  - **Given** the cancelled leg, **when** the refund executes, **then** it reverses that leg's supplier transfer **and** the proportional agency-margin slice (VRB-506) via the reservation-keyed `RefundForBookingCommand`, in the leg's settlement currency (VRB-507).
  - **Given** the itinerary aggregates leg statuses, **when** a leg is cancelled, **then** the `Itinerary` recomputes an aggregate status (e.g. `PartiallyCancelled`) and the guest/agency views reflect per-leg state; the parent Order stays `Confirmed`/`PartiallyConfirmed` for the remaining legs.
  - **Given** a Tentative (not-yet-confirmed) leg at the 48h SLA, **then** it is auto-expired + fully released (no transfer, no fee) — reusing the existing expiry worker, aggregated into the itinerary view.
  - **Given** the guest cancels while a leg is still within its refundable window vs after, **then** the two §6 models resolve correctly (Tiered %tier; Refundable-upgrade full-if-before-checkin-else-0), filling the per-item `RefundAmount` (today's `Booking.cs:194` `RefundAmount=0` TODO, per leg).
- **TDD plan:**
  - *Unit:* `ItineraryCancellationTests.Cancel_one_leg_leaves_siblings`; `..._aggregate_status_partially_cancelled`; `..._tiered_and_upgrade_models_resolve_per_leg`.
  - *Integration (Testcontainers):* `PerLegRefundTests.Refund_reverses_supplier_and_proportional_margin_in_settlement_currency`; `..._tentative_leg_sla_expiry_releases_without_transfer`.
  - *E2E:* `itinerary-per-leg-cancel.e2e` (buy 3 legs → cancel 1 → refund correct, 2 legs live).
- **Technical notes:** New `CancelItineraryLegCommand` that resolves the leg's `Reservation` and dispatches the **existing** reservation-keyed `RefundForBookingCommand` (`RefundForBookingCommand.cs`), then recomputes itinerary aggregate status. Guest is tenant-less → the `TenantAuthorizationBehavior` `BackgroundTenantScope` fallback (lines 80–94) resolves the leg's supplier tenant from the row (§4, already ships for single-booking guest cancel). **Reuse:** zero new refund primitive — §6 cancellation engine + Phase-3 per-item cancel + VRB-506 transfer reversal. The Itinerary only *aggregates* leg states for display.
- **UI/UX spec:** Guest itinerary view (VRB-511): each leg card has a "Cancel this leg" action showing the leg's policy + computed refund preview *before* confirm (`ConfirmActionModal` pattern, `web/src/components/ui/ConfirmActionModal.tsx`). Aggregate itinerary status chip. WCAG 2.2 AA: refund amount + policy in text; destructive action needs explicit confirm; focus management on modal.
- **Configuration:** none beyond `Ota:Enabled`. Reuses §6 policy config.
- **Rollout:** No schema (aggregate status is a computed/denormalized field on `Itinerary`). Rollback: per-leg cancel falls back to whole-order cancel if disabled.
- **Observability:** Metric `ota.leg.cancelled{policy_model}`, `ota.leg.refund_amount_cents`; alert on refund/transfer-reversal failures (partial-refund money-stuck risk, ties to VRB-512). Log aggregate-status recompute.
- **Definition of Done:** unit+integration green → review of per-leg refund + margin-reversal correctness → staging per-leg cancel walk (both policy models) → prod pilot → monitored.
- **Dependencies:** **blocked-by** VRB-500, VRB-506, VRB-507, Phase-3 §6 cancellation engine + C4 real fee reversal. **blocks** VRB-511 (cancel UI), VRB-512.
- **Parallelisation:** Lane **A (domain)** for aggregation + Lane **C** for refund/reversal. Owns `CancelItineraryLegCommand` + Itinerary aggregate-status logic.

---

### VRB-509 — RLS: Itinerary→AgencyTenantId, legs→supplier, Order guest-scoped, PI→platform (C2)
- **Epic:** Phase 4 (OTA Bundling) · **Priority:** Could (post-launch) · **Estimate:** M
- **Narrative:** As the **platform**, I want the itinerary's data isolation to reuse the Phase-3 "Order is the cross-tenant object" property — the Itinerary scoped to the agency, each leg to its supplier, the Order to the guest, the PI to the platform — so that no RLS policy ever expresses "tenant A OR tenant B" and cross-tenant leakage is structurally impossible.
- **Acceptance criteria:**
  - **Given** `ordering.itineraries`, **when** RLS is applied, **then** the policy is `agency_tenant_id = app.tenant_id OR app.is_platform_admin` — an agency sees only its own itineraries; a supplier never reads the itinerary root.
  - **Given** `booking.reservations` (legs), **then** the **unchanged** Phase-3 tenant-scoped policy applies — a supplier sees only their own leg; sibling legs in the same itinerary are invisible **for free** (§4).
  - **Given** the guest reads the whole itinerary, **then** the read iterates-per-tenant-scope-and-merges (`MyBookingsHandler.cs:41–55` idiom) — the guest-scoped Order root + each leg fetched in its supplier scope.
  - **Given** the PaymentIntent is a **second cross-tenant object** (C2, DESIGN-REVIEW R2 — `payment.payment_intents` is tenant-scoped today per `RefundForBookingCommand.cs:65` `pi.TenantId != cmd.TenantId`), **then** the itinerary PI root is read/written at **platform/orchestration scope** via `IRlsBypassDbContextFactory` (audited, allowlisted); `payment.transfers` carry the per-tenant axis.
  - **Given** the atomic multi-supplier Place (C3, DESIGN-REVIEW R1 — the interceptor fires per DbCommand and EF batches INSERTs), **then** the Place **flushes per tenant** (N `SaveChanges`, one `BackgroundTenantScope` each) **within one serializable transaction** so each leg INSERT passes its own tenant's `WITH CHECK`.
- **TDD plan:**
  - *Unit:* `ItineraryRlsPolicyTests.Agency_sees_own_only`; `Supplier_cannot_read_itinerary_root`.
  - *Integration (RLS on, Testcontainers):* `CrossTenantItineraryIsolationTests.Supplier_sees_only_own_leg`; `..._guest_merges_legs_across_supplier_scopes`; `..._PI_root_read_via_bypass_platform_scope`; `PerTenantFlushTests.Mixed_tenant_batch_flushes_per_tenant_in_one_serializable_txn` (proves C3 — a naive single batch would fail `WITH CHECK`).
  - *E2E:* covered by VRB-511 cross-tenant purchase.
- **Technical notes:** Additive RLS policy on `ordering.itineraries` (mirrors the Phase-3 `ordering.orders` guest policy shape but on `agency_tenant_id`). PI re-scoping is a **Phase-3 §8 item (C2)** that Phase 4 depends on — confirm it shipped. The per-tenant flush (C3) is likewise Phase-3's atomic PlaceOrder; Phase 4 exercises it with M supplier legs + agency-manual legs. **Reuse:** `TenantGucCommandInterceptor` (`app.tenant_id` + `app.user_id` GUCs, per-statement), `IRlsBypassDbContextFactory`, `TenantAuthorizationBehavior` fallback — **all unchanged**; this story adds one policy + tests the load-bearing per-tenant-flush path under itinerary shape. **`TenantAuthorizationBehavior` needs NO change** (§4).
- **UI/UX spec:** N/A (isolation story).
- **Configuration:** none. RLS policies are schema, not flags.
- **Rollout:** Additive `ordering.itineraries` RLS policy; existing isolation policies untouched (§8). Migration with the VRB-500 table create. Rollback: drop the policy with the table.
- **Observability:** Alert on any `pi.TenantId != cmd.TenantId` guard trip for itinerary PIs (would signal the PI wasn't platform-scoped). Metric `ota.rls.bypass_scope.opened` (audit the allowlisted bypass usage).
- **Definition of Done:** RLS integration tests green (**including the negative cross-tenant leakage tests + the C3 per-tenant-flush proof**) → security review of the bypass-scope usage → staging cross-tenant isolation walk → prod → monitored.
- **Dependencies:** **blocked-by** VRB-500, VRB-506, Phase-3 C2 (PI platform re-scoping) + C3 (per-tenant flush) + `app.user_id` GUC. **blocks** VRB-511.
- **Parallelisation:** Lane **A (domain)** + Lane **C (payments)**. Owns the `ordering.itineraries` RLS policy + the itinerary-shaped per-tenant-flush tests.

---

### VRB-510 — Agency package-builder UI (compose legs, sequence, set margin, publish)
- **Epic:** Phase 4 (OTA Bundling) · **Priority:** Could (post-launch) · **Estimate:** L
- **Narrative:** As a **travel-agency tenant**, I want a package-builder UI to compose legs (manual + on-platform-supplier), order them, set my margin, and publish a fixed package, so that I can sell curated multi-leg trips — a complete vertical slice, not a headless API.
- **Acceptance criteria:**
  - **Given** the agency admin, **when** it opens the builder, **then** it can create an itinerary (title), **add legs** of each kind — Manual (VRB-502 form) or On-platform-supplier (VRB-503 supplier picker, only active-relationship suppliers), **reorder** legs by `SequenceOrder` (drag or up/down, reusing the `SortableRuleRow` pattern from `web/src/components/pricing/SortableRuleRow.tsx`), **set** `agency_margin_bps`, and **publish**.
  - **Given** a draft with a live-priced on-platform leg, **when** displayed, **then** each leg shows its native settlement currency + live price; the builder shows the composed guest total in the agency charge currency (VRB-507) + expected margin (VRB-506).
  - **Given** `Ota:ExternalResolvers:Enabled=false`, **then** external Flight/Car options are disabled with a "coming soon" affordance (VRB-504).
  - **Given** validation errors (empty title, 0 legs, invalid margin, inactive supplier), **then** publish is blocked with inline, accessible error messaging.
  - **States covered:** empty (no itineraries), draft (composing), published (read-only + unpublish), sold (has orders — margin actuals), error/loading.
- **TDD plan:**
  - *Unit (Vitest):* `PackageBuilder.test.tsx` — add/remove/reorder legs, margin input bps↔%, publish gating, external-disabled state.
  - *Integration:* MSW-mocked API contract for `POST /agency/itineraries` + legs + publish.
  - *E2E (Playwright):* `agency-package-builder.e2e` — compose a 3-leg mixed package → publish → appears in guest catalog.
- **Technical notes:** New route `web/src/app/admin/itineraries/` (list) + `web/src/app/admin/itineraries/[id]/page.tsx` (builder). New components under `web/src/components/ota/` (`PackageBuilder`, `LegComposer`, `SupplierLegPicker`, `MarginInput`, `ItinerarySummary`). Reuses design-system: `SortableRuleRow` (sequencing), `ConfirmActionModal` (publish/unpublish), `AdminSidebar` entry. **Reuse:** admin surface patterns from `web/src/app/admin/pricing/page.tsx`.
- **UI/UX spec:** Responsive two-pane (leg list + summary/margin), collapses to single-column stacked on mobile. Design-system tokens (Tailwind, brand palette). WCAG 2.2 AA: drag-reorder has keyboard equivalent (up/down buttons with `aria-label`); margin input labeled with bps + computed %; all state chips text+icon; focus-visible; error text tied via `aria-describedby`; disabled external options `aria-disabled` + reason. Loading skeletons; optimistic reorder with rollback on failure.
- **Configuration:** `Ota:Enabled` gates the nav entry + routes (default false all envs). `NEXT_PUBLIC_OTA_ENABLED` mirror for client gating.
- **Rollout:** Web behind `Ota:Enabled`. Deploy after backend VRB-500..506. Rollback: flag off hides the surface entirely (no orphaned nav).
- **Observability:** Client analytics: builder funnel (create→add-leg→publish drop-off), publish success/fail. Metric via existing web telemetry.
- **Definition of Done:** Vitest + Playwright green → design review (frontend-design skill) → staging: agency composes + publishes a mixed package end-to-end → **owner UI test in browser** (per [test-through-UI]) → prod pilot → monitored.
- **Dependencies:** **blocked-by** VRB-500, VRB-501, VRB-502, VRB-503, VRB-505. **blocks** nothing (guest side is VRB-511).
- **Parallelisation:** Lane **D (frontend)**. Owns `web/src/app/admin/itineraries/**`, `web/src/components/ota/**` (agency components).

---

### VRB-511 — Guest itinerary view + checkout
- **Epic:** Phase 4 (OTA Bundling) · **Priority:** Could (post-launch) · **Estimate:** M
- **Narrative:** As a **guest**, I want to view a published fixed package with its sequenced legs, per-leg policies, and one total in my charge currency, then buy it whole in one checkout, so that booking a multi-supplier trip feels like one purchase.
- **Acceptance criteria:**
  - **Given** a published itinerary, **when** a guest opens it, **then** it renders legs **in `SequenceOrder`** with each leg's title, kind, dates/time, native price+currency, and **per-leg cancellation policy shown per-item** (Q3R/Q39R "per-item display"), plus one bold total in the agency charge currency with an expandable FX breakdown (VRB-507).
  - **Given** the guest checks out, **when** they pay, **then** the **existing** Order checkout + `StripePaymentForm` (`web/src/components/booking/StripePaymentForm.tsx`) places the parent Order atomically (one PI, agency charge currency) — buy-whole (Q37); a single unavailable leg fails the whole checkout with a clear message (Q34).
  - **Given** a purchased itinerary, **when** the guest views it under their account, **then** each leg shows live status (Tentative/Confirmed/Cancelled) and a **per-leg "Cancel this leg"** action with a refund preview (VRB-508).
  - **States:** browse (published catalog), detail (legs + total), checkout (Stripe), confirmation (per-leg Tentative), post-purchase management (per-leg status + cancel), error/loading/unavailable-leg.
- **TDD plan:**
  - *Unit (Vitest):* `ItineraryView.test.tsx` — leg order, per-leg policy display, FX breakdown expand, buy-whole CTA.
  - *Integration:* MSW-mocked `GET /itineraries/{id}` + `POST /orders/{id}/place`.
  - *E2E (Playwright):* `guest-itinerary-checkout.e2e` — view → buy whole → per-leg Tentative → cancel one leg.
- **Technical notes:** New routes `web/src/app/itineraries/page.tsx` (catalog) + `web/src/app/itineraries/[id]/page.tsx` (detail/checkout) + surface in `web/src/app/account/` for post-purchase management. New components `web/src/components/ota/ItineraryView`, `LegCard`, `FxBreakdown`, reusing `StripePaymentForm`, `CancelBookingButton`/`ConfirmActionModal`, `PriceQuoteWidget` patterns. **Reuse:** the checkout is the Phase-3 Order checkout unchanged; this is a presentation + per-leg-cancel wrapper.
- **UI/UX spec:** Responsive: leg timeline (vertical, sequence-ordered) collapsing gracefully on mobile; sticky total/CTA. Design-system tokens. WCAG 2.2 AA: leg sequence conveyed by numbered headings + `aria`, not visual line only; FX rate as text; per-leg policy in an accessible disclosure; cancel is a clearly-labeled destructive action with confirm + refund preview; payment form focus management; error/unavailable states announced via live region.
- **Configuration:** `NEXT_PUBLIC_OTA_ENABLED` gates guest routes (default false all envs).
- **Rollout:** Web behind the flag; deploy after VRB-506/507/508 backend. Rollback: flag off returns 404 on itinerary routes (no dangling links from catalog when off).
- **Observability:** Client funnel: view→checkout→confirm; per-leg-cancel usage; unavailable-leg checkout failures. Ties to VRB-512 backend metrics.
- **Definition of Done:** Vitest + Playwright green → design review → staging: guest buys a published package whole + cancels one leg → **owner UI test in browser** → prod pilot → monitored.
- **Dependencies:** **blocked-by** VRB-500, VRB-506, VRB-507, VRB-508, VRB-509. **blocks** nothing.
- **Parallelisation:** Lane **D (frontend)**. Owns `web/src/app/itineraries/**`, guest `web/src/components/ota/**`.

---

### VRB-512 — Observability: N+1 transfer failures + margin-accounting ledger
- **Epic:** Phase 4 (OTA Bundling) · **Priority:** Could (post-launch) · **Estimate:** M
- **Narrative:** As a **platform operator**, I want dedicated observability for the N+1 transfer fan-out and per-itinerary margin accounting, so that captured-but-undistributed funds and margin drift are caught before they become financial incidents.
- **Acceptance criteria:**
  - **Given** an itinerary capture + fan-out, **when** any of the N+1 transfers fails after a successful capture, **then** an alert fires immediately (funds captured, not fully distributed = money stuck on platform) with `itinerary_id`, failed `role` (Supplier/AgencyMargin), and reconciliation delta.
  - **Given** each settled itinerary, **then** a **margin-accounting ledger** records `captured_cents`, `Σ platform_fee`, `Σ agency_margin`, `Σ supplier_net`, and `delta` (must be 0); a non-zero delta raises a P1 alert.
  - **Given** per-leg refunds/reversals (VRB-508), **then** the ledger updates and a reversal that fails to claw back the proportional platform fee (C4 dependency) or agency margin alerts.
  - **Given** the operator, **then** an App Insights dashboard shows: transfer success rate by role, capture-vs-distributed reconciliation, margin totals per agency, SLA-expiry drop counts.
- **TDD plan:**
  - *Unit:* `MarginLedgerTests.Delta_is_zero_on_balanced_itinerary`; `..._non_zero_delta_flagged`.
  - *Integration:* `TransferFailureAlertTests.Failed_transfer_after_capture_emits_alert` (Testcontainers + Stripe test mode transfer failure injection).
  - *E2E:* synthetic itinerary → force a transfer failure in Stripe test mode → alert fires.
- **Technical notes:** Structured logs already emitted by VRB-506/508; this story adds the **aggregation + alerting + dashboard**. New `payment.margin_ledger` projection (or a view over `payment.transfers` + capture rows). App Insights custom metrics + Kusto alert rules (Bicep-declared, matching the existing App Insights wiring). **Reuse:** existing App Insights + Log Analytics infra (`infra/main.bicep`); no new observability stack.
- **UI/UX spec:** Operator dashboard is an App Insights workbook (not app UI). If a platform-admin in-app surface is wanted, a read-only `admin/platform/itinerary-reconciliation` page (deferred sub-story).
- **Configuration:** Alert thresholds in Bicep params (default: any post-capture transfer failure = immediate; delta ≠ 0 = P1). Under `Ota:Enabled` (no OTA traffic = no alerts).
- **Rollout:** Additive `payment.margin_ledger` view/table + Bicep alert rules. KV/secret: none new. Rollback: drop the view + alert rules (no impact on transfers themselves).
- **Observability:** This story *is* the observability. Self-check: the alert-fires E2E is the DoD proof.
- **Definition of Done:** unit+integration green → **synthetic transfer-failure alert verified firing in staging** → dashboard reviewed by operator → prod with alerts armed → monitored through the first real itinerary settlements.
- **Dependencies:** **blocked-by** VRB-506, VRB-508. **blocks** nothing (final ops-hardening story).
- **Parallelisation:** Lane **C (payments/infra)**. Owns `payment.margin_ledger`, Bicep alert rules, App Insights workbook.

---

## Epic-level Definition of Done

- All VRB-500..512 stories green (unit + integration + cross-tenant split-payment + E2E) with the `Ota:Enabled` flag proving off-by-default zero-impact on Phase 1–3.
- Money reconciliation proven: for every itinerary, `captured = Σ platform_fee + Σ agency_margin + Σ supplier_net` (delta == 0), including after per-leg refunds.
- The "Order is the cross-tenant object (+ PI at platform scope, C2)" isolation property holds under negative cross-tenant leakage tests.
- **No new checkout/payment/refund primitive was introduced** — every capability traces to a Phase-3 engine reuse cited above.
- ADR-0019 (OTA bundling scope + the external-resolver deferral) written; `docs/MASTER_PLAN.md` Slice 10 row flipped; close-out `docs/OPS_?_CLOSE_OUT.md` written.
- Owner UI-tested the agency builder + guest checkout in the browser on staging.

## Parallelisation lane map

| Lane | Scope | Stories | Key files owned |
|------|-------|---------|-----------------|
| A — domain | Itinerary aggregate, polymorphism, aggregation, RLS policy | 500, 501, 508(agg), 509(policy) | `Modules.Ota/Domain/**`, `Contracts/Enums/ReservableKind.cs`, `ordering.itineraries` migrations |
| B — resolvers | The three resolver families | 502, 503, 504 | `*ReservableResolver`, `ota.agency_manual_reservables` |
| C — identity/payments | Relationship table, revenue split, FX split, refunds, observability | 505, 506, 507(split), 508(refund), 509(flush), 512 | `TenantSupplierRelationship`, `ITenantStripeContextLookup` impl, transfer fan-out, `payment.transfers`/`margin_ledger` |
| D — frontend | Agency builder + guest views | 507(display), 510, 511 | `web/src/app/admin/itineraries/**`, `web/src/app/itineraries/**`, `web/src/components/ota/**` |

Lanes A/B/C share the `Modules.Ota` boundary — coordinate the aggregate ↔ resolver ↔ payment contracts up-front (VRB-500 + VRB-501 land the contracts before B/C fan out).
