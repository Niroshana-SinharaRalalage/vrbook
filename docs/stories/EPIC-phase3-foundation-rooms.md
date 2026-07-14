# EPIC — Phase 3 (first half): Foundation Reshape & Hotel-Style Rooms (VRB-400–412)

**Status:** Design-complete, implementation-deferred (owner directive 2026-07-13 — Q23: *Phase 3/4 design + document NOW, defer only code*). **Priority:** Could (post-launch, strictly after go-live). **Author:** Platform enterprise architect.
**Companions (read first, in this order):** [`../architecture/PHASE-3-4-DESIGN.md`](../architecture/PHASE-3-4-DESIGN.md) **§0.5 corrections C1–C11 (SUPERSEDE the prose)** + §1, §2, §4, §7, §8 · [`../architecture/PHASE-3-4-DESIGN-REVIEW.md`](../architecture/PHASE-3-4-DESIGN-REVIEW.md) · [`../architecture/CURRENT-STATE.md`](../architecture/CURRENT-STATE.md) · [`../../OPEN-QUESTIONS.md`](../../OPEN-QUESTIONS.md) Q1, Q29–Q31.

This epic implements the **CORRECTED** design (§0.5). Where the prose below §0.5 conflicts with a correction, the correction wins — most load-bearing here: **C10** (48h Tentative SLA everywhere, not 24h), **C11** (anonymous-cart posture + `app.user_id` fail-safe-deny for anonymous), **C7** (`RatePlan` dimension), **C8** (`booking.room_inventory` `FOR UPDATE` counter-row lock).

---

## What this epic is (and is not)

Phase 3 is delivered in two epics. **This one is the first half** — the two dependencies everything else stands on:

1. **Foundation reshape (VRB-400–404)** — split today's `Booking` into an **`Order`** (checkout container, guest-scoped, new `ordering` schema) + **`Reservation`** (tenant-scoped, evolves `booking.bookings`); add `ReservableKind`/`ReservableId` polymorphism; add the `app.user_id` RLS GUC (fail-safe-deny anonymous); forward-only backfill with dual-event emission. **Zero guest-facing change** for existing single-property bookings — a whole-house booking becomes an `Order` with exactly one `Reservation`. *Depends on nothing.*
2. **Hotel-style rooms (VRB-405–412)** — `Facility` (renamed `Property`) + `ListingMode {WholeHouse, RoomTypes}`; `RoomType` child entity; the `RatePlan` dimension (C7); per-room-type pricing/availability/iCal; the `properties → facilities` multi-schema rename wave with view shims; count-availability with the `FOR UPDATE` overbooking guard (C8). *Depends on (1).*

**The second half is [`EPIC-phase3-cart.md`](EPIC-phase3-cart.md) (VRB-42x)** — multi-unit + cross-business cart, atomic multi-tenant Place, separate-charges-and-transfers, FX, Stripe Tax, the cancellation engine. That epic is **blocked-by this one**: the cart needs both the Order/Reservation split (foundation) and room-type reservables (rooms) before it can assemble N legs across M tenants.

**Owner decisions locked (OPEN-QUESTIONS, 2026-07-13):** Q1 = **48h** Tentative hold (make configurable; kill the dead `Booking:TentativeSlaHours` key + hard-coded 24h, G2). Q29 = owner **chooses per facility**: one whole-house listing OR list individual room-type units. Q30 = **Room Type + inventory count** ("5× Deluxe King"), not named units. Q31 = pricing/availability/capacity/photos/iCal per room-type in room mode; **amenities both levels; reviews at facility level**.

---

## Summary table

| ID | Title | Est | Corrections in play | Blocked-by |
|----|-------|-----|---------------------|-----------|
| VRB-400 | `ordering` schema + `Order` aggregate (guest-scoped root) | L | §1.2 | — |
| VRB-401 | `app.user_id` RLS GUC + `ordering.orders` guest policy (fail-safe-deny anonymous) | M | C11, §10-Q5 | VRB-400 |
| VRB-402 | Split `Booking → Reservation`: state machine verbatim + `ReservableKind`/`ReservableId` | L | §1.3, §1.4 | VRB-400 |
| VRB-403 | 48h Tentative SLA — configurable, kill hard-coded 24h | S | C10, G2 | VRB-402 |
| VRB-404 | Forward-only backfill + PaymentIntent re-key + dual-event emission (zero guest-facing change) | L | §8, C10 | VRB-401, VRB-402, VRB-403 |
| VRB-405 | `Facility` (rename `Property`) + `ListingMode` + `RoomType` child entity | L | §2.1 | VRB-404 |
| VRB-406 | `properties → facilities` multi-schema rename wave + view shims | L | §2.1, §8 | VRB-405 |
| VRB-407 | `RatePlan` dimension (`RoomType × RatePlan`) + per-room-type pricing | L | **C7** | VRB-406 |
| VRB-408 | Count-availability + `booking.room_inventory` `FOR UPDATE` counter row | L | **C8** | VRB-406 |
| VRB-409 | Per-room-type iCal feeds; reviews stay facility-level | M | §2.1, Q31 | VRB-406 |
| VRB-410 | Owner room-mode management UI (ListingMode, room types, rate plans, photos) | L | Q29–Q31 | VRB-405, VRB-407, VRB-408 |
| VRB-411 | Guest room-selection + booking UI (`Reservation(Room)`) | M | §2.1 | VRB-408, VRB-410 |
| VRB-412 | Observability — overbooking-attempt counter + lock contention + view-shim usage | M | C8, §8 | VRB-408, VRB-409 |

**Internal dependency order (sequenced spine — NOT fully parallelizable):**
`VRB-400 → 401 → 402 → 403 → 404` (foundation, strictly sequential; the split + backfill are one atomic reshape) **then** `405 → 406` (the rename wave, strictly sequential — one coordinated multi-schema deploy) **then** `407 / 408 / 409` fan out in parallel over the renamed schema **then** `410 → 411` (UI) with `412` (observability) tracking 408/409. **The reshape (400–404) and the rename wave (405–406) are the two non-parallelizable spines of this epic.**

---

### VRB-400 — `ordering` schema + `Order` aggregate (guest-scoped root)
- **Epic:** Phase 3 — Foundation & Rooms · **Priority:** Could (post-launch) · **Estimate:** L
- **Narrative:** As a **guest**, I want my checkout to be represented by a single `Order` that can eventually hold reservations from one or many suppliers, so that the platform has one checkout container per purchase without forcing a single `TenantId` onto my booking (which would make cross-tenant orders impossible under RLS).
- **Acceptance criteria:**
  - **Given** a new `ordering` schema modeled on the deliberately tenant-less `loyalty`/`notifications` contexts (`CURRENT-STATE.md` §8), **when** the migration runs, **then** `ordering.orders` exists with `Id`, `Reference`, `GuestUserId` (the **only** ownership axis — no `TenantId`), `GuestDisplayName`, `DisplayCurrency`, `Status ∈ {Draft, Placed, PartiallyConfirmed, Confirmed, Completed, Cancelled}`, `PlacedAt`, `PlacedTentativeUntil`, `OriginatingTenantId?` (null for guest carts), and an `_reservations` navigation (empty for now — populated in VRB-402/404).
  - **Given** the `Order` aggregate (`AggregateRoot`, `src/VrBook.Domain/Common/AggregateRoot.cs`), **when** created, **then** it carries **no Money total on the root** — totals are computed projections over reservations (§1.2, §5); `Money.Add` refusing cross-currency (`Money.cs:29`) is preserved so mixed-currency totals must be a per-line collection, never a root scalar.
  - **Given** the `Status` machine, **then** `PartiallyConfirmed` is a first-class state (supplier A can confirm while B is still Tentative — load-bearing for the cart epic's per-item manual capture).
  - **Invariant (anonymous-cart posture, C11):** guest-scoped RLS means **no server-side Order exists before sign-in** — an `ordering.orders` row requires a non-empty `GuestUserId`. Pre-auth cart state is client-side; checkout forces sign-in. This story creates the table + aggregate only; the RLS policy that enforces it lands in VRB-401.
- **TDD plan:**
  - *Unit:* `OrderTests.New_order_has_no_root_money_total`; `..._draft_is_initial_status`; `..._partially_confirmed_is_reachable`; `..._requires_guest_user_id`.
  - *Integration (Testcontainers Postgres):* `OrderingSchemaTests.Ordering_orders_table_created_tenantless`; `..._outbox_messages_table_owned_by_ordering_schema` (each schema owns its own outbox, `BaseDbContext.cs`).
  - *E2E:* none (no guest-facing surface yet; verified end-to-end in VRB-404's zero-change walk).
- **Technical notes:** New module boundary `VrBook.Modules.Ordering` (or `ordering`-schema context inside Booking — architect to confirm placement so the `Order ↔ Reservation` cross-schema FK stays clean; recommendation: its own context, mirroring how Loyalty is guest-scoped and tenant-less). New `Order.cs` aggregate + `OrderingDbContext` (schema `ordering`, `BaseDbContext` conventions: soft-delete filter, audit columns, own `outbox_messages`, RowVersion mapped-but-disabled per `BaseDbContext.cs:48-58`). Enum `OrderStatus`. Events `OrderDrafted`, `OrderPlaced` (wired for emission in VRB-404). `DisplayCurrency` is presentation-only (§5). **Reuse:** guest-scoped/tenant-less schema pattern from Loyalty; `AggregateRoot` domain-event buffer.
- **UI/UX spec:** None (backend aggregate story). DTO exposes `Reference` + `Status` for the later `/me/orders` surface.
- **Configuration:** No flags/secrets. The `ordering` schema is additive and inert until VRB-404 wires the reshape. (The epic-wide `Phase3:Rooms` / cart flags default **off** at launch, but this schema carries no runtime behavior on its own.)
- **Rollout:** Additive schema only; zero impact on existing bookings. Migration runs after Identity (canonical tenant registry) but has no cross-schema FK yet (the `Reservation.OrderId` FK is created bottom-up in VRB-402). Backward-compat: nothing reads `ordering.orders` until VRB-404. Rollback: drop the `ordering` schema (no consumers).
- **Observability:** Log `order_id`, `guest_user_id`, `status` on create. Metric `ordering.order.created` (0 until VRB-404 flips the write path).
- **Definition of Done:** unit + integration green → architect review of module placement + tenant-less scoping → staging: migration applies, `ordering.orders` present + empty → prod (schema only, inert) → monitored.
- **Dependencies:** **blocked-by** nothing. **blocks** VRB-401, VRB-402 (Reservation FKs up to Order), and the entire rooms + cart stack.
- **Parallelisation:** Lane **F (foundation spine)** — sequenced, single-owner. Owns `Modules.Ordering/**`, `ordering.orders` migration. **NOT parallelizable** with 401/402/404 (they share the reshape).

---

### VRB-401 — `app.user_id` RLS GUC + `ordering.orders` guest-scoped policy (fail-safe-deny anonymous)
- **Epic:** Phase 3 — Foundation & Rooms · **Priority:** Could (post-launch) · **Estimate:** M
- **Narrative:** As the **platform**, I want a guest-scoped RLS axis (`app.user_id`) added to the tenant GUC interceptor so that `ordering.orders` is isolated per guest and **anonymous callers are denied by default**, so that a guest can only ever read/write their own order and no order exists server-side before authentication (C11 anonymous-cart posture).
- **Acceptance criteria:**
  - **Given** `TenantGucCommandInterceptor` (`src/VrBook.Infrastructure/Persistence/TenantGucCommandInterceptor.cs:70-101`) already runs `set_config('app.tenant_id',…,true)` + `app.is_platform_admin` per DbCommand, **when** the interceptor is extended, **then** it **additively** runs `set_config('app.user_id', <ICurrentUser.UserId>, true)` before every command; existing `app.tenant_id`/`app.is_platform_admin` behavior is **unchanged** (regression-tested).
  - **Given** an **anonymous** caller (no `ICurrentUser.UserId`), **when** any command executes, **then** `app.user_id` resolves to **empty** — the exact fail-safe-deny idiom used for `app.tenant_id` today (`ICurrentUser.TenantId → BackgroundTenantScope → empty`, `CURRENT-STATE.md` §8) — and the `ordering.orders` policy denies the row (§10-Q5: **re-verify fail-safe-deny for anonymous** is satisfied).
  - **Given** the new RLS policy on `ordering.orders`, **then** it is `guest_user_id = app.user_id OR app.is_platform_admin` — a guest sees only their own orders; suppliers never read the order root (§4); an empty `app.user_id` matches nothing.
  - **Given** the platform-admin/worker bypass, **then** `IRlsBypassDbContextFactory` + `RlsBypassScope` still work for `ordering.orders` (allowlisted call-sites, arch-test-pinned).
  - **Invariant:** `TenantAuthorizationBehavior` needs **NO change** (§4) — the GUC is a read/isolation primitive, not a write-auth change.
- **TDD plan:**
  - *Unit:* `TenantGucCommandInterceptorTests.Sets_app_user_id_when_authenticated`; `..._empty_user_id_when_anonymous`; `..._does_not_regress_tenant_id_or_is_platform_admin`.
  - *Integration (RLS on, Testcontainers):* `OrderingRlsPolicyTests.Guest_sees_only_own_orders`; `..._anonymous_denied_all_orders` (fail-safe-deny proof, Q5); `..._platform_admin_sees_all`; `..._rls_bypass_scope_reads_ordering`.
  - *E2E:* none (RLS is below the API surface; exercised transitively in VRB-411).
- **Technical notes:** One additional `set_config` line in `TenantGucCommandInterceptor` (additive, per-statement — the same load-bearing per-DbCommand binding the recon flagged). `app.user_id` sourced from `ICurrentUser.UserId`. New `OpsPhase3_Ordering_RlsPolicies` migration creating the guest policy (mirrors the per-module `OpsM9_*_RlsPolicies` shape). Existing isolation policies **untouched** (§8). **Reuse:** the entire fail-safe-deny + bypass machinery already exists for `app.tenant_id`; this story adds a parallel axis. **Note (C3 forward-hook):** the interceptor firing per DbCommand while EF batches INSERTs is the reason the cart epic must flush per tenant — documented here so the GUC's per-statement nature is understood, but the multi-tenant flush itself is a cart-epic (VRB-42x) concern.
- **UI/UX spec:** None (infrastructure).
- **Configuration:** No flags. RLS policies are schema, not runtime-toggled.
- **Rollout:** Additive GUC + additive policy; existing policies unchanged. Migration after VRB-400. Backward-compat: setting an unused GUC on existing schemas is inert (no policy references `app.user_id` outside `ordering`). Rollback: drop the `ordering` policy + remove the `set_config` line (existing tenant isolation unaffected).
- **Observability:** Metric `ordering.rls.anonymous_denied` (should spike only on probing). Audit-log every `RlsBypassScope` open on `ordering` (existing audited bypass). Alert on any `ordering.orders` read with empty `app.user_id` returning rows (would mean the policy regressed).
- **Definition of Done:** unit + integration green (**including the anonymous-denied fail-safe proof + the tenant-id non-regression suite**) → security review of the new axis → staging RLS isolation walk → prod → monitored.
- **Dependencies:** **blocked-by** VRB-400. **blocks** VRB-404 (guest writes to `ordering.orders`), and the cart epic's cross-tenant reads.
- **Parallelisation:** Lane **F (foundation spine)** — sequenced. Owns the `TenantGucCommandInterceptor` edit + `ordering` RLS migration. **NOT parallelizable** (shared reshape + interceptor is load-bearing for all tenants).

---

### VRB-402 — Split `Booking → Reservation`: state machine verbatim + `ReservableKind`/`ReservableId`
- **Epic:** Phase 3 — Foundation & Rooms · **Priority:** Could (post-launch) · **Estimate:** L
- **Narrative:** As the **platform**, I want today's `Booking` evolved into a tenant-scoped `Reservation` that references a polymorphic reservable and links up to an `Order`, so that a single supplier line carries its own policy/currency/status machine while the guest-facing checkout concerns move to the `Order` — **without changing the booking status machine's behavior**.
- **Acceptance criteria:**
  - **Given** today's `Booking` (`src/Modules/VrBook.Modules.Booking/Domain/Booking.cs`), **when** reshaped into `Reservation` (`booking.reservations`, RLS-scoped by `TenantId` exactly as `booking.bookings` today), **then** it gains `OrderId` (cross-schema FK up to `ordering.orders`), `ReservableKind`, `ReservableId`, `ReservableTitleSnapshot`, `Stay?` (nullable for future point-in-time legs), `SettlementCurrency`, per-line `Money` on `ReservationLineItem` (was bare decimal on `BookingLineItem`), and drops checkout-only concerns (single root `PropertyId`/`PropertyTitle`, root-money, root-`CancellationPolicy`) which migrate to the Order / per-line policy.
  - **Given** the status machine, **then** `Tentative → Confirmed → CheckedIn → CheckedOut → Completed` (+ `Rejected`/`Cancelled`) moves **verbatim** from `Booking.cs:141–284` — same transitions, same guards, same `CompletionDueAt` turnover snapshot on `CheckOut` (OPS.M.16), same `BookingHold`/`AvailabilityBlock` integration. **Behavior is byte-identical**; only the shape around it changes.
  - **Given** `enum ReservableKind { Property=0, Room=1, Flight=2, Car=3, Activity=4 }`, **then** a reshaped whole-house booking is `Reservation(Property, propertyId, Stay, GuestCount)` — a **loose cross-module `(Kind, Id)` reference**, exactly like `Booking.PropertyId` is today (no hard FK across module boundaries).
  - **Given** the `IReservableResolver` seam (per-kind → `(title, settlementCurrency, availabilityProbe, priceQuote)`), **then** a `Property` resolver is registered so checkout is kind-agnostic; `Room` lands in VRB-408, later kinds in the Phase-4 OTA epic.
  - **Invariant:** enum values are **append-only** — `Property=0`/`Room=1` are immutable; never renumber.
- **TDD plan:**
  - *Unit:* `ReservationTests.State_machine_transitions_match_legacy_Booking` (port every `BookingTests` case); `..._checkout_snapshots_completion_due_at`; `..._line_item_carries_money_not_decimal`; `PropertyReservableResolverTests.Resolves_property_descriptor`.
  - *Integration (Testcontainers, RLS on):* `ReservationPersistenceTests.Reservation_scoped_by_tenant_id`; `..._order_id_cross_schema_fk_enforced`; `CrossTenantReservationTests.Supplier_sees_only_own_reservations` (§4 — sibling reservations invisible for free).
  - *E2E:* deferred to VRB-404 (the zero-guest-facing-change walk proves the reshape end-to-end).
- **Technical notes:** Rename/evolve `Booking.cs → Reservation.cs`; `booking.bookings → booking.reservations` (forward-only migration; **keep a `booking.bookings` view one release**, §8, for the dual-event window). `BookingLineItem → ReservationLineItem` with `Money` (`Money.cs`) not bare decimal. New `IReservableResolver` + `PropertyReservableResolver`. State-machine methods copied verbatim from `Booking.cs:141–284`. `PlaceBookingHandler` (`PlaceBookingHandler.cs`) stays single-line for now (the atomic N-line cross-tenant Place is the cart epic); it writes one `Reservation` under one `Order`. **Do NOT** hand-edit the EF snapshot — regenerate via `dotnet ef migrations add`. **Reuse:** `MyBookingsHandler.cs:41–55` per-tenant-scope read idiom is untouched and becomes the guest whole-order read; `TenantAuthorizationBehavior` fallback (lines 80–94) for guest-driven writes is unchanged.
- **UI/UX spec:** None directly — but the reshape MUST preserve every guest/owner booking screen's behavior (proven in VRB-404). Snapshot fields (`ReservableTitleSnapshot`) are denormalized so a supplier queue renders without the Order root (§4).
- **Configuration:** No flags (the reshape is not toggle-able — it's a structural migration under the dual-event window). SLA value comes from VRB-403.
- **Rollout:** Forward-only. Order of operations: create `ordering` (VRB-400) → GUC/policy (VRB-401) → rename `bookings → reservations` + view shim (this story) → backfill + PaymentIntent re-key + dual events (VRB-404). Backward-compat: the `booking.bookings` view keeps legacy readers working one release; `BookingPlaced` still emitted (VRB-404). Rollback: forward-fix only (the view shim + dual events are the safety net; there is no down-migration per repo convention).
- **Observability:** Log `reservation_id`, `order_id`, `tenant_id`, `reservable_kind`, `status` on every transition (parity with today's booking logs). Metric `booking.reservation.status_changed{status}`.
- **Definition of Done:** unit + integration + cross-tenant green (**every legacy `BookingTests` case ported and passing against `Reservation`**) → architect review that the state machine moved verbatim → staging (behind the reshape, guest flows identical) → prod → monitored via VRB-404's walk.
- **Dependencies:** **blocked-by** VRB-400. **blocks** VRB-403, VRB-404, and every rooms story (rooms are `Reservation(Room)`).
- **Parallelisation:** Lane **F (foundation spine)** — sequenced. Owns `Modules.Booking/Domain/Reservation.cs`, `ReservationLineItem`, `IReservableResolver`, the `bookings → reservations` migration. **NOT parallelizable** — this is the core reshape.

---

### VRB-403 — 48h Tentative SLA: configurable, kill the hard-coded 24h
- **Epic:** Phase 3 — Foundation & Rooms · **Priority:** Could (post-launch) · **Estimate:** S
- **Narrative:** As a **platform operator**, I want the Tentative hold window to be the locked **48h** value and driven by config, so that the expiry sweep and guest UX use one correct, changeable number instead of a hard-coded 24h that ignores its own config key (C10 / gap G2).
- **Acceptance criteria:**
  - **Given** the locked answer is **48h** (OPEN-QUESTIONS Q1), **when** a `Reservation` is placed, **then** `PlacedTentativeUntil = PlacedAt + 48h` — resolved from the **live** `Booking:TentativeSlaHours` config key (default 48), **not** the hard-coded 24h that today sits at `Booking.cs:119` (`CURRENT-STATE.md` §6/§14: "hard-coded 24h while the config key is dead").
  - **Given** the config key was previously ignored (G2), **when** an operator sets `Booking:TentativeSlaHours`, **then** both the domain (`Reservation.Place`) and the expiry worker (`src/Workers/VrBook.Workers.Booking --mode=expiry`) honor the same value — no divergence between what's set at Place and what the sweep enforces.
  - **Given** the `Order`, **then** `Order.PlacedTentativeUntil` is derived consistently from the same SLA (the order-level Tentative deadline used by the cart epic's capture timing).
  - **Invariant (C10):** **48h is used everywhere** — no remaining 24h literal in domain, worker, Bicep comment, or docs. A grep guard test asserts no `24` SLA literal survives.
- **TDD plan:**
  - *Unit:* `ReservationSlaTests.Tentative_until_is_placed_at_plus_config_hours`; `..._defaults_to_48_hours`; `..._no_hardcoded_24h_literal` (grep-style assertion over the domain).
  - *Integration:* `ExpirySweepTests.Sweep_expires_reservations_past_configured_sla` (Testcontainers; set SLA=48, advance clock, assert Tentative → expired).
  - *E2E:* covered by VRB-404's walk (place → still Tentative before SLA).
- **Technical notes:** Replace the `Booking.cs:119` hard-coded `TimeSpan.FromHours(24)` with an injected `Booking:TentativeSlaHours` (default 48) threaded into `Reservation.Place` (via an options/clock port, not a domain-reads-config anti-pattern — pass the resolved hours in from the handler). Update the Booking worker's expiry predicate to read the same key. Fix the Bicep comment / any older doc that says 6h/24h. **Reuse:** the existing expiry sweep worker + `TentativeUntil` sweep predicate (OPS.M.16-era) — only the source of the hours changes. This is the G2 dependency C10 names.
- **UI/UX spec:** Guest booking-confirmation + owner queue copy that references the hold window must read "48 hours" (or render the configured value) — no "24h" strings in `web/`. A11y: the countdown/deadline is text, not color-only.
- **Configuration:** `Booking:TentativeSlaHours` — **default 48 all envs** (dev/staging/prod). Previously-dead key becomes live. No secret.
- **Rollout:** Config-only behavior change (plus a small domain edit). Deploy with the reshape. Backward-compat: existing in-flight Tentative bookings keep their already-stamped `TentativeUntil` (snapshot, not recomputed). Rollback: revert the default to prior behavior via config (no schema change).
- **Observability:** Log the resolved SLA hours at Place. Metric `booking.tentative.expired{sla_hours}`. Alert if the domain-resolved SLA ≠ the worker-resolved SLA (drift guard).
- **Definition of Done:** unit + integration green → review (confirm no 24h literal remains) → staging: place a reservation, verify 48h deadline in DB + UI → prod → monitored.
- **Dependencies:** **blocked-by** VRB-402 (the SLA now lives on `Reservation.Place`). **blocks** VRB-404 (the backfill/walk asserts 48h).
- **Parallelisation:** Lane **F (foundation spine)** — small, sequenced with the reshape. Owns the `Booking.cs`/`Reservation.cs` SLA edit + worker predicate + the config key.

---

### VRB-404 — Forward-only backfill + PaymentIntent re-key + dual-event emission (zero guest-facing change)
- **Epic:** Phase 3 — Foundation & Rooms · **Priority:** Could (post-launch) · **Estimate:** L
- **Narrative:** As the **platform**, I want every existing booking migrated into an `Order(1 Reservation)` with payments re-keyed and both old + new events emitted for one release, so that the reshape ships with **zero guest-facing change** and no downstream consumer breaks during the transition.
- **Acceptance criteria:**
  - **Given** existing `booking.bookings` rows, **when** the forward-only backfill runs (add → backfill → NOT-NULL, `Migrator` `Backfill` service pattern, `CURRENT-STATE.md` §8), **then** each becomes exactly **one `Order`** (guest-scoped, `GuestUserId` from the booking's guest) wrapping **one `Reservation(Property)`** — preserving status, dates, guests, line items, and the 48h/snapshotted `TentativeUntil`.
  - **Given** `PaymentIntent` is `BookingId`-keyed today (one row per booking, `CURRENT-STATE.md` §6), **when** re-keyed, **then** each PI maps `BookingId → OrderId` **without** changing its tenant scope or Stripe object (the multi-supplier PI re-scoping to platform is **C2 / a cart-epic concern** — this story only re-keys the identifier so single-tenant payments keep working byte-identically).
  - **Given** the dual-event window (§8), **when** a booking is placed, **then** **both** `BookingPlaced` (legacy, `Contracts/Events/BookingEvents.cs:20`) **and** `ReservationPlaced` + `OrderPlaced` are emitted for **one release**; existing in-process `INotificationHandler`s keep firing on `BookingPlaced`; new handlers subscribe to the new events; the legacy event is deprecated the following release.
  - **Given** a guest or owner exercising every existing booking flow (the 6-flow walk: browse+quote → book → cancel → admin confirm → admin reject → iCal sync), **then** **every screen and outcome is identical** to pre-reshape — this is the epic's headline **zero guest-facing change** acceptance criterion; a single behavioral difference fails the story.
  - **Invariant:** `catalog.properties` + `booking.bookings` views survive one release (§8) so any straggler reader keeps working; the `ordering`/`reservations` write path is authoritative.
- **TDD plan:**
  - *Unit:* `BackfillMappingTests.One_booking_maps_to_one_order_one_reservation`; `..._preserves_status_dates_guests_lineitems`; `DualEventTests.Place_emits_BookingPlaced_and_ReservationPlaced_and_OrderPlaced`.
  - *Integration (Testcontainers):* `ReshapeBackfillTests.Existing_bookings_backfilled_idempotently` (re-run = no dupes); `PaymentIntentRekeyTests.Pi_rekeyed_booking_to_order_single_tenant_unchanged`; `LegacyReaderTests.booking_bookings_view_still_serves_legacy_readers`.
  - *E2E (Playwright):* `reshape-zero-change.e2e` — run the full 6-flow guest+owner walk pre/post reshape and assert identical outcomes (the zero-guest-facing-change gate).
- **Technical notes:** New idempotent `Migrator` backfill service (alongside `SeedPlatformAdminsBackfill`/`SeedE2EBackfill`, `CURRENT-STATE.md` §8) that creates `Order` + rewrites each booking as `Reservation(Property)` and re-keys `payment.payment_intents.BookingId → OrderId`. Forward-only migrations, cross-schema FKs via raw SQL (EF can't model them). Dual emission: keep `BookingPlaced` (`BookingEvents.cs`) + add `OrderPlaced`/`ReservationPlaced`. Availability probes stay in the `booking` schema (mirror room inventory arrives in VRB-408). **Reuse:** the migrator backfill pattern, the outbox → in-process MediatR publish (`CURRENT-STATE.md` §9), the 6-flow staging walk (`reference_staging_walk_6_flows`). **Note:** the outbox → Service Bus relay is still uncoded (G9) — cross-module events remain in-process; this story does not change that.
- **UI/UX spec:** **No UI change by design.** The DoD is that the UI is provably identical. New `/me/orders` + `GET /orders/{id}` API endpoints may be added (aliased behind existing `bookings/*` during transition) but no guest-visible route changes in this story.
- **Configuration:** `Phase3:DualEventEmission` (default **on** during the transition release, then removed). No new secret. The reshape itself is not flag-gated (it's a data migration), but new order endpoints sit behind the existing auth.
- **Rollout:** **The migration order is load-bearing:** Identity first → `ordering` schema (400) → GUC/policy (401) → `bookings → reservations` rename + view (402) → **this backfill + PI re-key + dual events**. View shims (`catalog.properties`, `booking.bookings`) live one release; dual events live one release. Backward-compat is the whole point. Rollback: forward-fix — the view shims + dual events keep the previous release's code working, so a bad deploy rolls back to the prior image without a down-migration; the backfill is idempotent and re-runnable.
- **Observability:** Backfill emits `reshape.backfill.orders_created`, `..._reservations_created`, `..._payment_intents_rekeyed` counts + a reconciliation assertion (`#orders == #bookings`, delta 0). Metric `booking.dual_event.emitted{event}`. Alert on any `BookingPlaced`-only handler that stops firing (regression) or any backfill reconciliation delta ≠ 0.
- **Definition of Done:** unit + integration green → **the `reshape-zero-change.e2e` 6-flow walk passes identically pre/post** → architect review of the backfill idempotency + PI re-key → staging: backfill applied, full 6-flow walk verified by owner in-browser → prod: backfill run + reconciliation delta 0 → monitored one release, then legacy events + view shims removed.
- **Dependencies:** **blocked-by** VRB-401, VRB-402, VRB-403. **blocks** VRB-405 (rooms build on the reshaped `Reservation`) and the entire cart epic.
- **Parallelisation:** Lane **F (foundation spine)** — the terminal foundation story, sequenced. Owns the migrator backfill + PI re-key migration + dual-event wiring. **NOT parallelizable** — closes the reshape.

---

### VRB-405 — `Facility` (rename `Property`) + `ListingMode` + `RoomType` child entity
- **Epic:** Phase 3 — Foundation & Rooms · **Priority:** Could (post-launch) · **Estimate:** L
- **Narrative:** As a **property owner**, I want to choose per facility whether it is advertised as one whole-house listing or as bookable individual room-types, so that a hotel-style operator can sell "5× Deluxe King" while a whole-house owner's listing is completely unchanged (Q29/Q30).
- **Acceptance criteria:**
  - **Given** today's `Property` aggregate (`src/Modules/VrBook.Modules.Catalog/…/Property.cs`), **when** renamed to `Facility`, **then** it gains `ListingMode ∈ {WholeHouse, RoomTypes}` (**default WholeHouse** → identical behavior) and a `_roomTypes` collection; its gated `Activate(tenantStatus, chargesEnabled, payoutsEnabled)` (Stripe-readiness) and images/house-rules/amenities are preserved.
  - **Given** `ListingMode=RoomTypes`, **when** the owner adds a `RoomType` (`catalog.room_types`), **then** it carries `FacilityId`, `TenantId` (**denormalized** from the facility, like `property_images` today → inherits tenant scope free, no cross-tenant primitive), `Name`, `InventoryCount` (int ≥ 1, "5× Deluxe King"), `Capacity`, room-level `_amenityIds`, and room-level `_images`.
  - **Given** Q31, **then** **amenities exist at both levels** (facility + room-type); **capacity + photos are per room-type** in room mode; **reviews stay facility-level** (VRB-409 confirms).
  - **Given** a `WholeHouse` facility, **then** it has **zero** room-types and behaves exactly as today — no guest-facing change for existing listings.
  - **Invariant:** a `RoomType` is a **child entity of `Facility`, not a separate aggregate** and **not** a named unit — inventory-count model only (Q30); named units are a future refinement in the same table.
- **TDD plan:**
  - *Unit:* `FacilityTests.Default_listing_mode_is_whole_house`; `..._room_types_require_room_mode`; `RoomTypeTests.Inventory_count_at_least_one`; `..._denormalizes_tenant_id_from_facility`; `..._amenities_and_photos_at_room_level`.
  - *Integration (Testcontainers, RLS on):* `RoomTypePersistenceTests.Room_type_scoped_by_denormalized_tenant_id`; `..._whole_house_facility_has_no_room_types`.
  - *E2E:* owner room-management covered in VRB-410.
- **Technical notes:** Rename `Property.cs → Facility.cs`; add `enum ListingMode { WholeHouse=0, RoomTypes=1 }` + `_roomTypes`. New `RoomType` entity + `catalog.room_types` table (denormalized `tenant_id`, cross-schema raw-SQL FK to `identity.tenants` like every tenant-owned table). This story adds the **catalog-side** aggregate + table only; the cross-schema rename propagation (pricing/reviews/sync/messaging) is VRB-406. **Do NOT** hand-edit the EF snapshot — regenerate. **Reuse:** `property_images` denormalized-tenant pattern; the gated `Activate` Stripe-readiness guard stays on `Facility`.
- **UI/UX spec:** Backend/aggregate story; owner UI in VRB-410. DTO exposes `listingMode` + `roomTypes[]` (with `inventoryCount`, `capacity`, `amenityIds`, `images`) for the management + guest surfaces.
- **Configuration:** `Phase3:Rooms` feature flag — **default off all envs** at launch. `RoomType` writes are gated by the flag; reads of `ListingMode` default to WholeHouse when the flag is off. `NEXT_PUBLIC_PHASE3_ROOMS` client mirror.
- **Rollout:** Additive `ListingMode` column (default WholeHouse) + additive `catalog.room_types` table. **This is the first step of the rename wave (VRB-406) but scoped to catalog domain.** Backward-compat: every existing facility is WholeHouse and unchanged. Rollback: flag off hides room mode; the `ListingMode` column + empty `room_types` table are inert.
- **Observability:** Metric `catalog.facility.listing_mode{mode}`; `catalog.room_type.created`. Log `facility_id`, `tenant_id`, `listing_mode` on change.
- **Definition of Done:** unit + integration green → architect review of the child-entity vs aggregate decision → staging: create a RoomTypes facility with 2 room-types (flag on) while an existing WholeHouse facility is provably unchanged → prod (flag off) → monitored.
- **Dependencies:** **blocked-by** VRB-404 (rooms build on the reshaped `Reservation`/`Order`). **blocks** VRB-406, VRB-407, VRB-408, VRB-410.
- **Parallelisation:** Lane **R-catalog** — first rooms lane. Owns `Modules.Catalog/Domain/Facility.cs`, `RoomType.cs`, `catalog.room_types` migration. **Sequenced before VRB-406** (the rename wave depends on the catalog rename landing).

---

### VRB-406 — `properties → facilities` multi-schema rename wave + view shims
- **Epic:** Phase 3 — Foundation & Rooms · **Priority:** Could (post-launch) · **Estimate:** L
- **Narrative:** As the **platform**, I want the `properties → facilities` rename propagated across every schema that references it — in one coordinated, forward-only deploy with view shims — so that pricing, reviews, sync, and messaging all speak "facility" without a big-bang break (the ~2-day cross-schema wave, §2.1/§8).
- **Acceptance criteria:**
  - **Given** the wide cross-schema `property_id` FK (pricing/reviews/sync/messaging all reference `catalog.properties`, §0-item-10), **when** the rename wave deploys, **then** in **one coordinated deploy**: catalog is renamed (VRB-405 landed the table) + a **`catalog.properties` view** kept one release; pricing re-points its FK to `catalog.facilities`; reviews renames `property_id → facility_id`; sync renames + re-points; messaging renames.
  - **Given** the forward-only convention, **then** there are **no down-migrations**; the view shims (`catalog.properties`, and any per-schema alias) are the rollback safety net for one release.
  - **Given** the cross-schema migration trap (`reference_cross_schema_migration_trap`), **then** every cross-schema reference is guarded (`IF EXISTS (SELECT 1 FROM information_schema.tables …)`) and Identity migrations still run first.
  - **Given** any existing reader still issuing `catalog.properties` queries during the transition, **then** the view serves them identically until the next release drops it.
  - **Invariant:** **every existing whole-house facility is `WholeHouse` and functionally unchanged** through the entire wave — the rename is mechanical, not behavioral.
- **TDD plan:**
  - *Unit:* n/a (schema migration; logic-free).
  - *Integration (Testcontainers, all 5 schemas):* `RenameWaveTests.Pricing_fk_points_to_facilities`; `..._reviews_uses_facility_id`; `..._sync_and_messaging_renamed`; `LegacyViewShimTests.catalog_properties_view_serves_reads_one_release`; `CrossSchemaGuardTests.Rename_guarded_by_information_schema_exists`.
  - *E2E:* the VRB-404 6-flow walk re-run post-wave asserts pricing/reviews/sync/messaging still work end-to-end.
- **Technical notes:** Coordinated forward-only migrations across `catalog`, `pricing` (`pricing_plans` FK re-point), `reviews` (`property_id → facility_id` — note reviews already carry `(BookingId, PropertyId)`), `sync` (`channel_feeds` rename + re-point), `messaging` (thread-per-booking property ref rename). **The `room_type_id` nullable columns on `pricing.pricing_plans` and `sync.channel_feeds` are deliberately deferred to their feature stories (VRB-407, VRB-409)** as additive-after-wave — this story is the **pure rename + FK re-point + view shims** so it stays mechanical and atomic. Regenerate EF snapshots per context; never hand-edit. **Reuse:** the OPS.M.3a raw-SQL cross-schema FK pattern; the cross-schema `IF EXISTS` guard burned into two prior migrations.
- **UI/UX spec:** None (schema). The rename must be invisible to every UI (guest + owner).
- **Configuration:** No flag — a rename is not toggle-able. (Room *features* are behind `Phase3:Rooms`; the rename itself is unconditional structural work.)
- **Rollout:** **The single non-parallelizable multi-schema wave.** One coordinated deploy touching 5 schemas; migration order Identity → catalog → pricing → reviews → sync → messaging. View shims one release; drop them the next release once all readers are cut over. Backward-compat: the whole design of the wave. Rollback: forward-fix — the view shims keep the prior image working; there is no down-migration.
- **Observability:** Metric `catalog.legacy_properties_view.hits` (must trend to 0 before the shim is dropped — the signal that cutover is complete). Log each migration step's row counts. Alert if the view still has hits when the drop is scheduled.
- **Definition of Done:** integration green across all 5 schemas → architect review of the wave ordering + guards → staging: apply the wave, run the full 6-flow walk, confirm `legacy_properties_view.hits → 0` → prod (one coordinated deploy) → monitored one release → shim drop.
- **Dependencies:** **blocked-by** VRB-405. **blocks** VRB-407, VRB-408, VRB-409 (they consume the renamed schema).
- **Parallelisation:** Lane **R-catalog** — **the second non-parallelizable spine of this epic.** Owns the coordinated migrations in all 5 schemas + the view shims. **Must land as one deploy before 407/408/409 fan out.**

---

### VRB-407 — `RatePlan` dimension (`RoomType × RatePlan`) + per-room-type pricing
- **Epic:** Phase 3 — Foundation & Rooms · **Priority:** Could (post-launch) · **Estimate:** L
- **Narrative:** As a **hotel-style owner**, I want to sell each room-type under multiple rate plans (price × policy × prepayment), so that "flexible" and "non-refundable" are two rate plans on the same room-type — the incumbent-standard selling unit (C7, research §1).
- **Acceptance criteria:**
  - **Given** correction **C7**, **when** the pricing model is extended, **then** a `RatePlan` dimension exists such that a bookable option is `(RoomType × RatePlan)`; a `RatePlan` = **price × cancellation policy × prepayment terms**. `pricing.pricing_plans` gains a **nullable `room_type_id`** (a plan keyed to a facility OR a room-type) and the quote engine resolves by `(kind, reservableId)` (§2.1).
  - **Given** the two cancellation models (Q24: Tiered vs Refundable-rate-upgrade), **then** they are represented as **two rate plans** on the same room-type — **not** a separate "RefundableUpgrade line-item" (C7 folds that into the rate-plan model).
  - **Given** a `Reservation(Room)` is placed under a chosen rate plan, **then** the **resolved rate plan is snapshotted onto the Reservation** (immutable, like the price snapshot) so later plan/config changes don't alter in-flight bookings.
  - **Given** a `WholeHouse` facility, **then** its existing single facility-level pricing plan is unchanged (`room_type_id` null) — zero pricing change for whole-house listings.
  - **Given** the quote engine, **then** a room-type quote returns per-plan `Money` in the room-type's settlement currency; `Money.Add` cross-currency refusal (`Money.cs:29`) is preserved.
- **TDD plan:**
  - *Unit:* `RatePlanTests.Two_cancellation_models_are_two_rate_plans`; `..._plan_is_price_policy_prepayment`; `PricingPlanTests.room_type_id_nullable_facility_or_room`; `QuoteEngineTests.Resolves_quote_by_kind_and_reservable_id`; `ReservationTests.Resolved_rate_plan_snapshotted_at_place`.
  - *Integration (Testcontainers):* `RoomTypePricingTests.Room_type_quote_returns_per_plan_money`; `..._whole_house_plan_unchanged_null_room_type`.
  - *E2E:* owner sets two rate plans on a room-type (VRB-410); guest picks one at booking (VRB-411).
- **Technical notes:** Extend `pricing.pricing_plans` with nullable `room_type_id` (additive-after-wave, per VRB-406's deferral note) + a `RatePlan` concept (new `pricing.rate_plans` keyed to `(pricing_plan / room_type, policy, prepayment)` — architect to confirm whether a rate plan is a new table or a facet of `pricing_plans`; recommendation: a `rate_plans` child so a room-type can carry N plans). The resolved plan is snapshotted onto `Reservation.Policy` + price line at Place (VRB-402's snapshot mechanism). **Reuse:** the existing quote engine + `PricingPlan` root (`CURRENT-STATE.md` §5/§6); the price-snapshot idiom that already exists on booking. **Cross-ref:** the full two-model cancellation *resolution engine* (Tiered %tier / upgrade full-if-before-checkin) is a **cart-epic (VRB-42x)** deliverable per §6 — this story lands the rate-plan *shape* + snapshot; the cart epic lands the refund resolution.
- **UI/UX spec:** Owner rate-plan editor in VRB-410 (add/name plans, set price, pick policy model, set prepayment). Guest sees per-plan price + policy in VRB-411. This story is backend; DTO exposes `roomType.ratePlans[]` with `{name, price, currency, policyModel, prepayment}`.
- **Configuration:** Under `Phase3:Rooms` (default off). No new secret. Optional `Pricing:MaxRatePlansPerRoomType` (default 8).
- **Rollout:** Additive `pricing.pricing_plans.room_type_id` (nullable) + `pricing.rate_plans` table, after VRB-406's rename wave. Backward-compat: whole-house plans have null `room_type_id` and no rate plans → identical behavior. Rollback: flag off; nullable column + empty table inert.
- **Observability:** Metric `pricing.rate_plan.created`, `pricing.quote.resolved{kind}`. Log the snapshotted plan id on each `Reservation(Room)` place.
- **Definition of Done:** unit + integration green → architect review of the rate-plan table shape + snapshot → staging: two rate plans on a room-type, guest quotes each → prod (flag off) → monitored.
- **Dependencies:** **blocked-by** VRB-406. **blocks** VRB-410 (owner editor), VRB-411 (guest plan pick). **blocks** the cart epic's cancellation-engine resolution (which consumes the snapshotted policy).
- **Parallelisation:** Lane **R-pricing** — parallel with R-booking (408) + R-sync (409) after the rename wave. Owns `Modules.Pricing/**` rate-plan additions + `pricing.rate_plans`/`room_type_id` migration.

---

### VRB-408 — Count-availability + `booking.room_inventory` `FOR UPDATE` counter row
- **Epic:** Phase 3 — Foundation & Rooms · **Priority:** Could (post-launch) · **Estimate:** L
- **Narrative:** As a **hotel-style owner**, I want room-type availability to be counted against an inventory count with a proper lock, so that two concurrent guests can never both book the last "Deluxe King" (C8 overbooking race).
- **Acceptance criteria:**
  - **Given** a `Reservation(Room)` place, **when** availability is probed, **then** it succeeds only if `COUNT(overlapping active reservations for this room-type) < InventoryCount` (§2.1) — count-based, not block-based.
  - **Given** correction **C8** (the boundary race: with no rows to lock, `COUNT < InventoryCount` can double-insert at the last unit), **when** two guests race for the final unit, **then** the place **locks the `booking.room_inventory` counter row `FOR UPDATE`** before the count check — exactly one guest wins; the other gets a clean `409 Conflict` identifying the room-type; **no overbooking, ever**.
  - **Given** the "no cross-schema `FOR UPDATE`" rule (`AvailabilityBlock.cs:12`), **then** the inventory count is **mirrored into `booking.room_inventory`** (from `catalog.room_types.inventory_count`, §10-Q2 "mirror") so the lock stays inside the `booking` schema — the real reason to mirror inventory into `booking`.
  - **Given** the existing serializable-txn + `FOR UPDATE` guard (`PlaceBookingHandler.cs:152–209`), **then** it **generalizes** to the room case; the `40001 → ConflictException` map (`PlaceBookingHandler.cs:217–225`) is reused; a `Room` `IReservableResolver.availabilityProbe` wires the count check in.
  - **Given** an inventory-count change on a room-type, **then** the mirror in `booking.room_inventory` is kept consistent (event-driven or same-txn) so the lock target never drifts from the catalog source.
- **TDD plan:**
  - *Unit:* `RoomAvailabilityTests.Available_when_count_below_inventory`; `..._unavailable_at_inventory_boundary`; `RoomReservableResolverTests.Probe_uses_count_check`.
  - *Integration (RLS + **serializable-txn**, Testcontainers Postgres):* `RoomInventoryLockTests.Concurrent_place_for_last_unit_one_wins_one_409` (the C8 race — two parallel transactions, assert exactly one commit, zero overbooking); `..._for_update_lock_on_room_inventory_row`; `..._serialization_failure_maps_to_conflict`; `InventoryMirrorTests.room_inventory_stays_consistent_with_catalog_count`.
  - *E2E (Playwright):* `room-overbooking-guard.e2e` — two guest sessions race the last unit; one confirms, one sees "no longer available."
- **Technical notes:** New `booking.room_inventory` projection (`room_type_id`, `tenant_id`, `inventory_count`) mirrored from `catalog.room_types`. `PlaceBookingHandler` (`PlaceBookingHandler.cs`) gains a room branch: `SELECT … FROM booking.room_inventory WHERE room_type_id = :id FOR UPDATE` → count overlapping active reservations → compare → insert or 409, all inside the existing serializable transaction. `Room` resolver (`IReservableResolver`, VRB-402) implements `availabilityProbe` against this. **Reuse:** the serializable-txn + `FOR UPDATE` pattern (`PlaceBookingHandler.cs:152–225`) + the `40001 → ConflictException` map + `AvailabilityBlock` schema-locality rule. **This story is the single-line room place; the atomic N-line cross-tenant Place is the cart epic.**
- **UI/UX spec:** Guest booking (VRB-411): room-type availability shows remaining count ("2 left"); at the boundary the CTA disables + explains; a lost race surfaces an accessible "no longer available — please pick another" (live-region announced, not color-only). Owner sees per-room-type occupancy.
- **Configuration:** Under `Phase3:Rooms` (default off). No secret.
- **Rollout:** Additive `booking.room_inventory` table + mirror backfill, after VRB-406. Backward-compat: whole-house facilities have no room-inventory rows → the existing property availability path is untouched. Rollback: flag off; room-place branch dormant; mirror table inert.
- **Observability:** **Metric `booking.overbooking_attempt` (the C8 counter — increments whenever the `FOR UPDATE` guard rejects a would-be overbook)** + `booking.room_inventory.lock_wait_ms` (contention). Alert on any confirmed reservation count exceeding a room-type's `InventoryCount` (must be impossible — an alert here is a P1). Log `room_type_id`, `tenant_id`, remaining count on every room place.
- **Definition of Done:** unit + integration + **the concurrent-race test** green → architect review of the lock placement (schema-local, serializable) → staging: run the two-session overbooking race, confirm exactly-one-wins + `overbooking_attempt` increments → prod (flag off) → monitored with the overbooking alarm armed.
- **Dependencies:** **blocked-by** VRB-406 (renamed schema + room-types). **blocks** VRB-411 (guest room booking), VRB-412 (observability), and the cart epic's atomic multi-line Place.
- **Parallelisation:** Lane **R-booking** — parallel with R-pricing (407) + R-sync (409). Owns `PlaceBookingHandler` room branch, `booking.room_inventory`, the `Room` availability probe.

---

### VRB-409 — Per-room-type iCal feeds; reviews stay facility-level
- **Epic:** Phase 3 — Foundation & Rooms · **Priority:** Could (post-launch) · **Estimate:** M
- **Narrative:** As a **hotel-style owner**, I want an iCal channel feed per room-type (so OTAs sync each room-type's calendar independently) while reviews remain at the facility level, so that calendar sync matches how rooms are sold and reviews still describe the whole property (Q31).
- **Acceptance criteria:**
  - **Given** `sync.channel_feeds` (renamed/re-pointed in VRB-406), **when** extended, **then** it gains a **nullable `room_type_id`** — a feed is keyed to a facility (whole-house) OR a room-type (room mode); the Sync worker polls + emits per-room-type calendars.
  - **Given** a room-type feed, **when** the outbound `.ics` (`GET /feeds/{token}.ics`) is generated, **then** it reflects only that room-type's reservations/availability (count-aware — a room-type with `InventoryCount=5` is "busy" only when all 5 overlap).
  - **Given** Q31, **then** **reviews stay facility-level** — `reviews` already carries `(BookingId, PropertyId)` (now `facility_id` after the wave); **no per-room-type review dimension is added**; a room booking's review attaches to the facility.
  - **Given** a `WholeHouse` facility, **then** its single facility-level feed is unchanged (`room_type_id` null) — zero iCal change for whole-house listings.
  - **Given** amenities at both levels (Q31), **then** feed/review behavior doesn't depend on amenities — noted only to keep the Q31 split explicit (amenities both, reviews facility-only, iCal per room-type).
- **TDD plan:**
  - *Unit:* `ChannelFeedTests.room_type_id_nullable`; `RoomTypeIcalTests.Feed_reflects_only_room_type_reservations`; `..._count_aware_busy_only_when_all_units_overlap`; `ReviewScopeTests.Room_booking_review_attaches_to_facility`.
  - *Integration (Testcontainers):* `PerRoomTypeFeedTests.Poll_and_emit_per_room_type`; `..._whole_house_feed_unchanged_null_room_type`.
  - *E2E:* owner adds a room-type feed (VRB-410); the `.ics` is fetchable and count-correct.
- **Technical notes:** Additive nullable `room_type_id` on `sync.channel_feeds` (deferred from VRB-406 per its note). Sync worker (`src/Workers/VrBook.Workers.Sync`) per-feed `BackgroundTenantScope` loop is unchanged; it just keys by room-type when set. Outbound `.ics` generation becomes count-aware for room-types (busy = all `InventoryCount` units overlap). **No reviews change** beyond the wave's `property_id → facility_id` rename. **Reuse:** the existing per-tenant Sync worker + token-bucket rate limiter + `channel_feeds` CRUD; the count logic aligns with VRB-408's inventory model.
- **UI/UX spec:** Owner calendar/feeds surface (VRB-410) lists feeds per room-type in room mode; each shows its `.ics` URL + last-sync. A11y: feed status text+icon; copyable URL with accessible label. Whole-house owners see the single feed unchanged.
- **Configuration:** Under `Phase3:Rooms` (default off). Reuses `Sync:*` config (mind the known `Sync__DefaultPollIntervalMin` vs `Sync:DefaultPollIntervalMinutes` name mismatch, G-list — do not reintroduce).
- **Rollout:** Additive nullable `sync.channel_feeds.room_type_id`, after VRB-406. Backward-compat: whole-house feeds have null `room_type_id` → identical. Rollback: flag off; per-room-type feeds dormant; column inert.
- **Observability:** Metric `sync.feed.room_type_poll`, `sync.feed.ics_emitted{scope=facility|room_type}`. Log `feed_id`, `room_type_id?`, reservation count per emit. Alert on room-type feed busy-count exceeding `InventoryCount` (mirrors the VRB-408 invariant on the outbound side).
- **Definition of Done:** unit + integration green → review of count-aware busy logic + confirmation reviews stayed facility-level → staging: room-type feed emits correct `.ics`, review on a room booking attaches to facility → prod (flag off) → monitored.
- **Dependencies:** **blocked-by** VRB-406. **blocks** VRB-410 (owner feed UI), VRB-412 (observability).
- **Parallelisation:** Lane **R-sync** — parallel with R-pricing (407) + R-booking (408). Owns `Modules.Sync/**` room-type feed additions + the `channel_feeds.room_type_id` migration.

---

### VRB-410 — Owner room-mode management UI (ListingMode, room types, rate plans, photos)
- **Epic:** Phase 3 — Foundation & Rooms · **Priority:** Could (post-launch) · **Estimate:** L
- **Narrative:** As a **property owner**, I want a management UI to switch a facility to room mode and manage room-types (inventory, capacity, photos, amenities), their rate plans, and per-room-type calendar feeds, so that hotel-style listing is a complete owner workflow — not a headless API (ship complete vertical slices).
- **Acceptance criteria:**
  - **Given** the facility edit surface (`web/src/app/properties/[slug]` / admin), **when** the owner opens it (flag on), **then** they can set `ListingMode` (WholeHouse ↔ RoomTypes), and in RoomTypes mode **CRUD room-types** (name, `InventoryCount`, capacity, room-level amenities + photos), **manage rate plans per room-type** (VRB-407: name, price, policy model, prepayment), and **manage per-room-type iCal feeds** (VRB-409).
  - **Given** a WholeHouse facility, **then** the UI is exactly as today (flag off, or WholeHouse mode) — no regression to existing owner listing management.
  - **Given** switching an occupied facility to room mode, **then** the UI warns + guards (owner must define at least one room-type before publishing in room mode; the gated `Activate` Stripe-readiness still applies).
  - **States covered:** empty (no room-types), editing (add/remove/reorder room-types + plans), published (read-only + edit), error/loading; inventory/capacity validation inline.
  - **Given** WCAG 2.2 AA, **then** all controls are keyboard-operable, labeled, and error text is `aria-describedby`-tied; drag-reorder (if used) has a keyboard equivalent.
- **TDD plan:**
  - *Unit (Vitest):* `RoomTypeManager.test.tsx` — add/remove room-type, inventory/capacity validation, listing-mode toggle guard; `RatePlanEditor.test.tsx` — add plan, bps/prepayment inputs, policy-model pick; `RoomTypeFeeds.test.tsx`.
  - *Integration:* MSW-mocked contracts for facility `PATCH listingMode`, room-type CRUD, rate-plan CRUD, feed CRUD.
  - *E2E (Playwright):* `owner-room-mode.e2e` — switch to room mode → add 2 room-types with plans + a feed → publish → appears to guests.
- **Technical notes:** Extend the owner facility management routes/components under `web/src/app/properties/[slug]` + admin listing pages. New components (`web/src/components/rooms/`: `RoomTypeManager`, `RoomTypeForm`, `RatePlanEditor`, `RoomTypeFeeds`, `InventoryInput`). **Reuse:** the pricing admin patterns (`web/src/app/admin/pricing/page.tsx`, `SortableRuleRow`), `ConfirmActionModal` (destructive room-type delete), the image-upload flow (note: image upload endpoints are `501` stubs today, `CURRENT-STATE.md` §5 — room photos depend on that gap being closed or reuse whatever ships for facility images). Design-system: Tailwind + brand tokens; **mind the "almost no shared component library" gap** (`CURRENT-STATE.md` §11) — build room components against the existing token conventions, don't fork a third color system.
- **UI/UX spec:** Responsive: room-type list + editor is two-pane on desktop, stacked single-column on mobile (respect the **no-mobile-nav** gap — don't rely on desktop-only nav). Loading skeletons, empty/error states. Inventory + capacity numeric inputs with min/step + inline validation. Rate-plan cards show price+currency+policy. WCAG 2.2 AA throughout (labels, focus-visible, `aria-live` on save, keyboard reorder).
- **Configuration:** `Phase3:Rooms` + `NEXT_PUBLIC_PHASE3_ROOMS` gate the nav entry + routes (default off all envs). No secret.
- **Rollout:** Web behind the flag; deploy after backend VRB-405/407/408/409. Rollback: flag off hides room-mode UI entirely (no orphaned nav; WholeHouse management unchanged).
- **Observability:** Client funnel: listing-mode switch, room-type create/publish drop-off, rate-plan create. Web telemetry (existing).
- **Definition of Done:** Vitest + Playwright green → **design review (frontend-design skill)** → staging: owner builds a full room-mode facility end-to-end → **owner UI-tests in browser** (per [test-through-UI]) → prod (flag off) → monitored.
- **Dependencies:** **blocked-by** VRB-405, VRB-407, VRB-408, VRB-409. **blocks** VRB-411 (guest side needs a published room-mode facility to book).
- **Parallelisation:** Lane **R-web** — frontend, parallel with backend feature lanes once their contracts land. Owns `web/src/app/properties/[slug]` room additions + `web/src/components/rooms/**` (owner components).

---

### VRB-411 — Guest room-selection + booking UI (`Reservation(Room)`)
- **Epic:** Phase 3 — Foundation & Rooms · **Priority:** Could (post-launch) · **Estimate:** M
- **Narrative:** As a **guest**, I want to browse a hotel-style facility's room-types, see availability + rate plans, and book a specific room-type + rate plan, so that booking a room feels natural and results in a `Reservation(Room)` under my `Order`.
- **Acceptance criteria:**
  - **Given** a published RoomTypes facility, **when** a guest opens it (`web/src/app/properties/[slug]`), **then** it renders each room-type with photos, capacity, amenities, **remaining availability** (count-aware, VRB-408), and its **rate plans** (price + per-plan cancellation policy shown per option, VRB-407).
  - **Given** the guest selects a room-type + rate plan + dates + guests, **when** they book, **then** an `Order` (guest-scoped) with one `Reservation(Room, roomTypeId, Stay, GuestCount)` is placed through the reshaped single-line place (VRB-402/408); the resolved rate plan + policy are snapshotted (VRB-407).
  - **Given** the last unit is taken mid-flow (VRB-408 race), **then** the guest gets an accessible "no longer available — pick another" (live-region announced) and nothing is charged/held.
  - **Given** a WholeHouse facility, **then** the guest experience is **exactly as today** (no room selector) — zero change for whole-house listings.
  - **States:** browse (room-types + availability), select (plan + dates + guests), quote (price + policy), confirm (Tentative, 48h), error/loading/sold-out.
- **TDD plan:**
  - *Unit (Vitest):* `RoomTypePicker.test.tsx` — render room-types, remaining count, rate-plan selection, sold-out disable; `RoomBookingFlow.test.tsx` — select → quote → place.
  - *Integration:* MSW-mocked `GET /facilities/{slug}` (room-types + availability) + `POST /orders` place.
  - *E2E (Playwright):* `guest-room-booking.e2e` — browse room-types → pick a plan → book → Tentative `Reservation(Room)` under an `Order`; plus the sold-out path.
- **Technical notes:** Extend the guest facility detail route (`web/src/app/properties/[slug]`) with a room-type selector when `listingMode=RoomTypes`. New components (`web/src/components/rooms/`: `RoomTypePicker`, `RoomTypeCard`, `RatePlanSelector`, `AvailabilityBadge`). Booking reuses the existing quote + booking flow + `StripePaymentForm` (payment is single-tenant here — the split PI is the cart epic). **Reuse:** the whole-house booking flow, `PriceQuoteWidget`, `useAuthedQuery`. The guest whole-order read (`/me/orders`) uses the `MyBookingsHandler.cs:41–55` idiom.
- **UI/UX spec:** Responsive room-type grid → stacked cards on mobile; sticky quote/CTA. Availability badge ("2 left") with accessible text. Per-plan policy in an accessible disclosure (not tooltip-only). Sold-out + race states announced via `aria-live`. Design-system tokens; WCAG 2.2 AA (labels, focus management on the booking modal, keyboard-operable selectors, error text tied via `aria-describedby`).
- **Configuration:** `NEXT_PUBLIC_PHASE3_ROOMS` gates the guest room selector (default off all envs). When off, facilities render whole-house only.
- **Rollout:** Web behind the flag; deploy after VRB-408 + VRB-410. Rollback: flag off → guests see whole-house rendering only; no dangling room routes.
- **Observability:** Client funnel: room-type view → plan select → book → confirm; sold-out/race encounters. Ties to VRB-408 `overbooking_attempt` on the backend.
- **Definition of Done:** Vitest + Playwright green → design review → staging: guest books a room-type end-to-end + hits the sold-out path → **owner UI-tests in browser** → prod (flag off) → monitored.
- **Dependencies:** **blocked-by** VRB-408, VRB-410. **blocks** the cart epic's guest cart UI (which extends this room-selection surface into a multi-item cart).
- **Parallelisation:** Lane **R-web** — frontend, after VRB-410's shared room components land. Owns the guest room components in `web/src/components/rooms/**` + the guest facility-detail room path.

---

### VRB-412 — Observability: overbooking-attempt counter + lock contention + view-shim usage
- **Epic:** Phase 3 — Foundation & Rooms · **Priority:** Could (post-launch) · **Estimate:** M
- **Narrative:** As a **platform operator**, I want dedicated observability for the room-inventory lock and the rename-wave cutover, so that overbooking attempts, lock contention, and lingering legacy-view reads are visible before they become incidents or block the shim drop.
- **Acceptance criteria:**
  - **Given** the `FOR UPDATE` inventory guard (VRB-408), **then** a **`booking.overbooking_attempt` counter** increments on every rejected would-be overbook, tagged `room_type_id`/`tenant_id`; a dashboard shows attempts over time and `booking.room_inventory.lock_wait_ms` contention.
  - **Given** the hard invariant "confirmed reservations never exceed `InventoryCount`", **then** a **P1 alert** fires if it is ever violated (must be impossible — the alert is the tripwire).
  - **Given** the rename wave (VRB-406) view shims, **then** `catalog.legacy_properties_view.hits` (and any per-schema alias) is tracked so the operator can confirm it trends to **0** before scheduling the shim drop; an alert warns if the shim still has hits at the scheduled drop.
  - **Given** the dual-event window (VRB-404), **then** `booking.dual_event.emitted{event}` confirms both legacy + new events flow, and an alert catches a legacy handler that stops firing during the transition.
  - **Given** the operator, **then** an App Insights workbook shows: overbooking attempts, lock contention, per-room-type occupancy, feed-emit counts, view-shim cutover progress, dual-event parity.
- **TDD plan:**
  - *Unit:* `OverbookingMetricTests.Increments_on_guard_rejection`; `ViewShimMetricTests.Counts_legacy_view_hits`.
  - *Integration (Testcontainers):* `OverbookingAlertTests.Alert_fires_if_confirmed_exceeds_inventory` (inject an impossible state → alert); `LockContentionMetricTests.Emits_lock_wait_ms`.
  - *E2E:* the VRB-408 race test asserts the counter increments; a synthetic legacy-view read asserts the shim metric.
- **Technical notes:** Wire the metrics emitted by VRB-408 (overbooking attempt, lock wait) + VRB-406 (view-shim hits) + VRB-404 (dual-event) into App Insights custom metrics + Kusto alert rules (Bicep-declared, matching existing App Insights wiring, `infra/main.bicep`). New App Insights workbook. **Reuse:** existing App Insights + Log Analytics infra; no new observability stack. This story is the aggregation + alerting + dashboard over metrics the feature stories emit.
- **UI/UX spec:** Operator dashboard is an App Insights workbook (not app UI). Optional deferred `admin/platform/room-inventory-health` read-only page.
- **Configuration:** Alert thresholds in Bicep params (default: any confirmed>inventory = immediate P1; view-shim hits at scheduled drop = warn). Under `Phase3:Rooms` (no room traffic → no room alerts). No new secret.
- **Rollout:** Additive metrics + Bicep alert rules + workbook. No schema beyond what 404/406/408 add. Rollback: remove alert rules + workbook (no impact on the features themselves).
- **Observability:** This story *is* the observability. Self-check: the "alert fires if confirmed>inventory" integration test + the race-counter E2E are the DoD proofs.
- **Definition of Done:** unit + integration green → **synthetic overbooking-invariant-violation alert verified firing in staging** + view-shim metric verified → operator reviews the workbook → prod with alerts armed → monitored through the first room-mode bookings + the rename-wave shim drop.
- **Dependencies:** **blocked-by** VRB-408, VRB-409 (and reads VRB-404/406 metrics). **blocks** nothing (terminal ops-hardening story of this epic half).
- **Parallelisation:** Lane **R-ops** — parallel, tracks the feature lanes. Owns the App Insights workbook + Bicep alert rules + metric aggregation.

---

## Epic-level Definition of Done

- All VRB-400..412 green (unit + integration with RLS + serializable-txn on Testcontainers Postgres + Playwright E2E), with `Phase3:Rooms` + the cart flags proving **off-by-default zero-impact** on Phase 1/1.5 at launch.
- **Zero guest-facing change** proven: the `reshape-zero-change.e2e` 6-flow walk is byte-identical pre/post the foundation reshape (VRB-404).
- **No overbooking possible**: the concurrent last-unit race (VRB-408) always resolves to exactly one winner; the confirmed>inventory tripwire (VRB-412) never fires outside injected tests.
- **48h everywhere** (C10): no surviving 24h SLA literal in domain, worker, Bicep, or docs.
- The rename wave (VRB-406) cut over cleanly: `legacy_properties_view.hits → 0`, then shims + dual events dropped the following release.
- ADR written for the Order/Reservation split + `ordering` schema + `app.user_id` GUC; `docs/MASTER_PLAN.md` Phase 3 rows added; close-out written.
- Owner UI-tested owner room-management (VRB-410) + guest room-booking (VRB-411) in the browser on staging.

## Parallelisation lane map

| Lane | Scope | Stories | Key files owned | Parallelizable? |
|------|-------|---------|-----------------|-----------------|
| **F — foundation spine** | Order/Reservation split, GUC, SLA, backfill | 400 → 401 → 402 → 403 → 404 | `Modules.Ordering/**`, `Modules.Booking/Domain/Reservation.cs`, `TenantGucCommandInterceptor.cs`, backfill migrations | **NO — strictly sequential** |
| **R-catalog** | Facility/RoomType + the rename wave | 405 → 406 | `Modules.Catalog/Domain/Facility.cs`, `RoomType.cs`, the 5-schema rename migrations + view shims | **NO — the wave is one atomic deploy** |
| R-pricing | RatePlan + per-room-type pricing | 407 | `Modules.Pricing/**`, `pricing.rate_plans`/`room_type_id` | Yes (after 406) |
| R-booking | Count-availability + inventory lock | 408 | `PlaceBookingHandler` room branch, `booking.room_inventory` | Yes (after 406) |
| R-sync | Per-room-type iCal | 409 | `Modules.Sync/**`, `channel_feeds.room_type_id` | Yes (after 406) |
| R-web | Owner + guest room UI | 410 → 411 | `web/src/components/rooms/**`, `web/src/app/properties/[slug]` room paths | Owner-then-guest; parallel to backend once contracts land |
| R-ops | Observability | 412 | App Insights workbook, Bicep alert rules | Yes (tracks 408/409) |

**The two non-parallelizable spines are Lane F (the reshape, 400–404) and the VRB-405→406 rename wave.** Everything else (407/408/409, then 410/411, with 412) fans out only after those spines land. Nothing in this epic ships until the reshape is on staging; the cart epic ([`EPIC-phase3-cart.md`](EPIC-phase3-cart.md), VRB-42x) ships only after this entire epic half is on staging.
