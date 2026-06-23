# Slice 6 — Host↔Guest chat + pricing power-user (Plan)

**Status**: Proposed — awaiting user review.
**Author**: Plan agent (architect) consult, revision 2, 2026-06-23.
**REPLAN section**: `docs/REPLAN.md` §3 Slice 6 (lines 274-306).
**Sequence**: `docs/MASTER_PLAN.md` §2 row 3 (after Slice 5, before Slice 7).

This plan supersedes informal Slice 6 notes and revision 1 of this document. It is the contract.

---

## 1. What is already shipped (do not re-architect)

**Messaging module — backend essentially complete (A7 v1)**:
- `MessageThread` + `Message` aggregates at `src/Modules/VrBook.Modules.Messaging/Domain/`. Thread is one-per-booking (unique on `BookingId`), two participants frozen at create time. `Message.Send(...)` validates participant + body length (1..4000) and raises `MessageSent`.
- `MessagingDbContext` + migration `20260610140603_A7_InitMessagingSchema`. Indexes `ix_threads_guest_last`, `ix_threads_owner_last`, `ix_messages_thread_sent`, `ix_messages_unread` already present.
- `OnBookingConfirmedHandler` auto-creates the thread on `BookingConfirmed` (idempotent on re-fire from outbox).
- `ThreadsController` at `[Route("api/v1/threads")]` ships all 5 endpoints: `GET /`, `GET /{id}`, `GET /{id}/messages`, `POST /{id}/messages`, `POST /{id}/read`. Attachments stay 501. `RealtimeController.Negotiate` stays 501 (Slice 7).
- `ListMyThreadsHandler` already computes per-thread unread counts off `ix_messages_unread`.

**Pricing module — base + weekend + fees shipped; rules are the gap**:
- `PricingPlan` aggregate (Base + Weekend + min/max stay + dynamic toggle + Fees collection).
- `ComputeQuoteHandler` walks per-night, weekend uplift, fees, no rules. Comment at line 81 calls this out (`A3 v1: no loyalty discount, no dynamic adjustments.`).
- `PricingMapping.ToDto` hard-codes `Rules: Array.Empty<PricingRuleDto>()`.
- DTO + enums are already in contracts: `PricingRuleDto`, `CreatePricingRuleRequest`, `PricingRuleKind` (DateRangeOverride/LastMinute/LengthOfStay/DayOfWeek/Base), `PricingAdjustmentKind` (Absolute/Multiplier/Override). Phase 1 uses 3 of the 5 kinds.
- `PricingController.AddRule` / `DeleteRule` return 501 (`[Authorize]` is per-method, not class-level — relevant to §3 C3).
- `PricingPlanConfiguration` configures the plan + fees only. No rule table. The `row_version` column exists at line 30 but is *intentionally* not configured as a concurrency token — see §2.5.
- **Existing module exception**: `UpdatePricingPlanHandler` at lines 44-71 bypasses the aggregate and uses raw SQL ("EF Core 8 tracking weirdness we hit in Catalog"). New mutators in this slice opt to use EF; see §2.9 for the rationale and escape hatch.

**Contracts already in place but unused**:
- `PricingRuleAdded(Guid PricingPlanId, Guid RuleId)` and `PricingRuleRemoved(...)` exist in `src/VrBook.Contracts/Events/PricingEvents.cs` lines 5-7. Slice 6 wires the aggregate to raise them. (Alternative: delete unused contract types. Decision in §2.9.)

**Frontend stubs to replace**:
- `web/src/app/admin/messages/page.tsx` (19 lines). **Note**: its placeholder text references "Agent O1: admin can view any thread read-only" — that is an **admin moderation** viewer (Phase 2). Slice 6 replaces this stub with an **owner inbox** (own threads only); the moderation viewer is explicitly out of scope (see §6). This divergence is intentional and called out so a future reader doesn't think we silently dropped the moderation feature.
- `web/src/app/account/messages/page.tsx` (19 lines, guest inbox placeholder).
- `web/src/app/admin/pricing/page.tsx` (19 lines, placeholder card).
- `web/src/app/admin/bookings/[id]/page.tsx` has **no** Messages tab today. The REPLAN copy ("Messages tab shows active thread") is implemented in this slice as a **right-rail card + deep link**, not as an embedded tab — see §2.8 for the divergence rationale.
- `web/src/lib/api/pricing.ts` already types `PricingPlan` (with `rules: readonly unknown[]`) and `Quote`. No messaging client lib exists. No `me.ts` exists.

**Dependencies**:
- `web/package.json` does **not** include `@dnd-kit/*`. The drag-reorder library is a new dep (see §2.7).

---

## 2. Decisions (architect-locked)

### 2.1 Polling cadence + load budget — **30s on `/admin/messages` thread list + active pane; pause when tab hidden; ETag/`If-Modified-Since` on `GET /api/v1/threads`**

REPLAN sets the acceptance bar at "lands within 30s". `setInterval(reload, 30_000)` on both the thread list (refreshes unread badge) and the active conversation pane (appends new messages). `document.visibilityState === 'hidden'` skips the tick — protects mobile data and dev-cycle DB churn.

**Load budget**: assume ≤50 concurrent users in Slice 6 (REPLAN single-tenancy phase). At 50 users × `GET /api/v1/threads` every 30s = ~1.7 req/s sustained. `ListMyThreadsHandler` already touches `ix_threads_guest_last` / `ix_threads_owner_last`, but to keep the per-request cost near zero on no-op ticks the controller adds an `ETag` keyed off `max(LastMessageAt)` across the caller's threads (one extra `MAX()` aggregation per request) and honours `If-None-Match` / `If-Modified-Since` → `304 Not Modified`. The active conversation pane does the same trick keyed off the latest `MessageDto.CreatedAt`.

Revisit at >200 concurrent users — that's the SignalR forcing function and Slice 7's problem. No `Negotiate` work lands here; the polling code is structured for the swap (§6).

### 2.2 Pricing rules — **new `PricingRule` child entity owned by `PricingPlan`**

`PricingRule` lives under `PricingPlan` (aggregate boundary already exists; the rules collection is a natural fit alongside `Fees`). New EF entity, new table `pricing.pricing_rules`, FK to `pricing_plans.Id`, cascade delete.

The aggregate gains `AddRule(...)`, `RemoveRule(...)`, `ReorderRules(IReadOnlyList<Guid>)`, `SetRuleEnabled(Guid, bool)`. Mutations route through the aggregate; the controller does not bypass it. See §2.9 for the EF-vs-raw-SQL trade-off.

### 2.3 Rule kinds — **3 of the 5 enum values, others stay declared but unhandled**

The enum already declares 5 kinds. Phase 1 ships handlers for:
- `PricingRuleKind.DateRangeOverride` — Seasonal. Fields used: `StartDate`, `EndDate`, `AdjustmentKind`, `AdjustmentValue`. Multiplier, Absolute, or Override semantics — see §2.4.1 matrix.
- `PricingRuleKind.LastMinute` — Field used: `DaysBeforeCheckin`. Triggers when `Checkin - clock.Today <= DaysBeforeCheckin`. See §2.10 for the clock seam.
- `PricingRuleKind.LengthOfStay` — Fields used: `MinNights` and `MaxNights` (see §2.11 for `MaxNights` semantics).

`DayOfWeek` (kind 3) is already covered by the existing weekend uplift. `Base` (kind 4) is the plan's `BaseNightlyRate`. Both stay valid enum values but `ComputeQuoteHandler` skips them.

The contract `PricingRuleDto` already has every column needed. No DTO change.

### 2.4 Stacking — **Applied in priority order; per-kind semantics; order matters when adjustment kinds mix**

For each enabled rule (priority ascending — lower number wins), apply against the running per-night subtotal in priority order. The application is per-kind:

- `Multiplier`: `nightly[i] *= rule.AdjustmentValue` (e.g. `1.5m` = +50%).
- `Absolute`: `nightly[i] += rule.AdjustmentValue` (signed; negative is allowed for discounts).
- `Override`: `nightly[i] = rule.AdjustmentValue` (replaces the rate for nights inside the rule's window; useful for `DateRangeOverride` ONLY — see §2.4.1 — meaningless on `LastMinute`/`LengthOfStay`).

Rule application is conceptually:
```
nightly[i] = BaseNightly (or WeekendRate)              // step 1
for each rule in priority ascending:                    // step 2
    if rule applies to this night/stay:
        nightly[i] = apply(rule, nightly[i])
subtotal = sum(nightly[i])
```

**Order matters when adjustment kinds mix.** A previous draft of this plan claimed "multiplication commutes — sanity check, not enforced." That is **wrong for mixed-kind stacks**: `Absolute(+10) → Multiplier(×1.5)` over base 100 yields `(100+10)×1.5 = 165`, while swapping yields `(100×1.5)+10 = 160`. The ordering is therefore a contract, not an implementation detail, and the priority-swap test must use **all-Multiplier rules** for the commutativity sanity check (or assert non-commutativity for the mixed case).

The `LastMinute` and `LengthOfStay` kinds apply to **every night** of the stay (whole-stay condition checked against the request, not the date). `DateRangeOverride` applies only to nights inside its `[StartDate, EndDate]` window. For stays that straddle a `DateRangeOverride` window, only the in-window nights are adjusted.

### 2.4.1 Rule-kind × adjustment-kind matrix (3×3)

| Rule kind          | + Absolute              | + Multiplier            | + Override                                  |
|--------------------|-------------------------|-------------------------|---------------------------------------------|
| DateRangeOverride  | Per in-window night += V| Per in-window night *= V| Per in-window night = V                     |
| LastMinute         | Every night += V        | Every night *= V        | **`quote.invalid_rule`** at AddRule time    |
| LengthOfStay       | Every night += V        | Every night *= V        | **`quote.invalid_rule`** at AddRule time    |

`LastMinute × Override` and `LengthOfStay × Override` are rejected by `PricingPlan.AddRule(...)` with `BusinessRuleViolationException("quote.invalid_rule", ...)`. Reason: "override" is semantically "replace the rate" — for a whole-stay rule that means flattening the entire stay to a single number, which collapses the weekend uplift and any prior priority's work and is almost never what an owner wants. If a real use case appears, lift the guard in Phase 2.

The UI's `RuleEditorModal` mirrors the matrix: choosing `LastMinute` or `LengthOfStay` disables the `Override` option in the adjustment-kind dropdown.

### 2.5 Concurrent reorder — **last-write-wins; revisit at Phase 2 multi-owner**

Two owners drag-reordering rules on the same plan simultaneously: the second `SaveChangesAsync()` overwrites the first silently. `PricingPlanConfiguration` line 28 already documents that `row_version` is intentionally NOT configured as a concurrency token in Phase 1.

Acceptable for ≤50 concurrent users single-tenancy. The cost of the alternatives (serializable transaction wrapping reorder + retry loop; promoting `row_version` to a token + 409-conflict round-trip in the UI) is not justified by Phase-1 collision probability. Revisit when Phase 2 multi-owner-per-property lands.

`ReorderRules(IReadOnlyList<Guid> orderedIds)` rewrites all priorities `0..N-1` in one save inside a single `SaveChangesAsync()`. No serializable txn wrapper. The UI doesn't show a conflict banner because there's no conflict signal.

### 2.6 Preview pane + the IsEnabled toggle — **save-then-preview; per-rule `PATCH .../enabled` endpoint avoids re-emitting `PricingRuleAdded` for toggle**

The editor saves the rule (or the draft reorder) before refreshing the preview. The preview just hits the existing `POST /api/v1/properties/{id}/quotes`. Pro: no second code path through `ComputeQuoteHandler`, no risk of "preview disagrees with the real quote" drift, no new command. Con: a half-built rule cannot be previewed without committing. Acceptable.

**Toggle path**: the owner stages a draft as `IsEnabled = false`, then needs to flip it on for preview. Routing that through `PUT /rules/{id}` would re-emit `PricingRuleRemoved` + `PricingRuleAdded` for what is logically just a flag flip (and any future subscribers to those events would see noisy state). Slice 6 ships a dedicated `PATCH /api/v1/properties/{propertyId}/pricing/rules/{ruleId}/enabled` body `{ isEnabled: bool }`. Returns the updated `PricingRuleDto`. No event raised — `IsEnabled` is a flag, not a structural change.

If field testing finds owners want true what-if without persistence, that becomes a Phase-2 addition.

### 2.7 Drag-reorder library — **`@dnd-kit/core` + `@dnd-kit/sortable`, new dep**

REPLAN explicitly names dnd-kit. Not in `web/package.json` today. Adding two packages (~30KB gzipped each, no transitive React clones, MIT). `react-beautiful-dnd` is deprecated; dnd-kit is the modern choice and the REPLAN-named one. No alternative seriously considered.

### 2.8 Messages on `/admin/bookings/[id]` — **deep-link card, not embedded tab (divergence from REPLAN copy)**

REPLAN line 279 says "Owner on `/admin/bookings/[id]` → Messages **tab** shows active thread". Slice 6 implements this as a **right-rail card with a deep link to `/admin/messages?thread={id}`**, not as an embedded tab.

Why: the booking detail page is already dense (5 cards, dev shortcuts, reject modal) and has no tab pattern today. A tab would force introducing one + a second copy of the conversation pane. A card is ~20 lines of JSX versus ~200 for tabs+pane duplication. The `/admin/messages` page reads `?thread=` on mount and auto-selects, so click-through is one hop.

This is a deliberate divergence from REPLAN. Calling it out so review of REPLAN-vs-built doesn't flag it as "missing tab". Acceptance 1 + 2 are still met (owner sees + replies); the journey is one extra click.

### 2.9 Mutation pattern — **Aggregate + EF, with raw-SQL escape hatch documented**

`PricingPlan.AddRule(...) → SaveChangesAsync()` through `PricingPlanRepository` — the clean DDD path. Three reasons over matching `UpdatePricingPlanHandler`'s raw-SQL convention:

1. The contract events `PricingRuleAdded` / `PricingRuleRemoved` already exist (`src/VrBook.Contracts/Events/PricingEvents.cs` lines 5,7). Going raw-SQL forfeits their emission and would force a follow-up cleanup that deletes them. Wiring the aggregate to raise them is ~6 lines.
2. The 3×3 matrix in §2.4.1 has two combos (`LastMinute × Override`, `LengthOfStay × Override`) that the aggregate constructor must reject. Raw-SQL paths historically skip these guards.
3. The `UpdatePricingPlanHandler` weirdness was specifically a join-table fan-out problem (delete-all-then-reinsert fees with `OnDelete.Cascade`). The new rule mutators are pure inserts / pure single-row updates / pure deletes — none of them touches the same shape.

**Escape hatch**: if the EF-tracking weirdness does bite — symptom would be `PricingPlanRepository.GetByPropertyIdAsync` returning stale rules after a write in the same scope — the contingency is to drop `AddPricingRuleHandler` and `RemovePricingRuleHandler` to raw SQL (mirroring `UpdatePricingPlanHandler` lines 47-70). `ReorderPricingRulesHandler` stays on the aggregate (single transaction, all-or-nothing semantic). Document any such drop in `docs/runbooks/pricing-rules.md`.

### 2.10 Clock seam for `LastMinute` — **inject `IDateTimeProvider`**

`LastMinute` depends on "today". `ComputeQuoteHandler` currently calls `DateTimeOffset.UtcNow` directly (line 95). `IDateTimeProvider` already exists at `src/VrBook.Contracts/Interfaces/IDateTimeProvider.cs` with `UtcNow` and `Today`, and is registered in the DI container in every other module's `SystemClock` binding.

`ComputeQuoteHandler`'s ctor gains an `IDateTimeProvider clock` parameter. The `LastMinute` predicate uses `clock.Today`. Existing tests that exercise the `ExpiresAt` calculation (`ComputeQuoteHandlerTests.Quote_expires_in_about_15_minutes`) keep passing as long as the test wires a real-clock substitute. The new `ComputeQuoteWithRulesTests` injects a frozen clock so `LastMinute` is deterministic.

### 2.11 `LengthOfStay.MaxNights` semantics — **`MinNights ≤ Nights ≤ MaxNights`, where `MaxNights == null` means open-ended**

Two equally defensible readings:
- "Min ≤ Nights" only (Max ignored).
- "Min ≤ Nights ≤ Max" (closed range).

Pick the second: it lets owners express "long-stay discount tiers" without writing N rules that all fire on top of each other (e.g. `Min=7,Max=13: -10%` and `Min=14,Max=null: -20%` instead of one 10% + one 10% that compound to -19%). When `MaxNights is null`, the upper bound is treated as open.

`PricingPlan.AddRule(...)` validates `MinNights >= 1` and (`MaxNights is null` or `MaxNights >= MinNights`).

### 2.12 Cross-property authorization

`PricingController` endpoints today only check `[Authorize(Roles = "Owner,Admin")]` per-method (the class itself has no `[Authorize]`). That gates anonymous traffic but **not** owner-of-A from hitting `POST /properties/{B}/pricing/rules`. `UpdatePricingPlanHandler` only checks `currentUser.UserId is null` — no ownership check.

Slice 6 adds a property-ownership check inside each new handler (and to the existing `UpdatePricingPlanHandler` for consistency, but mark that line as opportunistic — not the main goal of this slice). Pattern: inject the catalog ownership lookup (already exists at the API boundary in `adminListMyProperties`'s handler) into the pricing command handlers, throw `ForbiddenException` when `propertyId` isn't owned by the caller. Tests cover the 403 path.

---

## 3. Commit split (6 commits)

### C1 — Domain: `PricingRule` entity + migration + `PricingPlan` mutators + events

- New entity `src/Modules/VrBook.Modules.Pricing/Domain/PricingRule.cs`. Properties match `PricingRuleDto` 1:1: `Id`, `PricingPlanId`, `Kind`, `Priority`, `StartDate?`, `EndDate?`, `DayOfWeekMask?`, `MinNights?`, `MaxNights?`, `DaysBeforeCheckin?`, `AdjustmentKind`, `AdjustmentValue`, `IsEnabled`. Constructor validates per-kind invariants:
  - `DateRangeOverride`: requires `StartDate <= EndDate`. All three adjustment kinds allowed.
  - `LastMinute`: requires `DaysBeforeCheckin >= 1`. Rejects `Override`.
  - `LengthOfStay`: requires `MinNights >= 1` and (`MaxNights is null` or `MaxNights >= MinNights`). Rejects `Override`.
- `PricingPlan` gains: `_rules` list, `IReadOnlyList<PricingRule> Rules`, methods `AddRule(...)`, `RemoveRule(Guid)`, `ReorderRules(IReadOnlyList<Guid>)`, `SetRuleEnabled(Guid, bool)`. `AddRule` raises `PricingRuleAdded`. `RemoveRule` raises `PricingRuleRemoved`. `SetRuleEnabled` raises nothing (flag flip, not structural).
- New `PricingRuleConfiguration : IEntityTypeConfiguration<PricingRule>` — table `pricing.pricing_rules`, FK + cascade, index on `(pricing_plan_id, priority)`. `IsEnabled` defaults to `true`.
- Migration `Slice6_PricingRules` (matches existing convention `Slice4_DispatchColumns`, `Slice3_AvailabilityBlocks`, `Slice0_BookingHolds`) adds the table.
- `PricingDbContext` exposes `DbSet<PricingRule> PricingRules`.
- `PricingPlanRepository.GetByPropertyIdAsync` adds `.Include(p => p.Rules)` so mutations + reads come back hydrated. (Repository today only `Include`s `Fees`.)
- `PricingMapping.ToDto` updated: `Rules: p.Rules.OrderBy(r => r.Priority).Select(...).ToArray()`. The `Array.Empty` hack dies here.
- Unit tests in `tests/VrBook.Api.IntegrationTests/Pricing/PricingRuleInvariantsTests.cs` cover:
  - Constructor guards per the §2.4.1 matrix (9 cases — 7 happy + 2 reject).
  - `ReorderRules` rewrites all priorities 0..N-1.
  - `RemoveRule` no-ops on unknown id.
  - `SetRuleEnabled` flips the flag without raising events.

**Mutation pattern decision (Q3)**: aggregate + EF. See §2.9. Raw-SQL contingency is documented; not implemented unless tracking weirdness surfaces.

No quote-engine change yet. The slice is shippable here as "rules persist but don't compute" — by design, separates schema risk from engine risk.

### C2 — `ComputeQuoteHandler` applies rules (engine only)

- After the per-night loop (base + weekend) and **before** the fee loop, iterate `plan.Rules.Where(r => r.IsEnabled).OrderBy(r => r.Priority)` and apply per §2.4 + §2.4.1.
- New private helper `ApplyRule(NightlyLineDto[] nights, PricingRule rule, QuoteRequest req, IDateTimeProvider clock)`.
- `NightlyLineDto.RuleApplied` change: previous draft proposed making this a comma-separated string when multiple rules touch a night. **Reversed**: the existing test suite at `tests/VrBook.Api.IntegrationTests/Domain/ComputeQuoteHandlerTests.cs` already asserts `RuleApplied.Should().Be("base")` (line 56) and `.Should().Equal("weekend", "weekend", "base")` (line 72). Changing the field's contract from "single token" to "comma list" silently breaks those. Two options were considered:
  - **(A) Keep single token, change to the highest-priority applied rule's name.** Picked. Front-end shows one badge per row; if owners need full visibility, they read the rules table.
  - (B) Add a parallel `RulesApplied: string[]` field on `NightlyLineDto` and deprecate `RuleApplied`. Rejected — DTO churn that ripples through every test and the Quote widget for marginal UX.
  - The pre-existing assertions therefore stay green: nights touched only by weekend uplift still report `"weekend"`; nights touched by `DateRangeOverride` report `"seasonal"`; etc.
- New `quote.invalid_rule` error if a rule kind that the handler doesn't handle is found with `IsEnabled = true` (defensive; the §2.4.1 AddRule guards prevent this on the write path).
- `ComputeQuoteHandler` ctor gains `IDateTimeProvider clock`. `PricingModule.AddModule` registers `SystemClock` in the app-runtime DI graph (the migrator path already does it).
- Existing `ComputeQuoteHandlerTests` migrated: the two `RuleApplied` assertions at lines 56 and 72 stay as-is (they hit nights with no rule). Any test that constructs `ComputeQuoteHandler` directly gets a `Substitute.For<IDateTimeProvider>()` injected.

Tests in `tests/VrBook.Api.IntegrationTests/Pricing/ComputeQuoteWithRulesTests.cs`:
- Seasonal +50% on Dec 20–Jan 5: night inside window = base × 1.5; outside = base. Mixed-window stay sums correctly.
- Seasonal Override = 200 (Dec 20–Jan 5): in-window nights = 200; out-of-window unchanged.
- Last-minute -20% when `Checkin - clock.Today <= 2 days`: applies to every night; no-op for stays > 2d out.
- LOS -10% when `7 <= Nights <= 13`: applies; 6-night stay no-op; 14-night stay no-op (per §2.11 closed range).
- Two Multiplier rules compound (seasonal +50% then LOS -10% → 1.5 × 0.9 = 1.35× base). Priority swap → same total (sanity check valid for **all-Multiplier** stacks per §2.4).
- Mixed-kind ordering: `Absolute(+10)` priority 0 then `Multiplier(×1.5)` priority 1 → `(100+10)×1.5 = 165` per night. Swap priorities → 160. **Asserts non-commutativity** to lock the contract.
- Disabled rule (`IsEnabled = false`) skipped.

### C3 — Pricing rule endpoints + cross-property auth tests

**API endpoints** (`PricingController`):
- `POST /api/v1/properties/{propertyId}/pricing/rules` — was 501, now wires to `AddPricingRuleCommand` → returns `PricingRuleDto` with `201 Created`.
- `DELETE /api/v1/properties/{propertyId}/pricing/rules/{ruleId}` — was 501, wires to `RemovePricingRuleCommand` → `204 No Content`.
- New `PUT /api/v1/properties/{propertyId}/pricing/rules/{ruleId}` → `UpdatePricingRuleCommand`. Aggregate path: `RemoveRule + AddRule` (re-emits the pair of events — semantically correct: structure changed).
- New `PATCH /api/v1/properties/{propertyId}/pricing/rules/{ruleId}/enabled` body `{ isEnabled: bool }` → `SetPricingRuleEnabledCommand`. No event raised (§2.6).
- New `POST /api/v1/properties/{propertyId}/pricing/rules/reorder` body `{ ruleIds: Guid[] }` → `ReorderPricingRulesCommand`. Returns the full `PricingPlanDto`.
- All five `[Authorize(Roles = "Owner,Admin")]` (the per-method pattern already used by the existing endpoints; not lifted to class-level to keep the public `Compute` quote endpoint anonymous via the sibling `QuotesController`).

Each command handler does the §2.12 property-ownership check via the existing catalog port.

Tests in `tests/VrBook.Api.IntegrationTests/Pricing/PricingRuleEndpointsTests.cs`:
- Happy CRUD for each kind (DateRangeOverride/LastMinute/LengthOfStay).
- Reorder persists across re-fetch.
- `PATCH .../enabled` toggles `IsEnabled` and the next quote reflects it (engine smoke).
- **Cross-property auth**: owner-of-A authenticated, hits `POST /properties/{B}/pricing/rules` → 403. Same for DELETE, PUT, PATCH, reorder. (Admin role bypasses — separate test asserts that.)
- §2.4.1 reject combos return 422 / `quote.invalid_rule`.

Acceptance 3's backend half lands at end of C3.

### C4 — `/admin/pricing` editor UI

New `web/src/app/admin/pricing/page.tsx` (replace stub):
- Property dropdown at top using `adminListMyProperties()` from `web/src/lib/api/catalog.ts:234` (NOT a nonexistent `getOwnerProperties()` — earlier draft was wrong about that name). Defaults to first owned property.
- Plan basics card: read-only summary of base + weekend + currency. Editing the basics is out of scope for Slice 6 (`/admin/pricing` plan-basics editor is a Phase 2 polish; `PUT /pricing` already works for an owner who curls it).
- Rules table: columns `↕ | Priority | Kind | Window | Adjustment | Enabled | Edit | Delete`. Drag handle on the leftmost column; rows are `<SortableItem>` from `@dnd-kit/sortable`.
- "Add rule" button → `RuleEditorModal` with kind dropdown that swaps the form fields shown:
  - Seasonal: two date pickers + adjustment kind/value (all three allowed).
  - Last-minute: `DaysBeforeCheckin` int + adjustment (Override disabled per §2.4.1).
  - LOS: `MinNights` int (+ optional `MaxNights`) + adjustment (Override disabled).
- Enable toggle on each row → `PATCH .../enabled` (no full PUT, no event noise).
- Preview pane on the right: date range picker (defaults to 7 nights starting next Friday) + guests + "Refresh preview" → `computeQuote(propertyId, ...)` → per-night table with `RuleApplied` badges and the totals breakdown.
- On drag end: optimistic local reorder + `reorderPricingRules(propertyId, ruleIds)` → on failure, revert and toast.

New `web/src/lib/api/pricing.ts` additions:
- `createPricingRule(propertyId, body): Promise<PricingRule>`
- `updatePricingRule(propertyId, ruleId, body): Promise<PricingRule>`
- `deletePricingRule(propertyId, ruleId): Promise<void>`
- `setPricingRuleEnabled(propertyId, ruleId, isEnabled): Promise<PricingRule>`
- `reorderPricingRules(propertyId, ruleIds): Promise<PricingPlan>`
- Promote `rules: readonly unknown[]` to typed `readonly PricingRule[]`.

New `web/src/components/pricing/RuleEditorModal.tsx`, `SortableRuleRow.tsx`, `QuotePreviewPane.tsx`.

`@dnd-kit/core` + `@dnd-kit/sortable` added to `web/package.json`.

Acceptance 3 + 4 land here.

### C5 — Owner messaging UI (`/admin/messages`) + booking-detail card

Replace `web/src/app/admin/messages/page.tsx` stub (the "Agent O1: admin can view any thread read-only" comment is removed — that was the moderation viewer (Phase 2, see §6); this slice ships an owner-inbox view of the owner's own threads).

New `web/src/components/messaging/ThreadInbox.tsx` — left pane:
- List of threads from `GET /api/v1/threads` (existing endpoint).
- Each row: counterparty display name, booking ref, last message preview, relative time, unread badge.
- Polls via `useThreadPoller()` (see §6 SignalR seam).
- Selecting a thread sets the active one; on `/admin/messages` writes `?thread={id}` to the URL via `router.replace`; reading the param on mount auto-selects so the booking-detail deep link from §2.8 works.

New `web/src/components/messaging/ConversationPane.tsx` — right pane:
- `GET /api/v1/threads/{id}/messages` on mount + via `useThreadPoller(threadId)`.
- Renders bubbles, left/right alignment by `senderUserId === me`. Read receipts on owned bubbles (`m.ReadAt != null`).
- Send composer: `<textarea>` + Send → `POST /api/v1/threads/{id}/messages` → optimistically append on success.
- On open, fires `POST /api/v1/threads/{id}/read` with the latest message id.
- Empty state when no thread selected.

New `web/src/lib/api/messaging.ts`: `listThreads()`, `getThread(id)`, `listMessages(threadId)`, `sendMessage(threadId, body)`, `markThreadRead(threadId, upToMessageId)`. Types `Thread`, `Message` mirroring `ThreadDto` / `MessageDto`.

New `web/src/lib/api/me.ts`:
```ts
export interface CurrentUser { readonly id: string; readonly displayName: string; readonly email: string; }
export const getCurrentUser = (): Promise<CurrentUser> => apiFetch<CurrentUser>('/api/v1/me');
```
Reads from `GET /api/v1/me` (returns `UserDto` with `.Id`). The earlier draft of this plan said `getDevPersonas()` returns a user id — **wrong**; `web/src/lib/api/devAuth.ts:8-13` shows `DevPersonaInfo` has `value/displayName/email/roles` only, no `Id`. `IdentityController.Get` at `src/VrBook.Api/Controllers/IdentityController.cs:23` is the right source.

Booking-detail card edit on `web/src/app/admin/bookings/[id]/page.tsx`:
- Small right-rail "Messages" card with `Open thread →` linking to `/admin/messages?thread={threadId}`.
- Resolve `threadId` via a new optional query filter `?bookingId=` on `ListMyThreadsQuery`. **One-line query handler change** + a `[FromQuery] Guid? bookingId` pass-through on `ThreadsController.MyThreads`.

Acceptance 1 (owner sees within 30s) lands here.

### C6 — Guest messaging UI (`/account/messages`) + seed/docs/acceptance

Replace `web/src/app/account/messages/page.tsx` stub with the same two-pane shell as C5 (shared `ThreadInbox` + `ConversationPane`). The guest-side persona is the only difference; both panes are persona-agnostic.

- Sample seasonal rule on the demo property's pricing plan (idempotent dev-seed insert in `Program.cs` if a seeder exists; else ship `docs/runbooks/slice6-seed.md` with the SQL snippet — match the existing convention).
- README/QUICKSTART doc tweak documenting the 30s polling expectation and the dnd-kit dep.
- `docs/runbooks/pricing-rules.md` — short runbook covering "rule didn't apply to a quote" (check `IsEnabled`, check date window, check priority, check §2.4.1 matrix).
- Verification recipe walk in §7.

Acceptance 2 (guest sees reply within 30s) lands here.

---

## 4. Concrete file additions

### Contracts
- No DTO changes — `PricingRuleDto`, `CreatePricingRuleRequest`, `PricingRuleKind`, `PricingAdjustmentKind` already exist. `NightlyLineDto.RuleApplied` stays a `string?` single token (see C2 reasoning).
- `ListMyThreadsQuery(Guid? BookingId = null)` — optional filter param.

### Pricing module
- `Domain/PricingRule.cs` — new aggregate child.
- `Domain/PricingPlan.cs` — edit: `_rules` list, `AddRule`, `RemoveRule`, `ReorderRules`, `SetRuleEnabled`, event raises.
- `Application/Plans/Commands/AddPricingRuleCommand.cs` + handler — new.
- `Application/Plans/Commands/UpdatePricingRuleCommand.cs` + handler — new.
- `Application/Plans/Commands/RemovePricingRuleCommand.cs` + handler — new.
- `Application/Plans/Commands/SetPricingRuleEnabledCommand.cs` + handler — new.
- `Application/Plans/Commands/ReorderPricingRulesCommand.cs` + handler — new.
- `Application/Common/PricingMapping.cs` — edit: real `Rules` mapping.
- `Application/Quotes/Commands/ComputeQuoteHandler.cs` — edit: rule loop, `IDateTimeProvider` ctor param, private `ApplyRule` helper.
- `Infrastructure/Persistence/PricingDbContext.cs` — edit: `DbSet<PricingRule>`.
- `Infrastructure/Persistence/PricingRuleConfiguration.cs` — new.
- `Infrastructure/Persistence/Migrations/{timestamp}_Slice6_PricingRules.cs` — new (timestamp prefix per EF convention; name body `Slice6_PricingRules`).
- `Infrastructure/Persistence/PricingPlanRepository.cs` — edit: `.Include(p => p.Rules)`.
- `PricingModule.cs` — edit: register `IDateTimeProvider` → `SystemClock` for the runtime path.

### Messaging module
- `Application/Threads/Queries/ThreadQueries.cs` — edit: `ListMyThreadsQuery(Guid? BookingId = null)`, handler filters when set.

### API
- `Controllers/PricingController.cs` — edit: 5 endpoints (replace 2 stubs + add 3 new). `ETag`/`If-None-Match` shape added to `ThreadsController.MyThreads` (§2.1).
- `Controllers/ThreadsController.cs` — edit: pass-through `[FromQuery] Guid? bookingId`; ETag handling on list + per-thread messages.

### Web
- `web/package.json` — add `@dnd-kit/core`, `@dnd-kit/sortable`.
- `web/src/app/admin/pricing/page.tsx` — replace stub.
- `web/src/app/admin/messages/page.tsx` — replace stub (drop "Agent O1" comment).
- `web/src/app/account/messages/page.tsx` — replace stub.
- `web/src/app/admin/bookings/[id]/page.tsx` — edit: Messages card on right rail.
- `web/src/components/pricing/RuleEditorModal.tsx` — new.
- `web/src/components/pricing/SortableRuleRow.tsx` — new.
- `web/src/components/pricing/QuotePreviewPane.tsx` — new.
- `web/src/components/messaging/ThreadInbox.tsx` — new.
- `web/src/components/messaging/ConversationPane.tsx` — new.
- `web/src/hooks/useThreadPoller.ts` — new (the SignalR seam, §6).
- `web/src/lib/api/pricing.ts` — edit: typed rules + 5 mutators.
- `web/src/lib/api/messaging.ts` — new.
- `web/src/lib/api/me.ts` — new (`getCurrentUser` hitting `/api/v1/me`).

### Tests
- `tests/VrBook.Api.IntegrationTests/Pricing/PricingRuleInvariantsTests.cs` — new.
- `tests/VrBook.Api.IntegrationTests/Pricing/ComputeQuoteWithRulesTests.cs` — new (frozen `IDateTimeProvider`).
- `tests/VrBook.Api.IntegrationTests/Pricing/PricingRuleEndpointsTests.cs` — new (CRUD + reorder + cross-property auth).
- `tests/VrBook.Api.IntegrationTests/Messaging/ThreadByBookingFilterTests.cs` — new (`?bookingId=` filter).

### Runbooks
- `docs/runbooks/pricing-rules.md` — new.
- `docs/runbooks/slice6-seed.md` — new (sample rule SQL for dev).

### Infra + CI
- None.

---

## 5. Scope-cut order (drop top first when deadline bites)

1. **`LengthOfStay` rule kind handler** — REPLAN explicitly flags this as scope-flex.
2. **`LastMinute` rule kind handler** — same shape as #1 but for the second deferred kind.
3. **Preview pane on `/admin/pricing` (C4 sub-feature)** — Owner saves, hits `/properties/[slug]` in a second tab. Loses acceptance 3's UX polish but the data path still works. **This drops BEFORE the booking-detail card** (previous draft had this in the wrong order) — the preview pane is a one-page polish whose absence is recoverable; the booking-detail card is the only owner discoverability path from a booking to its thread.
4. **Booking-detail Messages card + `?bookingId=` filter** (§2.8). Owner gets to threads via `/admin/messages` directly. Loses the one-click drill-in; acceptance 1 + 2 still met.
5. **`/admin/pricing` rule editor entirely** (C4 except the engine). The 5 API endpoints land in C3 → owner curls them. Acceptance 3 + 4 fall.

Never falls: C1 (rules persist), C2 engine (rules compute, even if only Seasonal survives 1+2), C5/C6 messaging UI. That minimum delivers acceptance 1 + 2 + the engine half of acceptance 3.

---

## 6. Out of scope (Phase 1.5+ / OPS / Slice 7)

- **SignalR push for messages** — Slice 7. `RealtimeController.Negotiate` stays 501. **Seam**: polling code lives in a single `useThreadPoller(threadId)` custom hook at `web/src/hooks/useThreadPoller.ts`. In Slice 7 it's swapped for `useThreadStream(threadId)` (one hook → one hook substitution). Both components (`ThreadInbox`, `ConversationPane`) call only `useThreadPoller` — no direct `setInterval` in component code.
- **Message attachments** — A7.5 placeholder. `POST /threads/{id}/attachments` stays 501.
- **Chat notifications** (email/push when a message arrives while the other party is offline) — Phase 2.
- **`DayOfWeek` rule kind** — already covered by `WeekendRate`.
- **Gap-night + Occupancy rules** — REPLAN-deferred to Phase 2.
- **"What-if" preview without persisting** — see §2.6.
- **Rule audit log / history** — owners overwrite; no prior versions kept.
- **Public `/properties/[slug]` quote widget pulling rules** — already does transitively (same `ComputeQuoteHandler`).
- **Admin moderation of threads (read-only viewer for any thread regardless of participation)** — the stub at `/admin/messages` referenced this; Slice 6 ships owner-inbox only. A `[Authorize(Roles = "Admin")]` read-only viewer is Phase 2.
- **`row_version` concurrency token + 409-retry UI on reorder** — see §2.5. Phase 2 multi-owner.
- **Plan basics editor on `/admin/pricing`** — out of slice; `PUT /pricing` works for an owner who curls it.

---

## 7. Verification recipe (end-of-slice)

1. **Migrations**: `dotnet ef database update --context PricingDbContext` — verify `pricing.pricing_rules` table exists. Check `IsEnabled` column default = `true`. `SELECT column_name, column_default FROM information_schema.columns WHERE table_schema='pricing' AND table_name='pricing_rules';`
2. **Test grep**: `rg "RuleApplied\.Should" tests/` — every assertion should be `Be("base")`, `Be("weekend")`, `Be("seasonal")`, or `Be(null)`. No `Contain`-style assertions (would indicate someone went to comma-list contract).
3. **Thread auto-create check**: confirm `SELECT COUNT(*) FROM messaging.message_threads WHERE booking_id = '{demo}';` returns 1 after the demo booking confirms.
4. **Seed a Seasonal rule** via `docs/runbooks/slice6-seed.md` SQL: Dec 20–Jan 5, Multiplier 1.5, Priority 0, Enabled.
5. **Browser**: open `/admin/pricing` → property dropdown shows demo property → rules table lists Seasonal row → add a Last-minute -20% rule → drag to reorder → reload → order persists (acceptance 4).
6. **Preview pane**: range Dec 22–Dec 26 → Refresh → per-night badges show `seasonal` → subtotal = base × 1.5 × 4 nights (acceptance 3 UI).
7. **Public `/properties/[demo-slug]`** with same dates → quote total matches (acceptance 3 e2e).
8. **Cross-property auth**: as owner-A, `curl POST /properties/{owner-B-property}/pricing/rules` → 403.
9. **Chat — guest side**: log in as guest persona (DevAuth) → `/account/messages` shows the auto-created thread → send "Hi from the guest."
10. **ETag check**: `curl -i /api/v1/threads` twice in succession with `If-None-Match: "<etag-from-first>"` → second returns `304 Not Modified`.
11. **Chat — owner side**: second browser session as owner persona → `/admin/messages` → within 30s the unread badge increments and the message appears in the active pane on open (acceptance 1).
12. **Reply**: owner sends "Welcome!" → switch to guest tab → within 30s message appears (acceptance 2).
13. **Booking detail deep link**: `/admin/bookings/{id}` → Messages card shows `1 thread · 0 unread` → click `Open thread →` → lands at `/admin/messages?thread={id}` with the right thread auto-selected.
14. **CI**: integration tests green for `Pricing.*` + `Messaging.ThreadByBookingFilterTests`. Web typecheck + lint green.

If 5 + 6 + 7 + 11 + 12 all pass, the slice ships.

---

## 8. What gets approved by this document

If you approve this plan, the next concrete actions are:

1. I commit this document as `docs/SLICE6_PLAN.md`.
2. I open C1 — `PricingRule` entity + migration + aggregate mutators + event raises.
3. Each commit ends with: I push, CI runs, I report green/red. Slice ends with the verification recipe in §7.

If you reject or want changes: point at the specific decision in §2 or the specific commit in §3; I revise this document and re-submit.
