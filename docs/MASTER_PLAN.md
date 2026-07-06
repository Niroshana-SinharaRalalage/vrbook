# VrBook Master Plan — Single Source of Truth

**Last revised**: 2026-07-05.
**Status**: Slices 0–3 ✅ shipped. Slice OPS.M.0–3 ✅ shipped (the Phase-1.5 schema half is done). Sequencing now **Option A** per architect re-evaluation 2026-06-27: complete Slice OPS.M.4 → M.10 fully → Slice 4 → Slice 5–7 → Slice OPS.1–8 → Phase 3 → Phase 4. Option A reverses the morning's Option C verdict after the Slice 4 plan review surfaced that Slice 4 was being asked to author multi-tenancy infrastructure that properly belongs to Slice OPS.M.4/M.7/M.9.

This document is the single index. It points at the detailed plans for each phase; do not duplicate content here — update the linked plans and bump the date at the top of this file.

> **One TODO, one tag scheme — every work item is a `Slice`.** Original product slices keep their integers (`Slice 0` … `Slice 7`). Multi-tenancy items become `Slice OPS.M.0` … `Slice OPS.M.10`. Launch hardening becomes `Slice OPS.1` … `Slice OPS.8`. Phase 3 hotel-rooms + multi-unit cart continue the integer sequence as `Slice 8` (P3.1) + `Slice 9` (P3.2). Phase 4 OTA bundling is `Slice 10` (P4.1). The `P3.x` / `P4.x` codes stay parenthetically so cross-references in `PHASE_3_RECONNAISSANCE.md` still resolve. **Detail-plan filenames (`OPS_M_3_PLAN.md` etc.) and existing commit messages are immutable history — they are not retroactively renamed.**

---

## 1. Where we are right now

| Phase | Item | Status | Commits | Verified |
|---|---|---|---|---|
| **Phase 1** | Slice 0 — Honest booking lifecycle (race-free hold, FOR UPDATE, manual capture, expiry worker, ACS resource, calendar query) | ✅ | `0060216` → `0dbeed6` | staging |
| | Slice 1 — Owner onboards a property | ✅ | `c4aea3f` → `da6dfb9` | staging |
| | Slice 2 — Guest books, owner confirms (the credibility test) | ✅ | `9896882` → `3fddd45` | staging |
| | Slice 2 polish — DevAuth persona switcher works cross-origin | ✅ | `ca8ffd6` | staging |
| | Slice 3 — Calendar + iCal + owner blocks (with §10.1 forward-compat `tenant_id NULL` on every new table) | ✅ | `f8ffd04` → `c8f8b8b` | staging |
| | Slice 4 — Notifications that actually send | ⏭ after Slice OPS.M.10 | plan: `d9fa889` ([docs/SLICE4_PLAN.md](SLICE4_PLAN.md)) — re-review against post-M.10 world before starting (per architect 2026-06-27 re-evaluation) | |
| | Slice 5 — Stay completes → review + loyalty | ⏭ after Slice 4 | — | Ship `Review` with composite `(BookingId, PropertyId)` key per architect Phase-3 reconnaissance note. |
| | Slice 6 — Host↔Guest chat + pricing power-user | ⏭ after Slice 5 | — | |
| | Slice 7 — Reports + realtime polish | ⏭ after Slice 6 | — | |
| **Phase 1.5** | Slice OPS.M.0 — Microsoft Entra External ID cutover | ✅ | `c2bc4cd` → `989104d` | staging |
| | Slice OPS.M.1 — Tenant aggregate + memberships | ✅ | `b7ae589` → `3ce5f96` | staging |
| | Slice OPS.M.2 — `TenantId` claim wiring + `ICurrentUser` shape (DB-wins precedence per ADR-0014) | ✅ | `84d6c05` → `9d13cb3` | staging |
| | Slice OPS.M.3 — `tenant_id` column rollout (Wave A/B/C + Step 7) | ✅ | `a60e722` → `2a3d2b2` | staging |
| | Slice OPS.M.4 — `TenantAuthorizationBehavior` + event payload extensions + drop per-handler owner checks | ✅ | `98c8cab` → `a0f58f8` | staging |
| | Slice OPS.M.5 — Stripe Connect Express | ✅ | `6c27d82` → `2d39a3a` | staging (gateway + endpoints; full webhook fixture pack runs in CI) |
| | Slice OPS.M.6 — iCal poller tenant-scoping + outbound rate limit | ✅ | `82593af` → `a8da566` | infra only; first live exercise in OPS.M.7's onboarding flow |
| | Slice OPS.M.7 — Tenant Admin onboarding wizard | ✅ | `2f0786d` → `c05042b` | needs Stripe Key Vault URLs updated + Container App restart; welcome email is operator-manual until Slice 4 ships |
| | Slice OPS.M.8 — Super Admin console | ✅ | `998bba4` → `3b89ac0` | promotion is manual SQL until cmdlet ships; tenant-suspended enforcement deferred to Slice OPS.M.8.1 |
| | Slice OPS.M.9 — RLS policies + `IRlsBypassDbContextFactory<TContext>` + bypass connection factory | ✅ | `d591afb` → `e826e3b` | per-statement binding locked; Postgres-fixture schema facts deferred to OPS.M.10's test pack |
| | Slice OPS.M.10 — Cross-tenant isolation test pack | ✅ | `1ec66de` → `f0faccc` | Wave 1 (arch + schema facts + runbook) + Wave 2 (TwoTenantApiFixture + matrix + carve-out + bypass + audit + promote-revoke + AsyncLocal + JWT smoke) all shipped; 1 real cross-tenant leak in `SearchUsersHandler` documented as Slice OPS.M.10.1 follow-up |
| | Slice OPS.M.13 — Email-canonical users + tenant picker + X-Active-Tenant pipeline | ✅ | `b5d2d2d` → `125d456` | staging; close-out at [`OPS_M_13_CLOSE_OUT.md`](OPS_M_13_CLOSE_OUT.md) |
| | Slice OPS.M.14 — DevAuth retirement (handler + endpoints + test fixtures) | ✅ | `ff79389` → `c0654c7` | staging; close-out at [`OPS_M_14_CLOSE_OUT.md`](OPS_M_14_CLOSE_OUT.md) |
| | Slice OPS.M.15 — App-role legacy claim reads + `[Authorize(Roles=)]` drop | ✅ | `58a1fe5` → M.15.7 | staging; plan at [`OPS_M_15_APP_ROLES_CLEANUP_PLAN.md`](OPS_M_15_APP_ROLES_CLEANUP_PLAN.md); close-out at [`OPS_M_15_CLOSE_OUT.md`](OPS_M_15_CLOSE_OUT.md); ADR-0014 amendment 2026-07-06 documents survival of App Role definitions + tenant_memberships shape; follow-up A shipped as Slice OPS.M.21 (row below) |
| | Slice OPS.M.21 — M.15 App Roles cleanup follow-up A (drop `identity.users.is_owner/is_admin` columns + `UserDto` fields + `User.Grant*/Revoke*` domain methods) | ✅ | `09d12a2` → M.21.A.3 | staging; SPA nav derivation reshaped to key on `useMyTenants().memberships` + `isPlatformAdmin`; ADR-0014 amendment #2 marks the finalization; migration `20260706225458_OpsM21_Users_DropOwnerAdminColumns` is forward-only, rollback via [`OPS_M_15_APP_ROLES_CLEANUP_FOLLOWUP_ROLLBACK.md`](OPS_M_15_APP_ROLES_CLEANUP_FOLLOWUP_ROLLBACK.md) |
| | Slice OPS.M.16 — Turnover-aware completion + configurable turnover window | ✅ | `ff96e48` → M.16.7 | staging; close-out at [`OPS_M_16_CLOSE_OUT.md`](OPS_M_16_CLOSE_OUT.md); calendar UI overlay + integration test pack + runbook deferred to polish slice |
| | Slice OPS.M.12 — Social IdPs (Google + Microsoft consumer + Facebook + Apple) via Entra GuestSignUpSignIn + admin-vs-social split at Layer 1 (REFUSE-AT-PROVISIONING) + Layer 2 (middleware belt) + SPA per-flow authority + admin-guard companion + rejection error page | ✅ | `fbd3039` → M.12.8 | staging; close-out at [`OPS_M_12_CLOSE_OUT.md`](OPS_M_12_CLOSE_OUT.md); plan at [`OPS_M_12_SOCIAL_IDPS_PLAN.md`](OPS_M_12_SOCIAL_IDPS_PLAN.md); runbook at [`runbooks/social_idp_setup.md`](runbooks/social_idp_setup.md); ADR at [`adr/0016-admin-vs-social-idp-surface-split.md`](adr/0016-admin-vs-social-idp-surface-split.md); actual IdP portal setup deferred to operator (runbook §3–§6); legacy `NEXT_PUBLIC_ENTRA_AUTHORITY` fallback removed |
| | Slice OPS.INFRA.1 — Staging Postgres public-access rebuild (LankaConnect parity) | ✅ | `c1cc693` → `dd3cfc6` | staging; blue/green cutover complete; V2 = psql-vrbook-staging-v2 (public + IP-firewalled); plan + retro at [`OPS_INFRA_1_STAGING_POSTGRES_PUBLIC_REBUILD_PLAN.md`](OPS_INFRA_1_STAGING_POSTGRES_PUBLIC_REBUILD_PLAN.md); A8 (CAE outbound needs own firewall rule) + A10 (manual `az containerapp update` inherits placeholder image) added as retro lessons |
| **Phase 1.5 ops** | Slice OPS.1 – Slice OPS.8 — Launch hardening (Pact, k6, ZAP, Trivy, key rotation, Entra, DKIM) | ⏭ | [`docs/EXECUTION_PLAN.md`](EXECUTION_PLAN.md) §8 | |
| **Phase 3** | Slice 8 (P3.1) — Hotel-style rooms within a Property/Facility (purely additive: `rooms` child of `properties`, `BookingLineItem.ReservableKind=Room`) | ⏭ after Slice OPS.1–8 | plan: TBD — see [PHASE_3_RECONNAISSANCE](PHASE_3_RECONNAISSANCE.md) for the architect's verdict | |
| | Slice 9 (P3.2) — Multi-unit cart (one guest books N properties/rooms in one atomic checkout; Stripe `transfer_data[]` multi-destination) | ⏭ after Slice 8 | plan: TBD | |
| **Phase 4** | Slice 10 (P4.1) — OTA package bundling (Itinerary with Flights + Cars + Activities + Stays; supplier-tenant polymorphism; cross-tenant RLS + FX) | ⏭ after Slice 9 | plan: TBD — promoted from Phase 3 to Phase 4 per architect (different shape entirely) | |

---

## 2. End-to-end sequence and timeline

**Order locked 2026-06-27 — Option A per architect re-evaluation** (see [`docs/SEQUENCING_RE_EVALUATION_2026_06_27.md`](SEQUENCING_RE_EVALUATION_2026_06_27.md)). The morning Option C verdict was withdrawn by the same architect after the Slice 4 plan review surfaced ~1 day of multi-tenancy contract authoring + event payload extensions that properly belong to Slice OPS.M.4/M.7/M.9, not Slice 4. The order is now **Slice OPS.M.0–3 ✅ → Slice OPS.M.4 → M.5 → M.6 → M.7 → M.8 → M.9 → M.10 → Slice 4 → Slice 5 → Slice 6 → Slice 7 → Slice OPS.1–8 → Phase 3 → Phase 4**.

| Order | Item | Est. days | Cumulative | Why this slot |
|---|---|---|---|---|
| 1 | Slice OPS.M.0 ≡ Slice OPS.7 — Entra External ID cutover | 2 | 2 | ✅ **Shipped 2026-06-25** (c2bc4cd … 989104d). Hard prerequisite for Slice OPS.M.2. |
| 2 | Slice OPS.M.1 — Tenant aggregate + memberships | 2 | 4 | ✅ **Shipped 2026-06-26** (b7ae589, 74aaf64, 3ce5f96). See `docs/OPS_M_1_PLAN.md`. |
| 3 | Slice OPS.M.2 — `TenantId` claim wiring + `ICurrentUser` shape | 1.5 | 5.5 | ✅ **Shipped 2026-06-26** (84d6c05, afbfb61, 9d13cb3). DB-wins precedence per ADR-0014. |
| 4 | Slice OPS.M.3 — `tenant_id` column rollout (Wave A/B/C + Step 7) | 4 | 9.5 | ✅ **Shipped 2026-06-27** (`a60e722` → `2a3d2b2`). 9 modules; ~20 NOT NULL columns; cross-schema FKs to `identity.tenants`. |
| **5** | **Slice OPS.M.4 — `TenantAuthorizationBehavior` + event payload extensions + drop per-handler owner checks** | **~2.5d** (1.5d behavior + ~1d events + tenant-id-conscious-write pattern + arch test) | **12** | ⏭ **next**. Per architect re-evaluation: this slot also owns extending `BookingPlaced/Confirmed/Cancelled/Rejected/ConflictDetected` events to carry `Guid TenantId`, and authoring the "every write path sets `tenant_id` consciously" pattern (Roslyn-style arch test). **Phase-3 note**: do NOT pre-shape for cross-tenant scope; rewrite when Phase 4 OTA actually needs it. |
| 6 | Slice OPS.M.5 — Stripe Connect Express | 4 | 16 | Parallel with Slice OPS.M.7. **Phase-3 note**: do NOT add `connect_account_kind` enum; Phase 4 multi-supplier tenants need a relationship (new table), not a kind. |
| 7 | Slice OPS.M.6 — iCal poller tenant-scoping + outbound rate limit | 1 | 17 | Parallel against Slice OPS.M.5. |
| 8 | Slice OPS.M.7 — Tenant Admin onboarding wizard UI + first-property → Stripe link | 3 | 20 | Depends on Slice OPS.M.5. **Welcome email**: operator-manual placeholder until Slice 4 ships; M.7's scope at that point gains the `tenant.welcome` template + `TenantNotificationHandlers` against the now-existing ACS pipeline. |
| 9 | Slice OPS.M.8 — Super Admin console | 4 | 24 | Parallel against Slice OPS.M.7. |
| 10 | Slice OPS.M.9 — RLS policies + `IRlsBypassDbContextFactory<TContext>` + bypass connection factory | 1.5 | 25.5 | Depends on Slice OPS.M.3c. **Phase-3 binding-granularity decision**: choose per-statement `SET LOCAL app.tenant_id` over per-connection so the same factory can serve Phase 4 OTA cross-tenant itinerary reads (architect verdict). M.9 owns the bypass-factory contract authoring (do not author it in Slice 4). |
| 11 | Slice OPS.M.10 — Cross-tenant isolation test pack | 2 | 27.5 | Parallel against Slice OPS.M.5/M.7. |
| **= Phase 1.5 demo-able** | | **~16 critical-path days** + parallelism | **realistic 4–5 calendar weeks 1 engineer; 2.5–3 weeks two engineers** | Multi-tenant SaaS bring-up complete. |
| 12 | Slice 4 — Notifications that actually send | 3 | 30.5 | Now ships at its original 3-day scope (no bypass-factory authoring, no event payload extensions, no arch-test, no `tenant.welcome` template — all re-attributed to Slice OPS.M.4/M.7/M.9 per architect re-evaluation). Re-review `docs/SLICE4_PLAN.md` against the post-M.10 world before starting. |
| 13 | Slice 5 — Review + loyalty | 2 | 32.5 | Ship `Review` with composite `(BookingId, PropertyId)` key — saves a future migration when Slice 9 (P3.2) multi-unit cart lands (architect verdict). |
| 14 | Slice 6 — Chat + pricing rules | 3 | 35.5 | Daily-driver fit-and-finish. |
| 15 | Slice 7 — Reports + realtime | 2 | 37.5 | Operator polish; SignalR Serverless provisioning. |
| **= Phase 1 demo-able** | | **37.5** | | All seven slices' acceptance criteria met, on top of multi-tenancy. |
| 16 | Slice OPS.1 — Pact contract tests | — | | Launch hardening starts. |
| 17 | Slice OPS.2 — Playwright E2E suite (F1.1) | — | | |
| 18 | Slice OPS.3 — k6 load test (50 RPS, 5 min, P95 < 1s) | — | | |
| 19 | Slice OPS.4 — OWASP ZAP baseline in CI | — | | |
| 20 | Slice OPS.5 — Trivy + SBOM signing | — | | |
| 21 | Slice OPS.6 — Stripe key rotation | — | | |
| 22 | Slice OPS.8 — Custom domain DKIM/SPF for ACS email (per-tenant subdomains deferred to Phase 2/3) | — | | Per MULTI_TENANCY_OPS_PLAN §8 / EXECUTION_PLAN §8.A note. |
| **= Phase 1 launch ready** | | | | |
| 23 | Slice 8 (P3.1) — Hotel-style rooms | TBD | | Architect-recommended Phase 3 start. Purely additive (`rooms` child of `properties`, `BookingLineItem.ReservableKind=Room`). Validates the line-item refactor on one module. |
| 24 | Slice 9 (P3.2) — Multi-unit cart | TBD | | Same-tenant, multi-property/room. Uses Stripe `transfer_data[]` native multi-destination. |
| **= Phase 3 demo-able** | | | | Booking.com-style multi-unit + per-room inventory. |
| 25 | Slice 10 (P4.1) — OTA package bundling | TBD | | Supplier-tenant polymorphism, cross-tenant RLS (the per-statement binding granularity from Slice OPS.M.9 pays off here), FX, new domains (Flight/Car/Activity), Itinerary aggregate. Sits entirely on top of Phase 3's line-item refactor. |
| **= Phase 4 demo-able** | | | | Expedia/tour-operator style cross-supplier itineraries. |

`Slice OPS.7` does NOT appear in the OPS launch row because it lands inside the Slice OPS.M.0 slot as a hard prerequisite for Slice OPS.M.2.

**Order rationale (2026-06-27, third revision today).** Multi-tenancy lands first because the launch hardening's blast radius (Pact contracts, k6 load shape, ZAP attack surface, key rotation policy) all change once tenants exist. Slice 4 + Slice 5–7 wait until after Slice OPS.M.10 so they ship against a fully-shipped multi-tenancy gate — their handlers do not author per-handler tenancy boilerplate that Slice OPS.M.4 would delete, do not author the `IRlsBypassDbContextFactory<TContext>` contract that Slice OPS.M.9 owns, do not bump event payloads that Slice OPS.M.4 will bump, and do not invent a `tenant.welcome` template that Slice OPS.M.7 will own once the ACS pipeline exists. The morning Option C verdict (interleave Slice 4 before Slice OPS.M.4) was withdrawn by the architect once the Slice 4 plan review surfaced ~1 day of multi-tenancy infrastructure that was being attributed to Slice 4 incorrectly. Slice 8 + 9 (Phase 3) + Slice 10 (Phase 4) sit after launch hardening so they don't disturb a launched product.

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

### Phase 1.5 done (Slice OPS.M.0 – Slice OPS.M.10)

Two distinct tenants exist. Tenant A and Tenant B each have their own properties, bookings, Stripe Connect account, iCal feeds, and audit log. Cross-tenant isolation is enforced at app + RLS layers and proven by the Slice OPS.M.10 isolation test pack. Super Admin console can list/suspend/impersonate any tenant. Entra External ID has replaced DevAuth in production.

### Phase 1 launch ready (Slice OPS.1–8 less Slice OPS.7)

CI gates: Pact, Playwright, k6, ZAP, Trivy, SBOM. Stripe keys rotated. ACS DKIM verified for the platform sender. No remaining `TODO: production`. Runbooks current.

### Phase 3 done (Slice 8 + Slice 9)

A facility (formerly Property) can hold N bookable rooms or units. One guest can book multiple rooms or properties in a single atomic checkout with a single Stripe PaymentIntent that splits across the right Connect accounts. Reviews work at room granularity. iCal export works per-room. Pricing rules can target the room or the facility. Existing single-property listings continue to work unchanged via "facility with one room".

### Phase 4 done (Slice 10)

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
8. **DevAuth covers Phase 1 demo and pilot.** Real Entra cutover lands inside Phase 1.5 (Slice OPS.M.0 ≡ Slice OPS.7).
9. **Architect consult required** for any multi-module or sequencing decision. Commit the result as a doc under `docs/` before writing code.

---

## 5. Where each plan lives

| Doc | Owns |
|---|---|
| `docs/REPLAN.md` | The Slice 0–7 contract (active sequencing primitive). §10 sketches the OPS gate; §10.1 codifies the `tenant_id` forward-compat policy. |
| `docs/SLICE4_PLAN.md` | Slice 4 detail (commit split, six load-bearing decisions, scope-cut order). |
| `docs/MULTI_TENANCY_OPS_PLAN.md` | OPS.M.1–10 detail (tenancy model, Stripe Connect, RLS, isolation test pack, pushback items). |
| `docs/OPS_M_0_PLAN.md` / `OPS_M_1_PLAN.md` / `OPS_M_2_PLAN.md` / `OPS_M_3_PLAN.md` | Per-slot detailed plans for the shipped OPS.M items. |
| `docs/SEQUENCING_OPS_M_VS_SLICES.md` | 2026-06-27 morning architect verdict on Option C interleave (Slice 4 → OPS.M.4–10 → Slice 5–7). **Superseded** by `SEQUENCING_RE_EVALUATION_2026_06_27.md`. Kept for the decision trail. |
| `docs/SEQUENCING_RE_EVALUATION_2026_06_27.md` | 2026-06-27 evening architect re-evaluation. Withdraws Option C; user picked Option A (finish Slice OPS.M.4 → M.10 fully, then Slice 4). |
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
- 2026-06-27 — **Sequence reorder + Phase 3 + Phase 4 added**. (1) Mark Slice OPS.M.0–3 ✅ shipped 2026-06-25 → 2026-06-27. (2) Lock Option C order per architect: Slice 4 → Slice OPS.M.4–10 → Slice 5–7 → Phase 3 → Phase 4 → Slice OPS.1–8. (3) Define Phase 3 (Slice 8 hotel-style rooms + Slice 9 multi-unit cart) and Phase 4 (Slice 10 OTA package bundling). (4) Carry forward two narrow door-opening notes from `PHASE_3_RECONNAISSANCE.md`: per-statement `app.tenant_id` binding in Slice OPS.M.9, and composite `(BookingId, PropertyId)` Review key in Slice 5. Three explicit "do NOT pre-shape" instructions: Slice OPS.M.4 signature flex, Slice OPS.M.5 `connect_account_kind` enum, and Slice OPS.M.3 retrofit for rooms (rooms denormalize tenant_id from parent property — no retrofit needed). All other Phase 3 design deferred until after market validation.
- 2026-06-27 — **Unified naming consolidation**. Single `Slice` prefix locked across §1 + §2 + §3 + §4: original product slices keep their integers (Slice 0 … Slice 7); multi-tenancy items become Slice OPS.M.0 … Slice OPS.M.10; launch hardening becomes Slice OPS.1 … Slice OPS.8; Phase 3 items continue the integer sequence as Slice 8 (P3.1) + Slice 9 (P3.2); Phase 4 is Slice 10 (P4.1). Detail-plan filenames (`OPS_M_3_PLAN.md` etc.) and existing commit messages are immutable history and are NOT retroactively renamed.
- 2026-06-27 (evening) — **Sequencing re-evaluated → Option A locked.** The morning's Option C verdict (Slice 4 next, interleaved before Slice OPS.M.4) was withdrawn by the architect after the Slice 4 plan review surfaced ~1 day of multi-tenancy infrastructure — the `IRlsBypassDbContextFactory<TContext>` contract, the `tenant_id`-conscious-write arch test, five booking-event payload extensions to carry `Guid TenantId`, the `tenant.welcome` template + `TenantNotificationHandlers` — that properly belong to Slice OPS.M.4 / Slice OPS.M.7 / Slice OPS.M.9 rather than Slice 4. The user picked Option A (finish Slice OPS.M.4 → M.10 fully, then Slice 4 at its original 3-day scope). §1 + §2 reordered: Slice 4 moves from slot 5 to slot 12; Slice OPS.M.4 becomes slot 5 next; Slice OPS.M.4's scope explicitly expanded to own the event payload extensions and the tenant-id-conscious-write pattern. M.7's scope explicitly notes welcome email is operator-manual until Slice 4 ships, then M.7 gains the `tenant.welcome` template. M.9 explicitly owns the `IRlsBypassDbContextFactory<TContext>` contract authoring. See `docs/SEQUENCING_RE_EVALUATION_2026_06_27.md` for the full architect re-evaluation. The morning's `SEQUENCING_OPS_M_VS_SLICES.md` is superseded but kept for the decision trail.
- 2026-06-27 — **Slice OPS.M.5 shipped.** Stripe Connect Express end-to-end across 8 commits (`6c27d82` → `2d39a3a`). Step 1 (schema), Step 2 + 7 (tenant readiness state machine + 4 payment events bumped with `Guid TenantId`), Step 3 + 4 (helpers + `ITenantStripeContextLookup`), Step 5 (4 onboarding commands + `TenantsAdminController` + `IStripeConnectGateway`), Step 6a-6e (Connect-aware PI / refund / webhook handler rewrites + `NegativeBalanceRefundException` + `StripeGatewayBoundaryTests` arch enforcement). 330/330 `Category=Unit` pass; 33/33 architecture tests pass. Six documented deviations from the rev-2 plan; see `docs/OPS_M_5_PLAN.md` §11. Slice OPS.M.6 (iCal poller tenant-scoping) is next.
- 2026-06-27 — **Slice OPS.M.6 shipped.** iCal poller tenant-scoping + per-host outbound rate limit across 4 commits (`82593af` plan → `e152f03` Wave 1 → `3140b0a` Wave 2 → `a8da566` Wave 3). `IBackgroundCommand` marker + `BackgroundCommandTenantScopeBehavior` (Sync) close the foreground-only gap in `TenantAuthorizationBehavior`. `RunSyncForFeedCommand` carries `Guid TenantId` and implements both markers; the worker stamps `feed.TenantId`. Three Sync events bumped with leading positional `Guid TenantId` (`ExternalReservationImported`, `ExternalReservationCancelled`, `SyncRunFailed`); 4 raise sites updated. `ResolveConflictCommand` gap (no tenant gate) closed via `ITenantScoped` + `CallerTenantId()` + row-level check. Worker hardened: `Console.CancelKeyPress` → `CancellationTokenSource`, `LogContext.PushProperty` for `tenant_id` + `channel_feed_id`. `Sync.Infrastructure.RateLimiting` ships `IRateLimiter` + `InMemoryHostRateLimiter` (BCL `TokenBucketRateLimiter`, per-host buckets via `HostMatcher`) + `ChannelPollOptions` (Airbnb 60/min, Booking/VRBO 30/min, wildcard 20/min) + `OutboundRateLimitHandler` (DelegatingHandler on the `AirBnBICal` named client). 352/352 `Category=Unit` + 42/42 architecture tests pass. Seven documented deviations in `docs/OPS_M_6_PLAN.md` §11. Slice OPS.M.7 (Tenant Admin onboarding wizard UI) is next.
- 2026-06-28 — **Slice OPS.M.10 Wave 2 shipped (full Wave 1+2 complete).** Per user pushback on the initial Wave 2 deferral, executed the full Wave 2 (`f0faccc`) covering Steps 1-3 + 5-8 + 10: `TwoTenantApiFixture` (Postgres testcontainer + all 10 module migrations + seed-via-bypass for TenantA + TenantB + OwnerA + OwnerB + PlatformAdmin user + one Property per tenant), `TwoTenantDevAuthHandler` (test-only `AuthenticationHandler` mapping cookie to one of three personas, replaces production `DevAuthHandler` via `ConfigureTestServices`), `RouteMatrix` (~30 cells covering M.7 + M.5 + M.8 endpoints × OwnerA/OwnerB/PlatformAdmin/Anonymous + cross-target combinations), `CrossTenantEndpointMatrix` single `[Theory]` with `[MemberData]`, `CarveOutAppLayerTests` (outbound iCal token + tenants list + me-tenant filter + **discovered + documented a real cross-tenant leak in `SearchUsersHandler`** with `[Fact(Skip="..."]` pinning), `PlatformAdminBypassFactPack` (5 wire-level facts), `CrossTenantRejectionAuditFactPack` (2 facts verifying M.4 `AuditLogBehavior` records `.failed` rows for `CrossTenantAccessException`), `PlatformAdminPromoteRevokeSmokeTest` (end-to-end SQL-flip-and-re-request smoke per the M.8 runbook), `AsyncLocalLeakFactPack` (5 unit-level facts pinning `RlsBypassScope` + `BackgroundTenantScope` AsyncLocal semantics across awaits, parallel tasks, and exception paths), `JwtSmokeTests` (anonymous + invalid-bearer facts; production-shape JWT scaffold `[Skip]`'d behind env vars). 386/386 unit + 57/57 arch + ~50 CrossTenant matrix facts (Postgres + Docker gated) pass. Six deviations documented in `docs/OPS_M_10_PLAN.md` §11 — notably: matrix cell count is ~30 (not the plan's ~200; Property/Booking/Pricing endpoints deferred to incremental adds), Serilog InMemorySink for bypass log-content deferred to Slice OPS.M.10.1, JWT signing-key fixture deferred to OPS.M.10.1, `AcceptedStatuses` array shape rather than single value. **Critical finding**: `SearchUsersHandler` doesn't filter by tenant_memberships → real cross-tenant user enumeration leak. Tracked as Slice OPS.M.10.1 + `[Fact(Skip="..."]` pins the gap for visibility. Multi-tenancy rollout (OPS.M.0 → OPS.M.10) complete. Slice 4 (Notifications) is next per Option A.
- 2026-06-28 — **Slice OPS.M.10 Wave 1 shipped.** Cross-tenant isolation foundational facts across 2 commits (`1ec66de` plan → `a1164b4` Wave 1). `EndpointCoverageArchTest` enforces every controller action declares an explicit access decision (`[Authorize]` / `[AllowAnonymous]` / `[ExemptFromCrossTenantMatrix("reason")]`); 3 arch facts shipped — drift-detector for any future controller addition. `ExemptFromCrossTenantMatrixAttribute` in `src/VrBook.Api/Common/` carries a non-empty `Reason` string + class-or-method target. `RlsPolicySchemaFactPack` (76 facts via `[Theory]` × `[MemberData]`) introspects every M.9-protected table for `relrowsecurity`, `relforcerowsecurity`, expected policy name, and policy qual referencing both `app.tenant_id` + `app.is_platform_admin` GUCs; nullable-tenant tables also assert the `IS NULL` branch. `RlsCarveOutSchemaFactPack` (13 facts) asserts the §3.2 carve-out tables (users/tenants/tenant_memberships/9x outbox_messages/amenities) do NOT have RLS enabled. Both schema fact packs gate on `VRBOOK_TEST_POSTGRES_CONN` env var. `docs/runbooks/OPS_M_10_CROSS_TENANT_LEAK_TRIAGE.md` covers 8 failure modes (A-H) with triage steps and PR closure checklist. 384/384 unit + 57/57 architecture tests pass. **Wave 2 deferred** (`TwoTenantApiFixture` + `RouteMatrix` + ~200-fact `CrossTenantEndpointMatrix` + audit/bypass/promote-revoke/AsyncLocal-leak fact packs + JWT smoke fixture) — ~14-16h of focused implementation work that merits its own slice rather than bundling here. The high-value invariants ship immediately. Slice 4 (Notifications) is next per Option A; Wave 2 is optional but valuable before that.
- 2026-06-28 — **Slice OPS.M.9 shipped.** Row-Level Security across 19 tenant-scoped tables + the `IRlsBypassDbContextFactory<TContext>` defense-in-depth layer across 6 commits (`d591afb` plan → `b22bad6` Wave 1 foundation → `f9b5ae7` per-module DI → `aee6864` 9 RLS migrations + migrator BYPASSRLS → `38be22e` Steps 7-10 cross-tenant reader rewires → `e826e3b` arch test + diagnose runbook). Per-statement `SET LOCAL app.tenant_id` + `app.is_platform_admin` GUC stamping via `TenantGucCommandInterceptor` (`DbCommandInterceptor` on every Reader/NonQuery/Scalar path). Resolution order: `ICurrentUser.TenantId` > `BackgroundTenantScope.CurrentTenantId` > empty string (fail-safe). `RlsBypassScope` + `BackgroundTenantScope` AsyncLocal stacks; `BypassedDbContext<T>` wrapper composes inner-DbContext + scope disposal. `RlsServiceCollectionExtensions.AddTenantScopedDbContext<T>()` single-call helper replaces the 5-line `AddDbContext` boilerplate in all 10 modules. 9 `OpsM9_<Module>_RlsPolicies.cs` migrations applied via `EnableRlsTenantIsolation(schema, table, nullable=false)` helper emitting the §3.4 template (USING + WITH CHECK with `NULLIF(current_setting('app.tenant_id', true), '')::uuid` + bypass branch). Identity migration embeds the `vrbook_migrator` BYPASSRLS grant in a `DO $$` block. Cross-tenant readers rewired: `TenantStripeContextLookup` (both methods) + `ListPlatformTenantsHandler` + `GetPlatformTenantHandler` use `IRlsBypassDbContextFactory<IdentityDbContext>`; `HandleStripeWebhookHandler` + `PlatformTenantStatsLookup` use `RlsBypassScope.Enter()` directly (lets repo + cross-module IPropertyCountByTenant pick up AsyncLocal flag); Sync worker bootstrap wraps `feeds.ListDueForPollAsync` in the bypass factory. M.6 `BackgroundCommandTenantScopeBehavior` now also pushes `BackgroundTenantScope.Enter(scoped.TenantId)` so worker DbContext commands stamp the correct tenant. `RlsBypassCallSiteAllowlistTests` arch enforcement pins the 3-class injection allow-list (TenantStripeContextLookup + List/GetPlatformTenantHandler) + the BypassedDbContext contract shape. `docs/runbooks/OPS_M_9_RLS_DIAGNOSE.md` documents the 7-step "I can't see my data" decision tree. 381/381 `Category=Unit` + 54/54 architecture tests pass. 10 documented deviations in `docs/OPS_M_9_PLAN.md` §13 — notably: contract moved from Contracts to Infrastructure (EF Core dep), `BypassedDbContext<T>` wrapper replaces planned per-module sealed subclass, generic factory impl replaces 9 sealed subclasses, single-call `AddTenantScopedDbContext` helper, webhook + stats handlers use scope directly instead of factory, Step 11 + 11.5 + Step 13's Postgres-fixture schema facts deferred to OPS.M.10's test pack. Slice OPS.M.10 (cross-tenant isolation test pack) is next.
- 2026-06-28 — **Slice OPS.M.8 shipped.** Super Admin console across 4 commits (`998bba4` plan → `1d19aca` Wave 1+2 → `a2e571c` Wave 3 backend → `3b89ac0` Wave 3 frontend). `identity.users.is_platform_admin` DB column (NOT NULL, default false, partial index `WHERE = true`); `User.GrantPlatformAdmin`/`RevokePlatformAdmin` + `UserPlatformAdminGranted`/`Revoked` events. `ICurrentUser.IsPlatformAdmin` materialized by `UserProvisioningMiddleware` from the DB (DB-wins per ADR-0014); `HttpContext.Items` + `ClaimTypes.Role = "PlatformAdmin"` so `[Authorize(Roles="PlatformAdmin")]` just works. `TenantAuthorizationBehavior` bypass swap (was `return false`; now reads `user.IsPlatformAdmin`). Two new commands `SuspendTenantCommand` + `ReactivateTenantCommand` (NOT `ITenantScoped` — platform-scoped writes against a target) + `SetTenantPlatformFeeBpsCommand` lit-up with defense-in-depth `ForbiddenException` check. New `TenantsPlatformController` at `/api/v1/admin/platform/tenants` (list paged with status+search filters, detail, suspend, reactivate, platform-fee). `IPlatformTenantStatsLookup` cross-module read (delegates to OPS.M.7 `IPropertyCountByTenant`; booking + revenue zeroed pending Phase-2 swap). `UserDto.IsPlatformAdmin` field bump so the web client gates the Platform nav group without a second round trip. Web platform list + detail pages with Suspend modal (reason input), Reactivate button, inline Set fee form; AdminSidebar conditional Platform group + tenant-suspended banner. Operator runbook for promote/revoke (manual SQL with three-named-humans audit policy). `PlatformAdminEndpointRoleGateTests` arch enforcement (5 facts pinning the role attribute, route prefix, no method-level `Authorize`, no `AllowAnonymous`). 370/370 `Category=Unit` + 52/52 architecture tests + 28/28 web vitest pass. Eight documented deviations in `docs/OPS_M_8_PLAN.md` §11 — notably: Step 9 DevAuth seed migration deferred (operator runs manual SQL), Step 11 PowerShell cmdlet deferred (runbook covers it), Step 13 four-persona integration matrix deferred to OPS.M.10's cross-tenant sweep, booking+revenue stats zeroed pending Phase-2 contract. Slice OPS.M.9 (RLS policies + `IRlsBypassDbContextFactory`) is next.
- 2026-06-27 — **Slice OPS.M.7 shipped.** Tenant Admin onboarding wizard UI across 3 commits (`2f0786d` plan → `f2ac340` Wave 1 backend → `c05042b` Wave 2 frontend). Wave 1: `MeTenantDto` + `OnboardingProgressDto` (`VrBook.Contracts.Dtos.Identity`), `OnboardingProgress` static derivation (8-branch state machine), `IPropertyCountByTenant` cross-module read (contract in Contracts, impl in Catalog), `GetMyTenantQuery` + handler (no `ITenantScoped` — derives tenant id from `ICurrentUser`), `GET /api/v1/me/tenant` endpoint with `Cache-Control: no-store`. Wave 2: web client `tenant.ts` (typed wrappers for the OPS.M.5 endpoints + the new tenant read), `useMyTenant()` hook (react-query with polling, `pollMax = 30`, `stopWhen` predicate, `isExhausted` getter), `useStripeOnboardingFlow()` hook (two-call orchestration + redirector), `WizardCard` primitive (Tailwind + Lucide), `/admin/onboarding` wizard page (three-step layout, status-driven banners), `/admin/onboarding/complete` (1Hz × 30 polling with "Refresh now" fallback), `/admin/onboarding/refresh` (regenerates Stripe AccountLink + bounces back), AdminSidebar "Continue setup" link, operator-manual welcome-email runbook with Slice 4 swap instructions. 370/370 `Category=Unit` + 47/47 architecture tests + 23/23 web vitest pass. Eight documented deviations in `docs/OPS_M_7_PLAN.md` §11 — notably: Step 9 Playwright e2e deferred (needs running Stripe sandbox), dashboard-level redirect gate replaced by sidebar link (less aggressive UX, same discoverability), behavior facts on `GetMyTenantHandler` success path moved to CI Postgres run (sealed `IdentityDbContext` blocks mocking). Operator action pending: update Stripe Key Vault `OnboardingReturnUrl`/`OnboardingRefreshUrl` + Container App restart. Slice OPS.M.8 (Super Admin console) is next.
