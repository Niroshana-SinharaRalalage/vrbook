# Sequencing: OPS.M.4-10 vs Slices 4-7

**Date**: 2026-06-27. Decision pending user. Author: architect consult.

## TL;DR — Recommendation: **Option C (minimum interleave)**

Ship **Slice 4 next** (notifications + `NotBeforeUtc` column + ACS wire), then **OPS.M.4 → M.10**, then **Slices 5-7**.

One sentence: Slice 4 is the only outstanding slice that establishes a *cross-cutting infrastructure seam* (deferred-send column, ACS template pipeline, worker drain pattern) that Slices 5/6/7 all consume — building it after M.9 RLS forces the notifications worker to be authored against the bypass connection factory from day one, which is strictly cheaper than retrofitting; conversely, Slices 5/6/7 are *vertical* product slices whose handlers are simpler if M.4's `TenantAuthorizationBehavior` already exists.

## Risk matrix

### Option A — OPS.M.4-10 first, then all of 4-7 (user's preference)
- **Notifications worker rewrite hazard.** Slice 4's worker drains `notification_log` cross-tenant on a `*/2 min` cron. Under M.9 RLS that's a bypass-connection call — fine if planned, but Slice 4's plan (`SLICE4_PLAN.md` §2.5) is written assuming the default connection. Either Slice 4 is authored aware of M.9 (adds 0.5d) or it ships broken on staging when M.9 lands.
- **Slice 5 deferred-send dependency.** Slice 5's `review.request` T+1 nudge (`SLICE5_PLAN.md` §2.3) explicitly reuses Slice 4's `NotBeforeUtc` column and worker. Without Slice 4, Slice 5 must either ship its own cron (rejected in §2.3) or block on Slice 4. Holding 5 until after M.10 is fine; the coupling is internal.
- **Demo coherence break.** OPS.M.7 ships a tenant-admin onboarding wizard. The first thing a new tenant expects after onboarding is "I got a welcome email." Without Slice 4, the wizard hands off to silence. Demoing multi-tenancy without `bookings@vrbook` mail going out is a soft credibility hit that the user will feel.

### Option B — Slice 4-7 first, pause OPS.M
- **Reports (Slice 7) is the killer.** Slice 7 ships 4 cross-DbContext query handlers (Booking + Sync + Catalog) with date-window aggregations. Per `SLICE7_PLAN.md` §2.4 the ownership filter is *per-handler* via `IPropertyOwnerLookup`. After M.4 lands, all four handlers get rewritten to drop those checks; after M.9 lands they get rewritten *again* to either set `app.tenant_id` or use the bypass factory for cross-property views. Double rework.
- **Slice 6 pricing handlers ship with per-handler tenancy boilerplate.** `SLICE6_PLAN.md` §2.2 adds `AddRule`/`RemoveRule`/`ReorderRules` — each gets its own ownership check today, then loses it when M.4 lands. ~6 handlers × ~6 lines of soon-to-be-deleted code.
- **Stripe Connect blast.** Slice 7 reports include Revenue. Under single-tenant Connect (today) the revenue number is the platform's; under per-tenant Connect (M.5) revenue is partitioned by `tenant_id` with platform fee separation. Building Revenue before M.5 means the SQL projects `bookings.Total` directly; after M.5 it must net out `application_fee_amount` per tenant. That's a real semantics change, not cosmetic.

### Option C — Slice 4 only, then M.4-M.10, then Slices 5-7 (recommended)
- **Risk: Slice 4 needs to be *authored* aware of M.9 RLS.** Concretely: the worker uses the bypass connection factory from day one (~0.5d added scope to Slice 4's C2). The notification handlers themselves are fine — they write tenant_id from `currentUser.TenantId` or null per §1.6. Manageable.
- **Risk: Slice 4's `BookingNotificationHandlers` ship without M.4 `TenantAuthorizationBehavior`.** Acceptable: notification handlers are `INotificationHandler<T>` event-driven, not request-driven; the tenancy guard rides on the *triggering* command (PlaceBooking, etc.) which already passes M.2's `currentUser.TenantId` validation. No regression.
- **Risk: Slices 5-7 wait 8-10 days.** Real but bounded; the user has explicitly said they're comfortable doing M.4-M.10 first, so the muscle for "no Slice 5-7 yet" is already built.

## Cross-module coupling issues you didn't ask about

1. **`MASTER_PLAN.md` line 21 is stale** — still says "Slice 4 ⏭ next". Whichever option lands, bump §1 status and §2 sequence with a revision-log entry. Do this *before* the next commit goes in, not at close-out.
2. **OPS.M.4 deletes the self-booking guard at `PlaceBookingHandler.cs:51`?** No — MTOP §2 says it stays. Confirm in M.4 plan; otherwise Slice 6's pricing-rules handler (which has a similar "owner-can't-quote-own-property" question, open) lands in ambiguity.
3. **Slice 7's `IRealtimeNotifier` push routes by `userId` from `IPropertyOwnerLookup`.** Once M.4 lands, the controller's role check (`Roles = "Owner,Admin"`) becomes redundant with the behavior. Slice 7 should be re-planned after M.4 to drop that gate; otherwise re-review effort doubles.
4. **OPS.M.3's `notification_log.tenant_id` stays nullable forever** (§1.6). Slice 4's handlers must explicitly set `tenant_id` for tenant_admin-bound templates (`owner.*`) and leave null for guest-bound templates. This is a correctness rule that lives in Slice 4's code, not in M.3's schema — needs a §2.x decision row added to `SLICE4_PLAN.md` before C1.
5. **Stripe Billing (platform-side, MTOP §9) is separate from Stripe Connect (M.5).** Neither is wired today. Slice 7's Revenue report has no way to surface "platform fees collected" until Billing lands — note this as out-of-scope in Slice 7 §6 when it's replanned.

## Minimum interleave for Option C

**Just Slice 4.** Not Slice 5. Slice 5's value (review loop + loyalty) is genuine but its dependency on Slice 4 is total; deferring 5 with 6 and 7 is zero extra coupling cost. Slice 4 is the only one that doubles as infrastructure (deferred-send column + worker + ACS wire) that the M.7 onboarding wizard and the M.10 isolation test pack will both lean on for demo credibility ("welcome email arrives", "tenant_id correctly set on owner-bound notifications").

Concretely: **Slice 4 (~3 days) → OPS.M.4 (1.5d) → M.5/M.6/M.7/M.8 in parallel (parallelism per MTOP §10) → M.9 (1.5d) → M.10 (2d) → Slice 5 → 6 → 7.**

---

## Decision required

| Option | Order | Architect view | User's stated preference |
|--------|-------|----------------|--------------------------|
| A | OPS.M.4-10 first, then Slice 4-7 | Demo hit + Slice 4 dual-rewrite hazard | ✅ preferred |
| B | Slice 4-7 first, pause OPS.M | Double rework on Slice 6/7 handlers under M.4 + M.9 | ❌ not asked |
| C | Slice 4 only → OPS.M.4-10 → Slice 5-7 | Lowest total rework + best demo coherence | recommended |

Awaiting user resolution. Once committed: update `docs/MASTER_PLAN.md` §1 status + §2 sequence + §8 revision log in the same commit that picks the order, before any code work begins under the chosen sequence.
