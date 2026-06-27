# Phase 3 Reconnaissance — Architect's Independent Verdict

**Date**: 2026-06-27. Decision pending user. Author: architect consult (independent review of my middle-path recommendation).

## Phase 3 scope (per user, not in any existing plan doc)

Three product expansions, currently unbuilt:

1. **Hotel-style rooms within a facility** — currently 1 booking = 1 whole Property; no `Room`/`Unit` sub-aggregate.
2. **Multi-unit booking** — one guest books N properties/rooms in one atomic checkout (like Booking.com's multi-room cart).
3. **Package bundling (OTA)** — Itinerary spanning Stay + Flight + Rental Car + Activities + Tours, possibly cross-country (the user's example: "Sri Lanka tour with flights, cars, multiple stays, excursions").

## The user's question

> *"What is the best time to design Phase 3? If we do it now, we can plan and keep placeholders while developing Slice 0-7, Phase 1.5 and Phase 2? Or is it easy to redesign everything later?"*

## My middle-path recommendation (under review)

Don't design Phase 3 in full now. Spend half a day on a reconnaissance doc identifying 3-4 OPS.M.4-10 decisions where a small Phase-3-aware change today is cheap, vs an expensive retrofit later. My candidates:

- **OPS.M.5**: add `connect_account_kind` enum on `tenants` to leave room for future supplier-marketplace topology.
- **OPS.M.4**: leave `TenantAuthorizationBehavior` signature flex for a future "cross-tenant scope" exception.
- **OPS.M.9**: decide RLS bypass policy now.

## Architect's verdict — top-line

**The middle path is right, but two of three candidates are misaimed, and you're missing the actually load-bearing one.** Specifically: the Stripe Connect enum is premature; OPS.M.4 doesn't need pre-shaping; and the real cheap-now/expensive-later decision is the **Booking aggregate shape (Property-singular vs LineItem-plural)** — which is *already half-built* in the codebase and the user's middle-path didn't acknowledge.

## Pushback on each candidate

### OPS.M.5 `connect_account_kind` enum — **disagree, drop it**

`identity.tenants.stripe_account_id` is a nullable string today. A future "travel-agency tenant with N supplier sub-accounts" is a *relationship* (one tenant → many Connect accounts), not a *kind* (enum tag on one). The retrofit isn't an `ALTER TYPE`; it's a new `tenant_connect_accounts` table regardless of what enum you add now. **Cost of the placeholder > cost of the eventual retrofit.** The enum column will become vestigial.

### OPS.M.4 `TenantAuthorizationBehavior` signature flex — **disagree, don't pre-shape**

A behavior with an unused "cross-tenant scope" escape hatch is speculation overhead. The behavior is ~50 LOC. Rewriting it when Phase 3 arrives is half a day. Pre-shaping its signature against an imaginary itinerary aggregate produces worse code now and won't match Phase 3's real shape.

### OPS.M.9 RLS bypass policy — **partially agree, reframe**

The real decision isn't "add a bypass." It's: **does the RLS connection factory accept an explicit `SET LOCAL app.tenant_id = '...'` per-statement, or does it bind once per-connection?**

- Per-connection binding is cheaper to write but **cannot** serve cross-tenant itinerary reads.
- Per-statement is marginally slower but extensible.

Decide that *shape* in OPS.M.9 — it's not a retrofit when you change it, it's a rewrite of every read path. **This one is real and load-bearing.** Keep, but reframe the question to "binding granularity," not "bypass on/off."

## The candidate the user missed — load-bearing for both Hotel Rooms AND Multi-unit Cart

### Booking aggregate: Property-singular vs LineItem-plural

The codebase already has `Booking._lineItems` (`BookingLineItem` with Kind/Label/Quantity/UnitAmount) — **but** `Booking.PropertyId` and `Booking.PropertyTitle` are still aggregate-root scalars and the `BookingPlaced` event carries a single `propertyId`. This is a **half-finished line-item model**.

Phase 3 multi-unit cart needs `BookingLineItem` to carry `ReservableId` + `ReservableKind` (Property | Room | Flight | Activity), and the root needs to *drop* `PropertyId`. Payment is already `BookingId`-keyed (good — no rewrite there). But:

- `pricing.pricing_plans.property_id`, `reviews.reviews.property_id`, `sync.channel_feeds.property_id`, `messaging.threads` (verify) all assume one-property-per-booking transitively.
- **Slice 5 (Reviews) is about to ship `Review.PropertyId` keyed off `Booking.PropertyId`.** If multi-unit cart lands later, "which property is this review of?" needs an answer that Slice 5's schema won't have room for without a backfill.

**Cheap-now decision (Slice 5 timeframe, not OPS.M.3):** Have Reviews key off `(BookingId, PropertyId)` *composite* from day 1, not `BookingId → derive PropertyId`. **Costs ~1 hour now, saves a Reviews schema migration later.** This is the genuine YAGNI exception the middle-path framework justifies.

### Hotel rooms separately — does NOT need pre-shaping in OPS.M.3

`tenant_id` lives on `properties`. A future `rooms` table is a child of `properties` and inherits tenant scope via the parent's FK. The user's worry that "tenant_id at property level is load-bearing under rooms" is **wrong** — rooms denormalize tenant_id from their parent property, same pattern as `property_images.tenant_id` today. **No retrofit cost.**

## The "modular refactor is cheap" claim — mostly right, one caveat

`Property → Facility/Room` rename is contained within Catalog's own boundary. **But** `pricing_plans.property_id`, `reviews.property_id`, `channel_feeds.property_id` are cross-schema FKs OPS.M.3 just shipped. Renaming `properties` → `facilities` is a coordinated migration across 4 schemas in one deploy wave. Doable, but not free — call it **2 days**, not "contained in Catalog." Budget honestly.

## Phase 3 internal ordering

1. **Hotel rooms first** — purely additive (`rooms` table under `properties`, `BookingLineItem.ReservableKind=Room`), no cross-tenant primitives, ships fastest, validates the line-item refactor end-to-end on one module.
2. **Multi-unit cart second** — same tenant, multi-property/room. Needs payment-pipeline change (one PaymentIntent → N transfers, OR Stripe's native multi-destination `transfer_data` model). Builds on the room work.
3. **OTA bundling — NOT Phase 3.** Promote to Phase 4. Reasons: supplier-tenant polymorphism, cross-tenant authorization, cross-tenant RLS, FX, and wholly new domains (Flight/Car/Activity). Different shape entirely.

## Timing relative to Phase 1.5 / Phase 2

All three Phase 3 items sit *after* OPS.M.10. **None should leak into Phase 2** (loyalty tiers, advanced pricing) because Phase 2 assumes the property-singular booking model. Either:

- **Do Phase 3 (1) + (2) between OPS.M.10 and Phase 2** — architect's recommendation, OR
- **Accept Phase 2 will need rework** when Phase 3 lands.

Architect's call: rooms + multi-unit cart **between OPS.M.10 and Phase 2.** OTA bundling deferred to Phase 4.

## Net effect on existing plans

- **No change to OPS.M.4** — middle path was wrong about pre-shaping.
- **No change to OPS.M.5** — middle path was wrong about the enum.
- **One concrete change to OPS.M.9** — RLS connection factory binding-granularity decision (per-connection vs per-statement) becomes a design question with a Phase 3 implication, captured in OPS.M.9's plan when it's written.
- **One concrete change to Slice 5 (Reviews)** — composite `(BookingId, PropertyId)` key on `Review`, ~1 hour added scope, save a future migration. Note in `SLICE5_PLAN.md` when it's written.
- **Phase 3 internal ordering decided**: rooms → multi-unit cart → (OTA promoted to Phase 4).

## What this doc explicitly does NOT do

- It does NOT design hotel-room aggregates, schemas, or migrations.
- It does NOT design multi-unit cart aggregates or payment pipelines.
- It does NOT design OTA primitives (deferred to Phase 4).
- It does NOT change OPS.M.3 (already shipped) or any in-flight OPS.M.4-10 plan beyond the two narrow notes above.

Detailed Phase 3 design happens when there's market signal — concretely, when ~5 tenants have asked for hotel rooms or 2-3 have asked for multi-unit cart.
