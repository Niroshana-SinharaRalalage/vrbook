# Phase 3/4 Design — Independent Architect Review (record)

**Date:** 2026-07-13. **Reviewer:** second, independent platform architect (adversarial red-team of [`PHASE-3-4-DESIGN.md`](PHASE-3-4-DESIGN.md), which a different architect authored). **Inputs:** locked requirements ([`../../OPEN-QUESTIONS.md`](../../OPEN-QUESTIONS.md) R1–3), cited market research ([`../product/COMPETITIVE-RESEARCH.md`](../product/COMPETITIVE-RESEARCH.md)), and ground-truth code.

**Verdict: SHIP-WITH-CORRECTIONS.** The 11 corrections are applied in `PHASE-3-4-DESIGN.md` §0.5. C1–C5 must be reflected before writing §3/§4 stories; C6–C11 are story-level. This doc records the review's reasoning + the code-verified red flags so the evidence trail is complete.

## Section verdicts
- **CONFIRMED:** §1 unifying model, §6 cancellation, §7 sequencing, §8 migration, §9 Phase 4.
- **CONFIRMED-WITH-CORRECTION:** §2 rooms (RatePlan + inventory lock), §4 RLS (EF-batching + PI-scoping), §5 FX (charge currency + spread bearer).
- **REJECTED as written:** §3 manual-capture-across-N (capture-on-first-confirm charges for unapproved legs).

## Code-verified red flags (the reason this review mattered)
1. **R1 — "interceptor re-stamps `app.tenant_id` per statement" is false under EF batching.** The interceptor fires per **DbCommand** (`TenantGucCommandInterceptor.cs:70-101`); EF batches N INSERTs into one command → a mixed-tenant batch stamps one tenant and the other tenant's rows fail `WITH CHECK`. Fix = per-tenant flush within one serializable transaction. (→ design C3)
2. **R2 — the PaymentIntent is a second cross-tenant object.** `payment.payment_intents` is tenant-scoped (`RefundForBookingCommand.cs:65` `pi.TenantId != cmd.TenantId`); an `OrderId`-keyed PI spans M suppliers. "Order is the only cross-tenant object" is false. Fix = PI root to platform/bypass scope. (→ C2)
3. **R3 — capture-on-first-confirm is not workable + violates manual capture.** A Stripe PI captures once; first-confirm capture charges for legs B…N that haven't approved. Fix = resolve all legs by SLA → single partial capture = Σ confirmed → transfer per supplier. (→ C1)
4. **M1 — application-fee reversal is a no-op today.** `StripeGateway.cs:135-144` writes reversal cents to refund metadata only; never calls `ApplicationFeeRefundService`. Platform fee isn't clawed back on refund. **Launch-relevant** (single-tenant refunds). (→ C4, gap G37)
5. **C5 — single-tenant `OnBehalfOf=supplier` makes the supplier the MoR**, contradicting the platform-as-facilitator tax posture. **Launch-relevant.** (→ gap G38)
6. **R5 — count-availability overbooking race** at the inventory boundary (no rows to lock when `COUNT<InventoryCount`). Fix = `FOR UPDATE` on the `booking.room_inventory` counter row. (→ C8)

## Requirement coverage (OPEN-QUESTIONS R1–3)
All satisfied EXCEPT, before corrections: Q1 (design said 24h, locked is **48h** → C10), Q5/Q25 (tax per-state facilitator not modeled → C6), Q6 (manual capture broken for multi-supplier → C1), Q9R (charge-currency/FX-spread unresolved → C9). All now captured.

## Ruling on the research's three refinements
- **(a) RatePlan dimension — ADD** (defect, not simplification; the two cancellation models are literally two rate plans). → C7
- **(b) Per-state facilitator tax — REQUIRED** ("all states" is wrong; collected≠remitted; host obligations). → C6
- **(c) FX incidence — UNRESOLVED, must surface** (charge currency + who bears the ~1% spread). → C9

## MoSCoW correction
Two items reclassified to **launch Must-fix**: real application-fee reversal (C4/G37) and MoR/tax reconciliation (C5/G38) — both are single-tenant-launch concerns the design had implicitly filed under Phase 3.
