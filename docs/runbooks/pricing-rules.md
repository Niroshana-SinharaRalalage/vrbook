# Pricing rules runbook

Short triage guide for owners + support when a pricing rule doesn't
behave the way the owner expects.

## "I added a rule but the quote didn't change"

Walk this list top to bottom. Stop at the first match.

1. **Is `IsEnabled` true?** The PATCH `.../rules/{id}/enabled` toggle is
   independent of the rest of the rule's fields. Look in `/admin/pricing`
   → Enabled column. A draft staged as disabled does nothing until
   toggled on.

2. **Does the quote's date range overlap the rule's window?**
   - `DateRangeOverride`: the night must be inside `[StartDate, EndDate]`
     inclusive. Stays that straddle the window only get adjusted on the
     nights inside it.
   - `LastMinute`: `(Checkin - Today) <= DaysBeforeCheckin`. If the
     quote's check-in is further out than the window, the rule is
     silently skipped.
   - `LengthOfStay`: `MinNights <= Nights` AND (`MaxNights == null` OR
     `Nights <= MaxNights`). Closed range; see SLICE6_PLAN §2.11.

3. **Is another rule overriding it?** Rules apply in **priority
   ascending** order (lower number first). For mixed-kind stacks the
   order matters — see SLICE6_PLAN §2.4. If a later `Override` rule
   replaces the rate, earlier `Multiplier`/`Absolute` work is lost on
   that night.

4. **Is the `RuleApplied` badge showing the wrong rule's name?** The
   badge holds the **first** applied rule's short name (highest
   priority = lowest number). Subsequent rules adjust the amount but
   don't rewrite the badge. If you see `"seasonal"` on a night that
   should also pick up `last_minute`, that's intentional.

5. **Is the rule kind one the engine ignores?** The engine handles
   `DateRangeOverride`, `LastMinute`, `LengthOfStay`. `DayOfWeek` is
   absorbed by the existing weekend uplift; `Base` is the plan's
   `BaseNightlyRate`. Both stay valid enum values but are no-ops in
   Phase 1.

6. **Is the combo on the §2.4.1 reject list?**
   - `LastMinute + Override` → AddRule returns 422 with detail
     `"quote.invalid_rule"`. The UI hides Override in the dropdown for
     these kinds — if you see one in the DB it pre-dates Slice 6.
   - `LengthOfStay + Override` → same.

## "Drag-reorder didn't stick"

`ReorderRules` is last-write-wins per SLICE6_PLAN §2.5 — `row_version`
is intentionally not configured as a concurrency token in Phase 1.
If two owners drag the same plan in different sessions concurrently,
the later save silently overwrites the earlier one. Symptom: your drag
appears to work, then the next reload shows the other owner's order.

Fix: agree on who's editing. For Phase 1 single-tenancy this should
not happen in practice. Revisit when Phase 2 multi-owner lands.

## "The aggregate-vs-raw-SQL escape hatch"

Slice 6 mutators route through `PricingPlan.AddRule(...)` +
`SaveChangesAsync()`. The sibling `UpdatePricingPlanHandler` uses raw
SQL to dodge an EF Core 8 tracking issue documented at
[UpdatePricingPlanHandler.cs:45]. If you see symptoms of stale rule
state after a write — e.g. a fresh `GET /pricing` returning the rule
you just deleted, or a rule disappearing from the list after a
seemingly successful PUT — that's the EF tracking issue biting the
rules path too. Contingency in SLICE6_PLAN §2.9: drop `AddPricingRule`
and `RemovePricingRule` handlers to raw SQL mirroring lines 47-70 of
`UpdatePricingPlanHandler`.

## Manually seeding a rule

If you need a rule on staging without going through the UI (e.g. you
ran the verification recipe without a logged-in admin session), see
[`slice6-seed.md`](slice6-seed.md) for the raw SQL.
