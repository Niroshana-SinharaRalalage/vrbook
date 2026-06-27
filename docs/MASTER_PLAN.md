# VrBook Master Plan — Single Source of Truth

**Last revised**: 2026-06-27.
**Status**: Slices 0–3 ✅ shipped. OPS.M.0–3 ✅ shipped (the Phase-1.5 schema half is done). Sequencing reordered 2026-06-27 to **Option C** per architect: Slice 4 → OPS.M.4–10 → Slice 5–7 → Phase 3 → Phase 4. Phase 3 + Phase 4 defined for the first time in this revision.

This document is the single index. It points at the detailed plans for each phase; do not duplicate content here — update the linked plans and bump the date at the top of this file.

> **One TODO, one tag scheme** (per `docs/EXECUTION_PLAN.md` §1). Slice 0–7 are the Phase-1 demo path; OPS.M.1–10 is the Phase-1.5 multi-tenancy gate; OPS.1–8 is launch hardening; Phase 3 covers hotel-style rooms + multi-unit cart; Phase 4 covers OTA package bundling. No parallel naming.

---

## 1. Where we are right now

| Phase | Item | Status | Commits | Verified |
|---|---|---|---|---|
| **Phase 1** | Slice 0 — Honest booking lifecycle (race-free hold, FOR UPDATE, manual capture, expiry worker, ACS resource, calendar query) | ✅ | `0060216` → `0dbeed6` | staging |
| | Slice 1 — Owner onboards a property | ✅ | `c4aea3f` → `da6dfb9` | staging |
| | Slice 2 — Guest books, owner confirms (the credibility test) | ✅ | `9896882` → `3fddd45` | staging |
| | Slice 2 polish — DevAuth persona switcher works cross-origin | ✅ | `ca8ffd6` | staging |
| | Slice 3 — Calendar + iCal + owner blocks (with §10.1 forward-compat `tenant_id NULL` on every new table) | ✅ | `f8ffd04` → `c8f8b8b` | staging |
| | **Slice 4 — Notifications that actually send** | ⏭ next (per Option C) | plan: `d9fa889` ([docs/SLICE4_PLAN.md](SLICE4_PLAN.md)) — see [SEQUENCING doc](SEQUENCING_OPS_M_VS_SLICES.md) for why Slice 4 is interleaved here | |
| | Slice 5 — Stay completes → review + loyalty | ⏭ after OPS.M.10 | — | Ship `Review` with composite `(BookingId, PropertyId)` key per architect Phase-3 reconnaissance note. |
| | Slice 6 — Host↔Guest chat + pricing power-user | ⏭ after Slice 5 | — | |
| | Slice 7 — Reports + realtime polish | ⏭ after Slice 6 | — | |
| **Phase 1.5** | OPS.M.0 — Microsoft Entra External ID cutover | ✅ | `c2bc4cd` → `989104d` | staging |
| | OPS.M.1 — Tenant aggregate + memberships | ✅ | `b7ae589` → `3ce5f96` | staging |
| | OPS.M.2 — `TenantId` claim wiring + `ICurrentUser` shape (DB-wins precedence per ADR-0014) | ✅ | `84d6c05` → `9d13cb3` | staging |
| | OPS.M.3 — `tenant_id` column rollout (Wave A/B/C + Step 7) | ✅ | `a60e722` → `2a3d2b2` | staging |
| | OPS.M.4 — `TenantAuthorizationBehavior` + drop per-handler owner checks | ⏭ after Slice 4 | plan: `df5580b` ([docs/MULTI_TENANCY_OPS_PLAN.md](MULTI_TENANCY_OPS_PLAN.md)) | |
| | OPS.M.5 — Stripe Connect Express | ⏭ | same plan | |
| | OPS.M.6 — iCal poller tenant-scoping + outbound rate limit | ⏭ | same plan | |
| | OPS.M.7 — Tenant Admin onboarding wizard | ⏭ | same plan | |
| | OPS.M.8 — Super Admin console | ⏭ | same plan | |
| | OPS.M.9 — RLS policies + bypass connection factory | ⏭ | same plan — **Phase-3 note**: decide per-statement vs per-connection `app.tenant_id` binding granularity in this slot, per [PHASE_3_RECONNAISSANCE](PHASE_3_RECONNAISSANCE.md) | |
| | OPS.M.10 — Cross-tenant isolation test pack | ⏭ | same plan | |
| **Phase 1.5 ops** | OPS.1–8 — Launch hardening (Pact, k6, ZAP, Trivy, key rotation, Entra, DKIM) | ⏭ | [`docs/EXECUTION_PLAN.md`](EXECUTION_PLAN.md) §8 | |
| **Phase 3** | P3.1 — Hotel-style rooms within a Property/Facility (purely additive: `rooms` child of `properties`, `BookingLineItem.ReservableKind=Room`) | ⏭ after Phase 1.5 + Slice 5–7 | plan: TBD — see [PHASE_3_RECONNAISSANCE](PHASE_3_RECONNAISSANCE.md) for the architect's verdict | |
| | P3.2 — Multi-unit cart (one guest books N properties/rooms in one atomic checkout; Stripe `transfer_data[]` multi-destination) | ⏭ after P3.1 | plan: TBD | |
| **Phase 4** | P4.1 — OTA package bundling (Itinerary with Flights + Cars + Activities + Stays; supplier-tenant polymorphism; cross-tenant RLS + FX) | ⏭ after Phase 3 | plan: TBD — promoted from Phase 3 to Phase 4 per architect (different shape entirely) | |

---

## 2. End-to-end sequence and timeline

**Order locked 2026-06-27 — Option C per architect** (see [`docs/SEQUENCING_OPS_M_VS_SLICES.md`](SEQUENCING_OPS_M_VS_SLICES.md)). Originally Slices 4–7 → OPS.M.0–10 → OPS.1–8. Actual order is **Slice 4 → OPS.M.0–10 → Slice 5–7 → Phase 3 → Phase 4 → OPS.1–8**. The reorder happened because OPS.M.0 (Entra cutover) was a hard prerequisite for OPS.M.2 and we shipped it ahead of Slice 4, then continued into OPS.M.1–3. Option C interleaves Slice 4 next (the only Slice 4–7 item that doubles as cross-cutting infrastructure: ACS pipeline + deferred-send worker + 10 templates) before resuming OPS.M.

| Order | Item | Est. days | Cumulative | Why this slot |
|---|---|---|---|---|
| 1 | OPS.M.0 ≡ OPS.7 — Entra External ID cutover | 2 | 2 | ✅ **Shipped 2026-06-25** (c2bc4cd … 989104d). Hard prerequisite for OPS.M.2. |
| 2 | OPS.M.1 — Tenant aggregate + memberships | 2 | 4 | ✅ **Shipped 2026-06-26** (b7ae589, 74aaf64, 3ce5f96). See `docs/OPS_M_1_PLAN.md`. |
| 3 | OPS.M.2 — `TenantId` claim wiring + `ICurrentUser` shape | 1.5 | 5.5 | ✅ **Shipped 2026-06-26** (84d6c05, afbfb61, 9d13cb3). DB-wins precedence per ADR-0014. |
| 4 | OPS.M.3 — `tenant_id` column rollout (Wave A/B/C + Step 7) | 4 | 9.5 | ✅ **Shipped 2026-06-27** (`a60e722` → `2a3d2b2`). 9 modules; ~20 NOT NULL columns; cross-schema FKs to `identity.tenants`. |
| **5** | **Slice 4 — Notifications that actually send** | **3** | **12.5** | ⏭ **next**. Interleaved here per architect Option C — Slice 4 doubles as cross-cutting infrastructure (ACS pipeline + `NotBeforeUtc` worker + 10 templates) that OPS.M.7 onboarding and Slices 5–7 all consume. Author it RLS-aware (worker uses bypass connection factory) so it doesn't need rewriting after M.9. |
| 6 | OPS.M.4 — `TenantAuthorizationBehavior` + drop per-handler owner checks | 1.5 | 14 | Net code reduction. **Phase-3 note**: do NOT pre-shape for cross-tenant scope; rewrite when Phase 4 OTA actually needs it (architect verdict). |
| 7 | OPS.M.5 — Stripe Connect Express | 4 | 18 | Parallel with M.7. **Phase-3 note**: do NOT add `connect_account_kind` enum; Phase 4 multi-supplier tenants need a relationship (new table), not a kind. |
| 8 | OPS.M.6 — iCal poller tenant-scoping + outbound rate limit | 1 | 19 | Parallel against M.5. |
| 9 | OPS.M.7 — Tenant Admin onboarding wizard UI + first-property → Stripe link | 3 | 22 | Depends on M.5. |
| 10 | OPS.M.8 — Super Admin console | 4 | 26 | Parallel against M.7. |
| 11 | OPS.M.9 — RLS policies + bypass connection factory | 1.5 | 27.5 | Depends on M.3c. **Phase-3 binding-granularity decision**: choose per-statement `SET LOCAL app.tenant_id` over per-connection so the same factory can serve Phase 4 OTA cross-tenant itinerary reads (architect verdict). |
| 12 | OPS.M.10 — Cross-tenant isolation test pack | 2 | 29.5 | Parallel against M.5/M.7. |
| **= Phase 1.5 demo-able** | | **~16 critical-path days** + parallelism | **realistic 4–5 calendar weeks 1 engineer; 2.5–3 weeks two engineers** | Multi-tenant SaaS bring-up complete. |
| 13 | Slice 5 — Review + loyalty | 2 | 31.5 | Resume Phase-1 slices. Ship `Review` with composite `(BookingId, PropertyId)` key — saves a future migration when Phase 3 multi-unit cart lands (architect verdict). |
| 14 | Slice 6 — Chat + pricing rules | 3 | 34.5 | Daily-driver fit-and-finish. |
| 15 | Slice 7 — Reports + realtime | 2 | 36.5 | Operator polish; SignalR Serverless provisioning. |
| **= Phase 1 demo-able** | | **36.5** | | All seven slices' acceptance criteria met, on top of multi-tenancy. |
| 16 | OPS.1 — Pact contract tests | — | | Launch hardening starts. |
| 17 | OPS.2 — Playwright E2E suite (F1.1) | — | | |
| 18 | OPS.3 — k6 load test (50 RPS, 5 min, P95 < 1s) | — | | |
| 19 | OPS.4 — OWASP ZAP baseline in CI | — | | |
| 20 | OPS.5 — Trivy + SBOM signing | — | | |
| 21 | OPS.6 — Stripe key rotation | — | | |
| 22 | OPS.8 — Custom domain DKIM/SPF for ACS email (per-tenant subdomains deferred to Phase 2/3) | — | | Per MULTI_TENANCY_OPS_PLAN §8 / EXECUTION_PLAN §8.A note. |
| **= Phase 1 launch ready** | | | | |
| 23 | P3.1 — Hotel-style rooms | TBD | | Architect-recommended Phase 3 start. Purely additive (`rooms` child of `properties`, `BookingLineItem.ReservableKind=Room`). Validates the line-item refactor on one module. |
| 24 | P3.2 — Multi-unit cart | TBD | | Same-tenant, multi-property/room. Uses Stripe `transfer_data[]` native multi-destination. |
| **= Phase 3 demo-able** | | | | Booking.com-style multi-unit + per-room inventory. |
| 25 | P4.1 — OTA package bundling | TBD | | Supplier-tenant polymorphism, cross-tenant RLS (the per-statement binding granularity from M.9 pays off here), FX, new domains (Flight/Car/Activity), Itinerary aggregate. Sits entirely on top of Phase 3's line-item refactor. |
| **= Phase 4 demo-able** | | | | Expedia/tour-operator style cross-supplier itineraries. |

`OPS.7` does NOT appear in the OPS launch row because it lands inside the OPS.M.0 slot as a hard prerequisite for OPS.M.2.

**Order rationale (2026-06-27).** Multi-tenancy lands first because the launch hardening's blast radius (Pact contracts, k6 load shape, ZAP attack surface, key rotation policy) all change once tenants exist. Slice 4 interleaves because it's the only Slice 4–7 item that's *infrastructure* (ACS + worker + templates) consumed by every later slice and by OPS.M.7's onboarding demo. Slice 5–7 wait until after M.10 so their handlers ship without per-handler tenancy boilerplate that M.4 would delete and without report queries that M.9 would force a rewrite of. Phase 3 + Phase 4 sit after launch hardening so they don't disturb a launched product.

---

## 3. Phase boundaries and what "done" means

### Phase 1 done (slices 0–7)

A single seeded Owner with DevAuth can:
- Create a property end-to-end through the browser.
- Receive a guest reservation, see it as Tentative, confirm/reject in ≤3 clicks, see payment captured.
- Plant manual blocks on the calendar; iCal feed inbound + outbound work; conflict modal works.
- Receive a real email (`niroshanaks@gmail.com`) for every funnel-critical lifecycle event.
- See completed bookings become reviewable; loyalty tier advances on stay completion.
- Hold a live messaging thread with the guest; rule-based pricing alters quotes correctly.
- Pull occupancy/revenue/ADR reports as CSV; dashboard pill updates in real time.

### Phase 1.5 done (OPS.M.1–10)

Two distinct tenants exist. Tenant A and Tenant B each have their own properties, bookings, Stripe Connect account, iCal feeds, and audit log. Cross-tenant isolation is enforced at app + RLS layers and proven by the OPS.M.10 isolation test pack. Super Admin console can list/suspend/impersonate any tenant. Entra External ID has replaced DevAuth in production.

### Phase 1 launch ready (OPS.1–8 less OPS.7)

CI gates: Pact, Playwright, k6, ZAP, Trivy, SBOM. Stripe keys rotated. ACS DKIM verified for the platform sender. No remaining `TODO: production`. Runbooks current.

### Phase 3 done (P3.1 + P3.2)

A facility (formerly Property) can hold N bookable rooms or units. One guest can book multiple rooms or properties in a single atomic checkout with a single Stripe PaymentIntent that splits across the right Connect accounts. Reviews work at room granularity. iCal export works per-room. Pricing rules can target the room or the facility. Existing single-property listings continue to work unchanged via "facility with one room".

### Phase 4 done (P4.1)

A travel-agency tenant can compose an Itinerary spanning Stay + Flight + Rental Car + Activity legs across multiple supplier tenants and countries, sold as a single product to a guest with multi-currency settlement and per-leg cancellation. Existing accommodation-only tenants are unaffected.

---

## 4. Cross-cutting rules that apply across every slice / OPS item

These are locked-in policies; do not deviate without amending this section.

1. **Forward-compat `tenant_id`** on every new table from Slice 3 onward (REPLAN §10.1). `tenant_id uuid NULL REFERENCES identity.tenants(id)` from the first migration. OPS.M.3 backfills and tightens to NOT NULL.
2. **Manual capture** stays the Phase 1 default (Slice 0 decision, REPLAN §9 lockdown #1).
3. **6-hour tentative window** stays the Phase 1 default (REPLAN §9 lockdown #2).
4. **3 pricing rules** (Seasonal + Last-minute + LOS) in Slice 6. Gap-night and Occupancy deferred to Phase 2 (REPLAN §9 lockdown #3).
5. **10 notification templates** in Slice 4 (REPLAN §9 lockdown #4). Remaining 8 from §13 land in Slice 5 backfill or Phase 2.
6. **Single platform email domain** in Phase 1.5; per-tenant subdomains/DKIM in Phase 2 (MULTI_TENANCY_OPS_PLAN §8).
7. **In-process MediatR + outbox** is the Phase-1 event bus. Outbox → Service Bus relay deferred to A11/Phase 2 (REPLAN §5).
8. **DevAuth covers Phase 1 demo and pilot.** Real Entra cutover lands inside OPS.M (OPS.M.0 / OPS.7).
9. **Architect consult required** for any multi-module or sequencing decision. Commit the result as a doc under `docs/` before writing code.

---

## 5. Where each plan lives

| Doc | Owns |
|---|---|
| `docs/REPLAN.md` | The Slice 0–7 contract (active sequencing primitive). §10 sketches the OPS gate; §10.1 codifies the `tenant_id` forward-compat policy. |
| `docs/SLICE4_PLAN.md` | Slice 4 detail (commit split, six load-bearing decisions, scope-cut order). |
| `docs/MULTI_TENANCY_OPS_PLAN.md` | OPS.M.1–10 detail (tenancy model, Stripe Connect, RLS, isolation test pack, pushback items). |
| `docs/OPS_M_0_PLAN.md` / `OPS_M_1_PLAN.md` / `OPS_M_2_PLAN.md` / `OPS_M_3_PLAN.md` | Per-slot detailed plans for the shipped OPS.M items. |
| `docs/SEQUENCING_OPS_M_VS_SLICES.md` | 2026-06-27 architect verdict on Option C interleave (Slice 4 → OPS.M.4–10 → Slice 5–7). |
| `docs/PHASE_3_RECONNAISSANCE.md` | 2026-06-27 architect verdict on Phase 3 timing + the narrow set of door-opening decisions to carry into OPS.M.9 + Slice 5. Phase 3 itself is NOT designed yet. |
| `docs/EXECUTION_PLAN.md` | Legacy A-number sequencing — superseded by REPLAN.md. Retained because §8 owns the OPS.1–8 launch-hardening list and §8.A carries the OPS.M.1–10 summary table. |
| `docs/MASTER_PLAN.md` (this file) | Single index. No new content lives here; bump dates and link out. |

---

## 6. Open optionality (decisions deferred, not abandoned)

These will be picked up at the slot indicated; revisit only if the slot is reached or a fresh constraint surfaces:

- **Self-serve tenant sign-up** — Phase 2 per MULTI_TENANCY_OPS_PLAN §1. Schema supports it; UI gates on Super Admin invite for Phase 1.5.
- **`tenant_member` role** (housekeeper, co-host) — schema supports it; UI ships Phase 2.
- **Per-tenant ACS Email resources + DKIM** — Phase 2 per MULTI_TENANCY_OPS_PLAN §8 / EXECUTION_PLAN §8 OPS.8 footnote.
- **Channel Manager API push** (AirBnB direct API rather than iCal) — Phase 2.
- **Service Bus relay** for outbox → cross-process consumers — A11/Phase 2.
- **5-rule pricing engine** (Gap-night + Occupancy in addition to Seasonal/Last-minute/LOS) — Phase 2.
- **Auto evidence submission on Stripe disputes** — Phase 2.
- **Tenant subdomain routing (`a.vrbook.com`)** — Phase 2 cosmetic per MULTI_TENANCY_OPS_PLAN §11.

---

## 7. How to use this document

- **Starting a new session**: read this file first, find your current slot in §1 + §2, then read the linked detail plan for that slot.
- **Finishing a slot**: edit §1 row to ✅, paste commit range, mark verified. Bump §header date.
- **Re-prioritizing**: edit §2 with the new order; explain in a one-line revision note appended below.
- **Adding a slot**: open the relevant detail plan first; this file gets a row pointing at it.

---

## 8. Revision log

- 2026-06-14 — Initial assembly. Slices 0–3 marked ✅; Slice 4 next (plan `d9fa889`); OPS.M sequenced after Slice 7; OPS.7 Entra moved into the OPS.M critical path; §10.1 forward-compat policy carried forward.
- 2026-06-27 — **Sequence reorder + Phase 3 + Phase 4 added**. (1) Mark OPS.M.0–3 ✅ shipped 2026-06-25 → 2026-06-27. (2) Lock Option C order per architect: Slice 4 → OPS.M.4–10 → Slice 5–7 → Phase 3 → Phase 4 → OPS.1–8. (3) Define Phase 3 (P3.1 hotel-style rooms + P3.2 multi-unit cart) and Phase 4 (P4.1 OTA package bundling). (4) Carry forward two narrow door-opening notes from `PHASE_3_RECONNAISSANCE.md`: per-statement `app.tenant_id` binding in OPS.M.9, and composite `(BookingId, PropertyId)` Review key in Slice 5. Three explicit "do NOT pre-shape" instructions: M.4 signature flex, M.5 `connect_account_kind` enum, and OPS.M.3 retrofit for rooms (rooms denormalize tenant_id from parent property — no retrofit needed). All other Phase 3 design deferred until after market validation.
