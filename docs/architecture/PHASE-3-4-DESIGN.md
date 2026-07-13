# VrBook — Phase 3 + Phase 4 Technical Design

**Status:** Design-complete, implementation-deferred (owner directive 2026-07-13 — design & document now, defer only code). **Author:** Platform enterprise architect. **Date:** 2026-07-13.
**Scope:** Hotel-style rooms, multi-unit + cross-business cart (Phase 3), OTA package bundling (Phase 4).
**Companions:** [`CURRENT-STATE.md`](CURRENT-STATE.md), [`../PHASE_3_RECONNAISSANCE.md`](../PHASE_3_RECONNAISSANCE.md), [`../ops/CURRENT-GAPS.md`](../ops/CURRENT-GAPS.md), decisions in [`../../OPEN-QUESTIONS.md`](../../OPEN-QUESTIONS.md).

Every claim is verified against the code as-built. Source of truth for the PRD "Planned" capabilities + Phase 3/4 stories. It defines *the framework/machinery to standardize and the values to localize per-property* — the locked design principle.

---

## 0. What the code already gives us (verified baseline)

1. **The line-item model is half-built and price-only.** `Booking` (`src/Modules/VrBook.Modules.Booking/Domain/Booking.cs`) owns `_lineItems` (L68) + `_guests`, but `PropertyId`/`PropertyTitle` are root scalars (L22–23), money is root scalars (L31–39), `CancellationPolicy` is a single root enum (L40). `BookingLineItem` carries only `Kind/Label/Quantity/UnitAmount/LineTotal` — no `ReservableId`, `ReservableKind`, `TenantId`, `Policy`, currency, or tax. `Booking.Place(...)` takes one `propertyId` (L77); `BookingPlaced` carries one `PropertyId` (`Contracts/Events/BookingEvents.cs:20`).
2. **Payment is `BookingId`-keyed + single-destination.** `PaymentIntent` = 1 row per `BookingId`. `StripeGateway` uses **destination charges** (`TransferData.Destination` + `ApplicationFeeAmount` + `OnBehalfOf`, `StripeGateway.cs:65–72`), manual capture. One PI → one connected account. No separate-charges-and-transfers path today.
3. **RLS binding is already per-statement** (the recon's load-bearing decision — implemented correctly). `TenantGucCommandInterceptor` runs `set_config('app.tenant_id',…,true)` before **every** command (L87–89); resolves `ICurrentUser.TenantId → BackgroundTenantScope → empty` (fail-safe deny).
4. **Cross-tenant reads already have a working idiom.** `MyBookingsHandler` (L41–55) loops opening a `BackgroundTenantScope` **per tenant** and merges — the exact primitive a cross-business cart read needs.
5. **Write auth = per-command tenant equality + scope fallback.** `TenantAuthorizationBehavior` gates `ITenantScoped` writes by `currentUser.TenantId == request.TenantId`, with `IsPlatformAdmin`, `IBackgroundCommand`, and a **`BackgroundTenantScope` fallback for tenant-less guests** (L80–94) — the template for guest-driven cross-tenant checkout.
6. **Money refuses cross-currency arithmetic** (`Money.Add` throws, `Money.cs:29`) — so a mixed-currency order must be a *collection* of per-line `Money` + a display projection.
7. **Tax is a fee-kind stub.** `ITaxCalculator` already has the right shape (`Address` → per-line `JurisdictionCode`) but is backed by `StubTaxCalculator` (zero tax).
8. **Reviews already carry `(BookingId, PropertyId)`** (recon's cheap-now decision landed).
9. **`ITenantStripeContextLookup` is pre-committed to become a relationship** — its doc-comment says *"Phase 4's `tenant_connect_accounts` relationship table replaces the implementation without changing the contract."*
10. **Cross-schema `property_id` FK is wide** — pricing/reviews/sync/messaging all reference `catalog.properties`; the `properties → facilities` rename is a coordinated multi-schema wave (~2 days).

---

## 1. Unifying model — one engine, N front-ends

Split today's `Booking` into an **`Order`** (checkout container, guest-scoped) + **`Reservation`** (tenant-scoped line referencing a polymorphic `Reservable`). Today's whole-house booking becomes an `Order` with exactly one `Reservation` — **zero guest-facing change** for single-property bookings.

**Why split, not overload:** checkout concerns (one guest, one PaymentIntent, atomic, one display currency, spans N tenants → cannot be tenant-scoped) vs reservable-line concerns (one supplier tenant, one policy, one settlement currency, own status machine, per-item cancel → must be tenant-scoped). One `TenantId` on `Booking` makes cross-tenant orders impossible under RLS. Splitting is the minimal change that makes every Phase 3/4 requirement fall out.

### 1.2 `Order` (new root, non-tenant-scoped, new `ordering` schema)
`ordering.orders` — guest-scoped (modeled on `loyalty`/`notifications`, deliberately tenant-less). One Order = one checkout = one guest PaymentIntent.
Fields: `Id`, `Reference`, `GuestUserId` (only ownership axis), `GuestDisplayName`, `DisplayCurrency`, `Status` (`Draft→Placed→PartiallyConfirmed→Confirmed→Completed/Cancelled`), `PlacedAt/PlacedTentativeUntil`, `OriginatingTenantId?` (set for OTA agency; null for guest carts), `_reservations` (atomic set). **No Money total on the root** — totals are computed projections (§5). `PartiallyConfirmed` is load-bearing: supplier A can confirm while B is still Tentative (manual capture, per-item).

### 1.3 `Reservation` (evolved from `Booking`, tenant-scoped, `booking` schema)
`booking.reservations` — RLS-scoped by `TenantId` exactly as `booking.bookings` today. Today's `Booking` state machine **minus** checkout fields, **plus** reservable + policy + money:
`Id`, `OrderId` (cross-schema FK up), `TenantId` (supplier — RLS axis), `ReservableKind`, `ReservableId`, `ReservableTitleSnapshot`, `Stay?` (null for point-in-time legs), `GuestCount`, `Status` (**UNCHANGED** machine `Tentative→Confirmed→CheckedIn→CheckedOut→Completed` +Rejected/Cancelled), `SettlementCurrency`, `_lineItems` (`ReservationLineItem` = `BookingLineItem` with `Money` not bare decimal), `Policy` (per-item `CancellationPolicy` VO, §6), `TaxSnapshot` (§3/§5), timestamps. The status-machine methods move **verbatim** from `Booking.cs:141–284`.

### 1.4 `Reservable` polymorphism
`enum ReservableKind { Property=0, Room=1, Flight=2, Car=3, Activity=4 }`. `Reservation` references by `(Kind, Id)` — a loose cross-module reference (like `Booking.PropertyId` today). An `IReservableResolver` per kind returns `(title, settlementCurrency, availabilityProbe, priceQuote)` so checkout is kind-agnostic. **Extension seam:** new leg type = new `ReservableKind` + resolver, no change to Order/payment/RLS.

### 1.5 Front-ends collapse onto one engine
| Front-end | Order | Reservations |
|---|---|---|
| Whole-house (today) | 1 Order, 1 tenant | 1 `Reservation(Property)` |
| Hotel rooms (P3.1) | 1 Order, 1 tenant | N `Reservation(Room)` |
| Multi-unit same-tenant (P3.2) | 1 Order, 1 tenant | N `Reservation(Property\|Room)` |
| Cross-business cart (P3.2) | 1 Order, **M tenants** | N across M tenants |
| OTA itinerary (P4) | 1 Order, M supplier tenants, `OriginatingTenantId`=agency | N legs + `Itinerary` overlay (§9) |

---

## 2. Phase 3

### 2.1 Hotel-style rooms
**Decision: Room Type = child entity of `Facility` (renamed `Property`) with an integer inventory count** — not a separate aggregate, not named units. A room denormalizes `tenant_id` from its facility (like `property_images` today) → inherits tenant scope free, no cross-tenant primitive. Inventory-count availability ("5× Deluxe King") matches how hotels sell; named units are a future refinement in the same table.

- `Facility` gains `ListingMode ∈ {WholeHouse, RoomTypes}` (owner chooses per facility) + `_roomTypes`.
- `RoomType` (`catalog.room_types`): `FacilityId`, `TenantId` (denormalized), `Name`, `InventoryCount`, `Capacity`, room-level `_amenityIds` + `_images`.
- **Pricing:** `pricing.pricing_plans` gains nullable `room_type_id` (plan keyed to facility OR room-type); quote engine resolves by `(kind, reservableId)`.
- **Availability:** count-based. The serializable-txn + `FOR UPDATE` guard (`PlaceBookingHandler.cs:152–209`) generalizes: for `Room`, probe `COUNT(overlapping active reservations) < InventoryCount`. Inventory count mirrored into a `booking.room_inventory` projection so the lock stays inside the `booking` schema (preserves the "no cross-schema FOR UPDATE" rule, `AvailabilityBlock.cs:12`).
- **iCal:** per room-type (`sync.channel_feeds` gains nullable `room_type_id`). **Reviews stay facility-level.** Amenities both levels; capacity/photos per room-type.
- A room booking = `Reservation(Room, roomTypeId, Stay, GuestCount)`; **one Reservation per sellable unit** (per-item cancel + policy + inventory decrement need line granularity).

**Migration wave (`properties → facilities`, ~2 days, one deploy across 4 schemas, forward-only):** catalog rename + add room tables + `ListingMode` (default WholeHouse → identical behavior) + keep a `properties` view one release; pricing add `room_type_id` + re-point FK; reviews rename `property_id → facility_id`; sync add `room_type_id` + rename; messaging rename. **Every existing whole-house facility is WholeHouse and unchanged.**

### 2.2 Multi-unit + cross-business cart
**Decision: one atomic `Order` spanning M tenants, one guest PaymentIntent, N transfers (separate charges-and-transfers), per-item cancel.**
- Drop `Booking.PropertyId`/title/root-money/root-policy → `Order` + `Reservation`. `booking.bookings → booking.reservations`; new `ordering.orders`.
- **Cart:** a `Draft` Order accumulates `Reservation`s (from one or many tenants, at any time). Each add re-probes availability + re-quotes (price + policy + tax per line).
- **Atomic checkout:** `PlaceOrder` extends the serializable-txn pattern across all lines — acquire every availability lock, insert all, commit — or roll back the whole order. Partial availability → 409 identifying the failed line; nothing reserved (the `40001 → ConflictException` map at `PlaceBookingHandler.cs:217–225`, generalized).
- **Per-item cancel:** cancel one `Reservation` → its refund path (§6) + reverse only that supplier's transfer (§3). Siblings untouched.
- **Payment:** see §3 (destination charge for 1 tenant; separate-charges-and-transfers for M). `PaymentIntent` becomes `OrderId`-keyed; add `payment.transfers`.
- **Events:** `OrderDrafted`, `OrderPlaced`, `ReservationPlaced` (replaces `BookingPlaced`; keep `BookingPlaced` one release). **API:** `POST /orders`, `/orders/{id}/items` (add/remove any tenant), `/orders/{id}/place`, `GET /orders/{id}`, `/me/orders`, `/orders/{id}/items/{rid}/cancel`; existing `bookings/*` alias during transition.

---

## 3. Payments

**Selector = `Order.DistinctTenantCount()`.** 1 tenant → **destination charge** (existing, no change). M tenants → **separate charges & transfers** (new): one guest PaymentIntent on the platform account, N `Transfer`s to N Connect accounts, platform keeps Σ per-tenant fee. Both keep `CaptureMethod="manual"`.

**Manual capture across N:** PI authorizes the full order total at Place; on each supplier Confirm, create that line's `Transfer` (`lineNet = lineTotal − perTenantFee`); capture on first confirm; Tentative lines still open at the 24h SLA are auto-expired (existing worker) + refunded (no transfer). New `IStripeGateway.CreateTransferAsync(...)` + `payment.transfers` rows. Idempotency extended with `ForTransfer(reservationId)`.
**Fees:** per-tenant `ApplicationFeeCents(lineTotal, tenant.PlatformFeeBps)`; platform revenue = Σ.
**Refunds per item:** reuse `RefundForBookingHandler` re-keyed to reservation (proportional fee reversal + negative-balance guard already there) + **reverse that line's `Transfer`** (`TransferReversal`); no sibling affected.
**Stripe Tax + marketplace facilitator:** replace `StubTaxCalculator` with a Stripe Tax adapter (existing `ITaxCalculator` shape); tax computed **per reservation line** at its jurisdiction → `TaxSnapshot`; created on the **platform** account (platform collects+remits, all US states); tax lines added to the guest charge, **not** transferred to suppliers; emailed receipts carry the per-line breakdown.

---

## 4. Cross-tenant RLS + authorization (the load-bearing part)

**The key property: the Order is the ONLY cross-tenant object, and it is guest-scoped (not tenant-scoped)** — so no RLS policy ever expresses "tenant A OR tenant B".
- **`Order` → `ordering` schema, guest-scoped.** Ownership axis `guest_user_id`. New RLS policy `guest_user_id = app.user_id OR app.is_platform_admin`. Suppliers never read the order root.
- **`Reservation` → `booking` schema, tenant-scoped, RLS unchanged.** A supplier querying `booking.reservations` sees only their own — sibling reservations in the same order are invisible **for free**. Order-level display fields denormalized onto each reservation so a supplier's queue renders without the order root.
- **Guest reads whole order:** iterate-per-tenant-scope-and-merge (already in `MyBookingsHandler.cs:41–55`).
- **Guest checkout write:** reshaped `PlaceOrder` inserts each reservation inside a per-tenant `BackgroundTenantScope` frame within one serializable txn; the interceptor re-stamps `app.tenant_id` **per statement** so each INSERT passes its own tenant's `WITH CHECK`. **This is exactly why the recon's per-statement binding decision was load-bearing — and it's already implemented.**
- **Guest per-item write (cancel):** the `TenantAuthorizationBehavior` `BackgroundTenantScope` fallback (L80–94) — guest is tenant-less, handler resolves the reservation's tenant from the row. Already ships for single-booking guest cancel.
- **Supplier write:** standard `currentUser.TenantId == reservation.TenantId`. Unchanged.
- **Platform orchestration (transfer fan-out):** `RlsBypassScope` via the audited, allowlisted `IRlsBypassDbContextFactory`.
- **New: `app.user_id` GUC** — one more `set_config` in the interceptor (additive), with fail-safe-deny for anonymous (empty `user_id` denies `ordering.orders`).
- **`TenantAuthorizationBehavior` needs NO change** — each *write* is still single-tenant; only *reads/orchestration* are cross-tenant, served by existing primitives. Confirms the recon's "don't pre-shape M.4" verdict.

---

## 5. Currency / FX
Three roles: **Settlement** (`Reservation.SettlementCurrency` + line `Money`; supplier's payout currency; authoritative for payout/tax/refund), **Charge** (Order PI currency; guest's card), **Display** (`Order.DisplayCurrency`; presentation only). `Money.Add` refusing cross-currency is kept — forces per-currency `Money` collections + display projection.
**FX for display only**, at read time, via a new `IFxRateProvider` (cached, daily refresh). Never mutate stored `Money`. Store: each line `Money` in settlement currency, `TaxSnapshot` in settlement currency, the captured charge amount+currency, and `Order.FxRateSnapshot` (settlement→charge, locked at Place) for reproducible receipts/refunds. Refunds compute in settlement currency (no FX round-trip loss). **Launch:** single-tenant single-currency orders skip FX (charge=settlement=display, rate=1); the FX code activates only for mixed-currency orders.

---

## 6. Cancellation / refund engine
**ONE engine, TWO models chosen per property/room; policy snapshotted per `Reservation` at Place** (immutable, like the price snapshot). Replace `Booking.CancellationPolicy` root enum with a `CancellationPolicy` VO: `Model ∈ {Tiered, RefundableRateUpgrade}`, `TieredSnapshot?` (resolved-from-platform-config list of `(minDaysBeforeCheckin, refundPercent)`), `UpgradePaid?`, `RefundDeadline?`.
1. **Tiered** — full ≥7d / 50% 2–7d / none <48h; **the tier table is global (platform-admin config)**, the **selection** is per-property. Snapshot the resolved tiers at Place so later config changes don't alter in-flight bookings.
2. **Refundable-rate upgrade** — guest pays extra at booking (a `ReservationLineItem Kind="RefundableUpgrade"`); refund must be requested before check-in, else non-refundable; the upgrade fee itself is non-refundable.
**Per-property config (localize values):** `Facility`/`RoomType` gains `CancellationModel` + upgrade price; the **platform owns the global tiered table** — natural home is the currently-stubbed **Admin module** (`CURRENT-GAPS.md` G14).
**Resolution on cancel:** load snapshot → Tiered: `refund = lineNet × matched-tier %`; Upgrade: `now < checkin` → full line refund else 0 → dispatch reservation-keyed `RefundForBookingCommand` (handles explicit amount, fee reversal, transfer reversal). Fills today's `RefundAmount=0` TODO (`Booking.cs:194`) per item.

---

## 7. Sequencing (design-now / implement-later)
1. **Foundation reshape (first):** split `Booking → Order + Reservation`; add `ReservableKind`/`ReservableId`; move state machine; add `ordering` schema + `app.user_id` GUC. Single-property = Order(1 reservation), zero guest-facing change. *Depends on nothing.*
2. **Rooms (P3.1):** `properties → facilities` wave + room-types + count availability. *Depends on (1).* Additive, ships fastest, validates the polymorphism.
3. **Multi-unit + cross-business cart (P3.2):** Order assembly across tenants + atomic Place + separate-charges-and-transfers + per-item cancel + FX + Stripe Tax + cancellation engine. *Depends on (1)+(2)+§3.*
4. **OTA itinerary (P4):** Itinerary overlay + leg polymorphism + supplier-relationship + agency margin. *Depends on (3).*

**Placeholder-correct from the first reshape (cheap now / expensive later):** (a) `ordering` schema + `app.user_id` GUC, (b) `ReservableKind`/`ReservableId` on `Reservation`. Both are additive columns/enums.

---

## 8. Migration & backward-compat
Forward-only, add→backfill→NOT-NULL, cross-schema FKs via raw SQL. Backfill one `Order` per existing booking + rewrite each as `Reservation(Property)`; re-key `PaymentIntent BookingId→OrderId`. Availability probes stay in `booking` schema (mirror room inventory into `booking.room_inventory`). Emit **both** `BookingPlaced` (legacy) + `ReservationPlaced`/`OrderPlaced` one release, then deprecate. `catalog.properties` + `booking.bookings` views one release. New `ordering` policies + `app.user_id` GUC are additive; existing isolation policies untouched.

---

## 9. Phase 4 — OTA package bundling
**`Itinerary` = agency-owned overlay over a Phase-3 `Order`; legs are `Reservation`s with new `ReservableKind`s; the supplier relationship is a new table, not an enum.**
- `Itinerary` (agency-scoped): `AgencyTenantId`, `OrderId`, `Title`, `IsFixed=true`, `_legs` (each maps 1:1 to an Order `Reservation`, with `SequenceOrder` + `SourceKind ∈ {AgencyManual, OnPlatformSupplier}`).
- **Legs reuse the Phase-3 engine** — "buy whole" = placing the parent Order atomically; per-leg cancel = per-item cancel aggregated by the Itinerary. **No new checkout/payment/refund primitive.**
- **Two sources:** agency-manual legs (agency-owned reservable) + on-platform-supplier legs (resolve to another tenant via `IReservableResolver`); external flight/car APIs = a later resolver (seam designed now, adapter deferred).
- **Supplier relationship (not enum):** new `identity.tenant_supplier_relationships (agency_tenant_id, supplier_tenant_id, status, agency_margin_bps, …)` — replaces `ITenantStripeContextLookup` impl without changing the interface (pre-committed in its doc-comment).
- **Revenue split:** transfer fan-out gains an agency-margin layer — `supplierNet = lineTotal − platformFee − agencyMargin`; agencyMargin → agency tenant; supplierNet → supplier; platform keeps its fee. N+1 transfers on one PI (the §3 pipeline + one destination).
- **FX:** reuses §5 (each leg in its supplier's settlement currency; one charge in the charge currency at the locked rate).
- **RLS:** `Itinerary` scoped to `AgencyTenantId`; Order guest-scoped; each leg scoped to its supplier. Same "Order is the only cross-tenant object" property.

---

## 10. Open technical questions (product Qs all answered)
- **Q1 — Manual-capture-across-N timing (§3):** hold auth until all confirm (clean refunds, auth-expiry risk) vs capture-on-first-confirm + refund-unconfirmed. **Rec:** capture-on-first-confirm, bounded by the 24h/48h SLA. Needs a Stripe spike.
- **Q2 — Room inventory lock location (§2.1):** mirror `room_types.inventory_count` into `booking.room_inventory` (keeps lock in one schema) vs lock `catalog.room_types` directly. **Rec:** mirror.
- **Q3 — Transfer + fee-reversal accounting (§3):** `RefundForBookingHandler` doesn't persist prior fee-reversal cents + *approximates* the negative-balance guard; compounds for multi-transfer partial refunds. **Rec:** persist `fee_reversal_cents` before Phase 3 payments.
- **Q4 — Outbox→Service Bus relay (G9)** still unimplemented; cross-tenant orders raise more cross-module events. **Flag:** re-evaluate G9 priority when the cart lands.
- **Q5 — `app.user_id` GUC:** re-verify fail-safe-deny for anonymous callers.
- **Q6 — FX source + staleness:** no `IFxRateProvider` today; needs source + refresh cadence + locked-at-Place snapshot.

### Critical files (implementation)
- `Booking.cs` (split → Order + Reservation), `PlaceBookingHandler.cs` (atomic N-line cross-tenant Place), `StripeGateway.cs` (separate-charges-and-transfers), `TenantGucCommandInterceptor.cs` (`app.user_id` GUC), `TenantAuthorizationBehavior.cs` (verify guest cross-tenant fallback).
