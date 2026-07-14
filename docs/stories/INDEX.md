# VrBook — User Story Backlog (INDEX)

**86 stories across 6 epics** (85 feature/launch + **VRB-300**, the Wave-0 API contract suite). Every story is TDD-first and follows the full template (narrative · Given/When/Then AC · TDD plan · technical notes · UI/UX · configuration · rollout · observability · DoD · dependencies · parallelisation). Grounded in the code with cited paths, and in the locked decisions ([`../../OPEN-QUESTIONS.md`](../../OPEN-QUESTIONS.md)), the PRD ([`../product/PRD.md`](../product/PRD.md)), and the **corrected** Phase 3/4 design ([`../architecture/PHASE-3-4-DESIGN.md`](../architecture/PHASE-3-4-DESIGN.md) §0.5).

> **Story STATE lives in [`BOARD.md`](BOARD.md), not here.** This INDEX is the static catalogue (what each story *is* + how they relate); the board is the live `TODO`/`CLAIMED`/`DONE` tracker + the claim protocol. **New stories:** copy [`_STORY-TEMPLATE.md`](_STORY-TEMPLATE.md) — it embeds the DoR/DoD checklist inline so the rules travel with the story. **A fresh agent:** bootstrap per [`../AGENT-PLAYBOOK.md`](../AGENT-PLAYBOOK.md), **claim** a story on the board, read it + its `blocked-by` here → write the failing tests its TDD plan names → implement → satisfy the DoD (the global DoR/DoD in [`../ENGINEERING-RULES.md`](../ENGINEERING-RULES.md) + the story's own). Respect the Dependencies + Parallelisation fields (lanes own non-overlapping files; CODEOWNERS enforces it).

## Epics & ID ranges

| Epic | File | IDs | Count | When |
|---|---|---|---|---|
| Launch Features | [`EPIC-launch-features.md`](EPIC-launch-features.md) | VRB-101–113 | 13 | **Launch** (10 Must, 3 Should) |
| Configuration & Settings | [`EPIC-configuration-settings.md`](EPIC-configuration-settings.md) | VRB-200–220 | 21 | **Launch** (platform prereqs early) |
| Staging→Production Go-Live | [`EPIC-go-live.md`](EPIC-go-live.md) | VRB-300–313 | 14 | **Launch** (VRB-300 API suite = Wave 0) |
| Phase 3 — Foundation & Rooms | [`EPIC-phase3-foundation-rooms.md`](EPIC-phase3-foundation-rooms.md) | VRB-400–412 | 13 | Post-launch (Could) |
| Phase 3 — Multi-unit & Cross-business Cart | [`EPIC-phase3-cart.md`](EPIC-phase3-cart.md) | VRB-420–431 | 12 | Post-launch (Could) |
| Phase 4 — OTA Bundling | [`EPIC-phase4-ota.md`](EPIC-phase4-ota.md) | VRB-500–512 | 13 | Post-launch (Could) |

Companion ops docs: [`../ops/CONFIG-MATRIX.md`](../ops/CONFIG-MATRIX.md) (every key/secret per env), [`../ops/GO-LIVE-RUNBOOK.md`](../ops/GO-LIVE-RUNBOOK.md) (executable cutover), [`../ops/CURRENT-GAPS.md`](../ops/CURRENT-GAPS.md) (defect register).

---

## Gap → Story traceability (owner directive: every P0/P1 gap is its own story)

| Gap | Sev | Story(ies) |
|---|---|---|
| G1 loyalty thresholds hard-coded (config dead) | P1 | **VRB-206** |
| G2 booking SLA hard-coded 24h; locked 48h | P1 | **VRB-207** (launch config) → **VRB-403** (carried into Reservation reshape) |
| G3/G4 dead/mismatched Sync + hold config | P2 | **VRB-208** |
| G5 no fail-fast config validation (silent no-auth risk) | **P0** | **VRB-200** |
| G6 `stripe-publishable-key` + `acs-sender-address` not seeded | P1 | **VRB-201** |
| G7 `EntraExternalId:AdminFlowName` read but never provided | P2 | **VRB-209** |
| G8 hard-coded staging API FQDN in web build-arg | P2 | **VRB-202 / VRB-205** |
| G9 outbox→Service Bus relay unimplemented | P1 | **DEFERRED** (in-process OK at launch scale per Q22; design §10-Q4 re-evaluates when the cart lands — not a launch story) |
| G10 property image upload (501) | P1 | **VRB-101** |
| G13 feature-flag runtime no-op | P2 | **VRB-203** |
| G17 home page placeholder data | P1 | **VRB-107** |
| G18 `/account/profile` stub | P1 | **VRB-108** |
| G19 no mobile nav | P1 | **VRB-106** |
| G23 no prod deploy pipeline | **P0** | **VRB-301** |
| G24 no tested rollback | **P0** | **VRB-302** |
| G25 backup/restore untested | P1 | **VRB-304** |
| G26 sandbox-only integrations | P1 | **VRB-204 + VRB-309** |
| G27 informational-only quality gates | P1 | **VRB-307 / VRB-308** |
| G28 in-memory (non-distributed) rate limiter | P2 | **VRB-205** |
| G32 no cookie consent / GDPR-CCPA / legal surfaces | P1 | **VRB-311** |
| G33 no WCAG audit | P1 | **VRB-110** |
| G34 SEO/i18n gaps | P1 | **VRB-109 + VRB-219** |
| G35 no analytics/conversion tracking | P1 | **VRB-311** |
| G36 no availability target / RTO / RPO / on-call | P2 | **VRB-304 + VRB-306 + VRB-313** |
| G37 application-fee reversal is a no-op | P1 | **VRB-104** |
| G38 single-tenant `OnBehalfOf` makes supplier the MoR | P1 | **VRB-105** |

All P0/P1 gaps are covered; the only deferral (G9) is an explicit owner decision (Q22), not an omission.

## Design-correction → Story traceability (Phase 3/4 review §0.5)

| Correction | Story |
|---|---|
| C1 multi-supplier capture (resolve→single partial capture→transfer) | VRB-425 |
| C2 PaymentIntent is a 2nd cross-tenant object → platform scope | VRB-423 |
| C3 RLS per-tenant flush within one serializable txn (not "per statement") | VRB-422 |
| C4 real application-fee reversal | VRB-104 (launch) |
| C5 MoR/tax reconciliation (drop `OnBehalfOf`) | VRB-105 (launch) |
| C6 per-state facilitator tax | VRB-103 + VRB-216 + VRB-429 |
| C7 RatePlan dimension | VRB-407 |
| C8 inventory counter-row `FOR UPDATE` | VRB-408 |
| C9 FX charge-currency + spread-bearer decision | VRB-427 |
| C10 48h SLA everywhere | VRB-207 / VRB-403 |
| C11 mixed-policy cart display + anonymous-cart | VRB-421 + VRB-428 |

---

## Dependency spine (build order)

```
EARLY PREREQUISITES (land first, unblock everything):
  Config:   VRB-200 fail-fast · VRB-201 secrets · VRB-203 flags · VRB-210 settings-UI · VRB-211 audit-trail
  Go-live:  VRB-301 prod-pipeline · VRB-302 rollback · VRB-303 migration · VRB-304 restore-drill · VRB-306 observability

LAUNCH FEATURES (parallel by domain, after their config deps):
  Payments spine (sequenced, shared StripeGateway.cs): VRB-105 → VRB-104 → VRB-103
  VRB-102 cancellation-engine  ← VRB-104, VRB-215/216 (policy config)
  VRB-101 photos · VRB-106 mobile-nav · VRB-107 home · VRB-108 profile · VRB-109 SEO · VRB-110 a11y  (mostly independent)
  Should: VRB-111 instant-book · VRB-112 promos (←102,103) · VRB-113 deposit

LAUNCH-WEEK (operator-gated): VRB-305 DNS · VRB-307 security · VRB-308 load · VRB-309 payments-live ·
  VRB-310 UAT · VRB-311 analytics+legal · VRB-312 cutover · VRB-313 hypercare

PHASE 3 (strictly sequenced spine):
  VRB-400→401→402→403→404 (foundation reshape)  ⟶  VRB-405→406 (rename wave) → 407/408/409 (rooms fan-out) → 410/411 (UI)
    ⟶  VRB-420…431 (cart; VRB-426 also ← VRB-104)
PHASE 4:  VRB-500…512  ← Phase 3 cart complete
```

## Parallelisation (agent lanes)

- **Within each epic**, every story's `Parallelisation` field names its lane + the exact files it owns, so non-overlapping stories run concurrently.
- **Cross-cutting sequenced (NOT parallel):** the Phase-3 foundation reshape (VRB-400–404), the `properties→facilities` rename wave (VRB-405–406), and any story touching `StripeGateway.cs` / `PlaceBookingHandler.cs` / `TenantGucCommandInterceptor.cs` (VRB-104/105, VRB-422/423/424/425, VRB-506) — these share load-bearing files and must be serialized. The full lane map + integration order + file-ownership rules are in [`../plan/EXECUTION-PLAN.md`](../plan/EXECUTION-PLAN.md); the copy-paste per-lane kickoff prompts are in [`../plan/AGENT-PROMPTS.md`](../plan/AGENT-PROMPTS.md).

---

## Reconciliations (flagged by the epic writers — resolved here)

1. **Supplier-relationship table naming.** Code doc-comment `ITenantStripeContextLookup.cs:11-13` names it `tenant_connect_accounts`; the design + VRB-505 use **`identity.tenant_supplier_relationships`**. **Resolution: adopt `tenant_supplier_relationships`** (it models the agency↔supplier *relationship* incl. `agency_margin_bps`, broader than a connect-account map). Update the doc-comment when implementing VRB-505.
2. **SEO is split across three stories by concern — intentional, coordinate:** VRB-109 (the engine: sitemap.xml / robots.txt / canonical / JSON-LD), VRB-219 (per-property SEO metadata *settings*), VRB-305 (custom domain / DNS / TLS at go-live). VRB-109 is blocked-by nothing; VRB-305 consumes it.
3. **Launch payment fixes are reused downstream:** VRB-104 (fee reversal) + VRB-105 (MoR) are launch stories; VRB-309 (payments go-live) verifies them in prod, and VRB-426 (Phase-3 per-item cancel) reuses the corrected fee-reversal path. Single source of truth = VRB-104/105.
4. **Accessibility is cross-cutting:** every UI story carries WCAG 2.2 AA acceptance criteria; **VRB-110** is the dedicated audit + shared focus-trap primitive (blocks the modal-heavy UIs).
5. **48h SLA:** VRB-207 makes the *current* `Booking` SLA configurable (48h) at launch; VRB-403 carries the same value into the reshaped `Reservation` model — VRB-403 supersedes VRB-207's touchpoint in the Phase-3 world (no conflict; sequential).

---

*Next (spec program): Phase 4 — `docs/plan/EXECUTION-PLAN.md` (agent lanes + integration order + git strategy) + `docs/plan/AGENT-PROMPTS.md` (copy-paste kickoff prompts per lane). Phase 5 — consolidate + CLAUDE.md links.*
