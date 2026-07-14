# Sequencing Re-evaluation — 2026-06-27 (Slice 4 vs OPS.M)

**Author**: architect re-consult.
**Supersedes**: the decision row in `docs/SEQUENCING_OPS_M_VS_SLICES.md` (Option C verdict, same author, half a day earlier).
**Trigger**: the Slice 4 plan review surfaced ~1 day of multi-tenancy contract authoring + event payload extensions + a Roslyn-style architecture test that the user — correctly — observed was OPS.M lane work being asked of Slice 4. They asked: *"to proceed with Slice 4, it is better to have tenant_id (tenancy implementation)? if so, why don't you complete OPS.M completely and then move to slice 4?"*

## Top-line

**I called Option C wrong this morning.** The right answer is **Option E**: ship `Slice OPS.M.4` (~1.5d) + the **contract half of `Slice OPS.M.9`** (~1.5d — the `IRlsBypassDbContextFactory<TContext>` contract, the default `NotificationsDbContext` factory registration, the bypass connection-string Key Vault binding, **without** the RLS policy DDL which stays in the full `Slice OPS.M.9`) **first**, then Slice 4 at its **original** 3-day scope, then `Slice OPS.M.5/6/7/8/M.9-policy-half/M.10`.

I underweighted the multi-tenancy debt Slice 4 was being asked to author on behalf of slices that haven't been planned in detail yet. The amendments I added to `docs/SLICE4_PLAN.md` review this morning — §2.5 bypass factory, §2.7 tenant-id correctness rule + arch test, §2.8 contract authoring, §2.9 `TenantActivated` payload extension, §7.4 five booking-event payload bumps, C0 commit — are not Slice 4 *consuming* multi-tenancy; they are Slice 4 *defining* it. That's the OPS.M lane's job.

The user's reframe is correct. The coupling is deeper than my "0.5d added to C2, manageable" line in the prior verdict. Acknowledged plainly: I anchored on demo coherence and undercounted contract-authoring drag.

## The four cost claims, re-attributed

### a) `IRlsBypassDbContextFactory<TContext>` contract — `Slice OPS.M.9` owns it

Slice 4 should not invent a contract for a runtime semantic (per-statement vs per-connection `SET LOCAL app.tenant_id`) that `Slice OPS.M.9`'s RLS design owns. `MULTI_TENANCY_OPS_PLAN.md` §3 already says *"a separate `IDbConnectionFactory<TenantBypass>`"* and `MASTER_PLAN.md` §2 row 11 notes the binding-granularity decision lives in M.9. Authoring it in Slice 4 forces M.9 to either amend the signature (broken consumers) or live with the wrong shape.

**Honest probability the Slice 4 shape survives M.9 unchanged: ~40%.** The ~30 LOC swap of the worker's DI registration when M.9 lands (`AddDbContext<NotificationsDbContext>` → `AddDbContext via factory`) is strictly cheaper than authoring + maintaining the wrong contract for 8-10 days.

### b) `notification_log.tenant_id` correctness rule + Roslyn/architecture test — `Slice OPS.M.4` owns the pattern; Slice 4 inherits it

`Slice OPS.M.4`'s `TenantAuthorizationBehavior` is the natural home for *"every write path sets `tenant_id` consciously"* — that's literally what the behavior asserts on the command side. Once M.4 ships the pattern (and the test infrastructure for *"no handler skips tenant-id"*), Slice 4's nullable case (`notification_log.tenant_id` null for guest-bound mail, set for owner-bound) is one decision row in the Slice 4 plan, not a Roslyn analyzer Slice 4 has to author. **Drop the §2.7 arch-test scope from Slice 4.**

### c) Event payload extensions (`BookingPlaced` etc. gain `Guid TenantId`) — `Slice OPS.M.4` owns these, not Slice 4

`Slice OPS.M.4`'s `TenantAuthorizationBehavior` validates commands against `ICurrentUser.TenantId`; the events it raises naturally need `TenantId` for downstream consumers (notifications, sync, reports). The five-event bump (`BookingPlaced/Confirmed/Cancelled/Rejected/ConflictDetected`) belongs to the M.4 commit where the behavior shapes write-side guarantees. Slice 4's handlers then read `evt.TenantId` and the §7.4 `IPropertyOwnerLookup` round-trip + `PropertyOwnerSnapshot.TenantId` `Guid?` wobble disappears for free. (Verified: `BookingEvents.cs` does not carry `TenantId` today; `PropertyOwnerSnapshot.TenantId` is `Guid?` per `IPropertyOwnerLookup.cs:24`.)

### d) `tenant.welcome` template + `TenantNotificationHandlers` — `Slice OPS.M.7` owns it

The handler subscribes to `TenantActivated`, which only fires from the onboarding wizard. Adding the 11th template and a new handler class in Slice 4 for an event no slice ships yet is speculative scope. M.7 ships the wizard; M.7 extends `TenantActivated` payload (`SupportEmail, DisplayName`); M.7 adds the welcome handler and template against the now-shipped ACS pipeline. **Slice 4 ships 10 templates, not 11.**

## Demo coherence — quantified pushback

I overstated this morning. *"Wizard hands off to silence"* is a **0.5d hotfix** in M.7's scope, not a credibility hit worth re-ordering for. Concretely: M.7 can either (i) emit the welcome via the now-existing ACS pipeline as a one-row handler addition (the *correct* answer, owned by M.7), or (ii) operator sends the welcome from `bookings@vrbook` manually for the first few tenants. Either way the *demo story* — *"tenant onboards, gets welcome email"* — is satisfied by M.7's own scope when M.7 ships. **Demo coherence is not load-bearing for the sequencing decision.** I let it carry weight it didn't earn.

## What Option E actually buys

Option E spends ~3 days on `Slice OPS.M.4 (1.5d) + the M.9 contract-half (~1.5d)` before Slice 4. Then Slice 4 ships at its **original** 3-day scope — no §2.5 amendment, no §2.7 arch test, no §2.8 contract authoring, no §2.9 welcome handler, no §7.4 event bumps.

| Option | Critical path to Slice 4 done | Risk | Notes |
|---|---|---|---|
| A — full M.4→M.10, then Slice 4 | ~17d | Demo silence (0.5d hotfix at M.7) | User's original instinct. Defensible. Strictly more conservative than E. |
| C — Slice 4 first (this morning's verdict) | ~4d | Wrong-shape bypass contract; arch-test authored in wrong slice; 5 event bumps in wrong slice | **Withdrawn.** |
| D — M.4 only, then Slice 4 | ~4.5d | Worker still uses default DB context; rewrites at M.9 | Cheap but leaves the M.9 retrofit unmanaged. |
| **E — M.4 + M.9-contract-half, then Slice 4** | **~6d** | Smallest sum of authoring + retrofit | **Recommended.** Slice 4 stays a 3d slice with no amendments. |

## Verdict

**Option E.** I called this wrong this morning.

If the user prefers Option A (complete all of OPS.M.4–10 first, then Slice 4) — the conservative read of their reframe — that is also defensible, costs 11 more days on the critical path, and gains a fully shipped multi-tenancy gate before Slice 4 starts. Option E is the architectural-cost-minimizing answer; Option A is the simplicity-maximizing answer; both are correct. User picks.

## What to update if Option E is accepted

1. `docs/MASTER_PLAN.md` §1 status table: re-order the rows.
2. `docs/MASTER_PLAN.md` §2 sequence table: re-order, restate the rationale, append a 2026-06-27 revision-log entry.
3. `docs/SLICE4_PLAN.md` will be **re-reviewed against the post-M.4 + M.9-contract-half world** at the slot when Slice 4 actually starts. Today's `docs/SLICE4_PLAN_REVIEW.md` (if persisted) should be marked superseded by this re-evaluation.
4. `docs/MULTI_TENANCY_OPS_PLAN.md` for Slice OPS.M.9 should be split into the "contract-half" (binding granularity decision + `IRlsBypassDbContextFactory<TContext>` + default factory registration + bypass Key Vault binding) and the "policy-half" (the actual RLS policy DDL + the bypass-role configuration), with the contract-half slot inserted between Slice OPS.M.4 and Slice 4. Naming option: `Slice OPS.M.9a` (contract) and `Slice OPS.M.9b` (policy). Cross-reference the unified Slice-prefix naming memory.

## What to update if Option A is accepted instead

1. `docs/MASTER_PLAN.md` §2 sequence: Slice 4 moves to slot 13 (after Slice OPS.M.10).
2. Slice 5's composite `(BookingId, PropertyId)` review-key reminder shifts from slot 13 to slot 14.
3. The 0.5d M.7 welcome-email hotfix is acknowledged as part of M.7's scope.
4. No Slice OPS.M.9 split is needed.
