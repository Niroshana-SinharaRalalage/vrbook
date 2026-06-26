# VrBook Master Plan — Single Source of Truth

**Last revised**: 2026-06-14.
**Status**: Slices 0–3 shipped + verified. Slice 4 plan approved-pending; OPS.M (Phase 1.5) and OPS launch hardening sequenced below.

This document is the single index. It points at the detailed plans for each phase; do not duplicate content here — update the linked plans and bump the date at the top of this file.

> **One TODO, one tag scheme** (per `docs/EXECUTION_PLAN.md` §1). Slice 0–7 are the Phase-1 demo path; OPS.M.1–10 is the Phase-1.5 multi-tenancy gate; OPS.1–8 is launch hardening. No parallel naming.

---

## 1. Where we are right now

| Phase | Item | Status | Commits | Verified |
|---|---|---|---|---|
| **Phase 1** | Slice 0 — Honest booking lifecycle (race-free hold, FOR UPDATE, manual capture, expiry worker, ACS resource, calendar query) | ✅ | `0060216` → `0dbeed6` | staging |
| | Slice 1 — Owner onboards a property | ✅ | `c4aea3f` → `da6dfb9` | staging |
| | Slice 2 — Guest books, owner confirms (the credibility test) | ✅ | `9896882` → `3fddd45` | staging |
| | Slice 2 polish — DevAuth persona switcher works cross-origin | ✅ | `ca8ffd6` | staging |
| | Slice 3 — Calendar + iCal + owner blocks (with §10.1 forward-compat `tenant_id NULL` on every new table) | ✅ | `f8ffd04` → `c8f8b8b` | staging |
| | **Slice 4 — Notifications that actually send** | ⏭ next | plan: `d9fa889` ([docs/SLICE4_PLAN.md](SLICE4_PLAN.md)) | |
| | Slice 5 — Stay completes → review + loyalty | ⏭ | — | |
| | Slice 6 — Host↔Guest chat + pricing power-user | ⏭ | — | |
| | Slice 7 — Reports + realtime polish | ⏭ | — | |
| **Phase 1.5** | OPS.M.1–10 — Multi-tenancy bring-up | ⏭ Phase 1.5 | plan: `df5580b` ([docs/MULTI_TENANCY_OPS_PLAN.md](MULTI_TENANCY_OPS_PLAN.md)) | |
| **Phase 1.5 ops** | OPS.1–8 — Launch hardening (Pact, k6, ZAP, Trivy, key rotation, Entra, DKIM) | ⏭ | [`docs/EXECUTION_PLAN.md`](EXECUTION_PLAN.md) §8 | |

---

## 2. End-to-end sequence and timeline

| Order | Item | Est. days | Cumulative | Why this slot |
|---|---|---|---|---|
| 1 | Slice 4 — Notifications | 3 | 3 | Email is the moment-of-truth touch for the funnel. Closes the §11/§13 compliance gap left by A9 v1. Forward-compat: any new tables get `tenant_id uuid NULL` per REPLAN §10.1. |
| 2 | Slice 5 — Review + loyalty | 2 | 5 | Closes the post-stay loop; only ~2 templates added to the Slice 4 pipeline. |
| 3 | Slice 6 — Chat + pricing rules | 3 | 8 | Daily-driver fit-and-finish. Adds messaging-side tables (with `tenant_id NULL`). |
| 4 | Slice 7 — Reports + realtime | 2 | 10 | Operator polish; SignalR Serverless provisioning isolated to this slice. |
| **= Phase 1 demo-able** | | **10** | | All seven slices' acceptance criteria met. |
| 5 | OPS.M.0 ≡ OPS.7 — Entra External ID cutover (folded into OPS.M critical path) | 2 | 12 | Hard prerequisite for OPS.M.2. Moves out of OPS.1–8 launch-readiness because OPS.M depends on it. |
| 6 | OPS.M.1 — Tenant aggregate + memberships | 2 | 14 | ✅ **Shipped 2026-06-26** (b7ae589, 74aaf64, 3ce5f96). Aggregates + Slice5 migration + default-tenant seed + ADR-0014. See `docs/OPS_M_1_PLAN.md`. |
| 7 | OPS.M.2 — `TenantId` claim wiring + `ICurrentUser` shape | 1.5 | 15.5 | Depends on OPS.M.1 + Entra. |
| 8 | OPS.M.3 — `tenant_id` column rollout (3a/b/c/d) | 4 | 19.5 | The work. Slice 3–7 tables already have the column nullable; OPS.M.3 backfills + tightens NOT NULL. |
| 9 | OPS.M.4 — `TenantAuthorizationBehavior` + drop per-handler owner checks | 1.5 | 21 | Net code reduction. |
| 10 | OPS.M.5 — Stripe Connect Express | 4 | 25 | Parallel with M.7. |
| 11 | OPS.M.6 — iCal poller tenant-scoping + outbound rate limit | 1 | 26 | Parallel against M.5. |
| 12 | OPS.M.7 — Tenant Admin onboarding wizard UI + first-property → Stripe link | 3 | 29 | Depends on M.5. |
| 13 | OPS.M.8 — Super Admin console | 4 | 33 | Parallel against M.7. |
| 14 | OPS.M.9 — RLS policies + bypass connection factory | 1.5 | 34.5 | Depends on M.3c. |
| 15 | OPS.M.10 — Cross-tenant isolation test pack | 2 | 36.5 | Parallel against M.5/M.7. |
| **= Phase 1.5 demo-able** | | **~16 critical-path days** + parallelism | **realistic 4–5 calendar weeks 1 engineer; 2.5–3 weeks two engineers** | Multi-tenant SaaS bring-up complete. |
| 16 | OPS.1 — Pact contract tests | — | | Launch hardening starts. |
| 17 | OPS.2 — Playwright E2E suite (F1.1) | — | | |
| 18 | OPS.3 — k6 load test (50 RPS, 5 min, P95 < 1s) | — | | |
| 19 | OPS.4 — OWASP ZAP baseline in CI | — | | |
| 20 | OPS.5 — Trivy + SBOM signing | — | | |
| 21 | OPS.6 — Stripe key rotation | — | | |
| 22 | OPS.8 — Custom domain DKIM/SPF for ACS email (per-tenant subdomains deferred to Phase 2) | — | | Per MULTI_TENANCY_OPS_PLAN §8 / EXECUTION_PLAN §8.A note. |
| **= Phase 1 launch ready** | | | | |

`OPS.7` does NOT appear in the OPS launch row because it lands inside the OPS.M.M.0 slot as a hard prerequisite for OPS.M.2.

**Order rationale.** Slices 4–7 complete the Phase-1 product story so the platform can be demoed end-to-end to one tenant (the seeded Owner). Multi-tenancy lands BEFORE launch hardening because the launch hardening's blast radius (Pact contracts, k6 load shape, ZAP attack surface, key rotation policy) all change once tenants exist. Doing OPS.1–8 first would mean redoing them after OPS.M.

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
