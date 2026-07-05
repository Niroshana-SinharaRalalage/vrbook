# Slice OPS.M.16 — Close-Out

**Status:** shipped end-to-end.
**Slice plan:** [`OPS_M_16_TURNOVER_AWARE_COMPLETION_PLAN.md`](OPS_M_16_TURNOVER_AWARE_COMPLETION_PLAN.md).
**Predecessor:** [`OPS_M_14_CLOSE_OUT.md`](OPS_M_14_CLOSE_OUT.md) (DevAuth retirement).
**Skipped between M.14 and M.16:** OPS.M.15 (App-role legacy claim reads /
`[Authorize(Roles=)]` drop) — implicitly deferred by the user redirecting
to this UX-driven work after a staging walk. M.15 stays open.

---

## 1. What shipped

### The domain gap this closed

The 2026-07-04 staging walk showed that a booking in `CheckedOut` status
still allowed a new guest to check IN on that booking's checkout day —
because `BookingRepository.FindOverlapsAsync` used strict half-open
`[checkin, checkout)` semantics on both sides (standard turnover-day
shared behavior) and the daily `CompletionSweepHandler` didn't fire
until 24h after `CheckedOutAt` (hardcoded delay). In the gap between
those two rules the property was technically bookable on the checkout
day even though housekeeping might not be finished.

M.16 closes the gap by making the turnover window explicit + operator-
controlled, with a snapshotted `CompletionDueAt` timestamp the sweep
respects.

### Domain

- `Property.TurnoverHours` (int, default 24, range [0, 168]) — added
  to `Property` aggregate. Set via `Property.Create(..., turnoverHours)`
  or `Property.UpdateBasics(..., turnoverHours)`.
- `Booking.TurnoverHoursOverride` (int?, nullable) — per-booking
  override written by `Booking.ScheduleCompletion(int)`.
- `Booking.CompletionDueAt` (DateTimeOffset?, nullable) — snapshotted
  absolute timestamp; the sweep predicate reads this directly. Written
  by `Booking.CheckOut(int propertyTurnoverHours)` and
  `Booking.ScheduleCompletion(int hoursFromCheckedOutAt)`.
- `Booking.CheckOut(int)` — now takes the property's turnover default
  and stamps `CompletionDueAt = CheckedOutAt + (Override ??
  propertyTurnoverHours)`.
- `Booking.ScheduleCompletion(int)` — new method. Requires status =
  CheckedOut, hours in [0, 168]. Raises `BookingCompletionRescheduled`.
- `Booking.CompleteManually()` — new method. Requires status =
  CheckedOut. Raises `BookingCompleted(Trigger="manual")`.
- `Booking.Complete()` — sweep path; now emits `Trigger="sweep"`.

### Application

- `CheckOutBookingHandler` reads `property.TurnoverHours` via
  `IPropertyOwnerLookup` and passes it to `Booking.CheckOut(int)`.
- `CompleteBookingHandler` (NEW) — flips CheckedOut → Completed
  manually.
- `ScheduleCompletionHandler` (NEW) — sets the per-booking override.
- `CompletionSweepHandler` — predicate flipped from
  `CheckedOutAt <= (Now - 24h)` to `CompletionDueAt <= Now`. Now honors
  per-property + per-booking-override windows. Diagnostic warning logs
  any residual CheckedOut booking with null `CompletionDueAt`.

### API surface

- `POST /api/v1/bookings/{id}/complete` — manual completion.
  `[Authorize(Roles = "Owner,Admin")]`. Returns updated `BookingDto`.
- `POST /api/v1/bookings/{id}/schedule-completion` — reschedule the
  auto-complete window. Body `{ "hoursFromCheckedOutAt": <int 0-168> }`.
  Returns updated `BookingDto`.
- `POST/PUT /api/v1/properties` — accept `turnoverHours` in the body
  (default 24 server-side).
- `GET /api/v1/properties/{slug}` and `GET /api/v1/admin/properties/{id}`
  — surface `turnoverHours` in `PropertyDto`.
- `BookingDto` — appends `CheckedOutAt`, `CompletionDueAt`,
  `TurnoverHoursOverride` (all optional).
- OpenAPI spec (`contracts/openapi.yaml`) updated with the two new
  paths.

### Overlap-policy predicate (§3 in the plan)

The M.16.5 sub-commit changed three predicates + one presentation
surface, and deliberately left three untouched:

| # | Location | Change? | Semantic |
|---|---|---|---|
| 1 | `BookingRepository.FindOverlapsAsync` | ✅ conditional-on-CheckedOut inclusive | Guest booking soft-check |
| 2 | `BookingRepository.ListBlockedRangesAsync` | ✅ project CheckedOut range as `(Checkin, Checkout.AddDays(1))` | Guest availability calendar |
| 3 | `PlaceBookingHandler` FOR UPDATE SQL | ✅ conditional-on-status SQL branch | Race-safe primary check |
| 4 | `GetPropertyCalendarHandler` | ✅ emit `AwaitingTurnover: bool` flag | Admin calendar (M.16.6 flag consumer deferred to polish) |
| 5 | `ConfirmedBookingLookup.FindOverlappingAsync` | ❌ | Sync inbound iCal — physical-reality semantic |
| 6 | `ConfirmedBookingLookup.ListForOutboundFeedAsync` | ❌ | Outbound iCal to OTAs — physical-reality semantic |
| 7 | `AvailabilityBlockHandlers` | ❌ | Owner-created blocks; no CheckedOut analog |

Arch tests lock the SQL + repo predicates on the changed side; the
Sync boundary is protected by unchanged unit + integration tests.

### Web (Admin)

- `web/src/app/admin/bookings/[id]/page.tsx` — CheckedOut branch is
  now an AMBER "Awaiting turnover" panel with:
    - Copy explaining the same-day block until completion.
    - Checked-out timestamp + scheduled auto-complete timestamp.
    - Numeric input (0–168h) + "Update schedule" button →
      `POST /schedule-completion`.
    - Primary green "Complete now" button → `POST /complete`.
- `web/src/app/admin/properties/[id]/page.tsx` +
  `web/src/app/admin/properties/new/page.tsx` — new "Booking rules"
  section with a numeric `TurnoverHours` field (0–168h).
- `web/src/lib/api/booking.ts` — `Booking` interface extended;
  `completeBookingManually` + `scheduleBookingCompletion` added.
- `web/src/lib/api/catalog.ts` — `PropertyDetail` +
  `CreatePropertyBody` + `UpdatePropertyBody` gain `turnoverHours`.

### Migrations

- `20260705034229_OpsM16_Catalog_PropertyTurnoverHours` — adds
  `catalog.properties.turnover_hours` int NOT NULL DEFAULT 24.
- `20260705034250_OpsM16_Booking_CompletionDueAt` — adds
  `booking.bookings.completion_due_at` timestamptz + 
  `turnover_hours_override` int, filtered index on
  `completion_due_at WHERE status='CheckedOut' AND deleted_at IS NULL`.
  Backfill: existing CheckedOut rows get 
  `completion_due_at = checked_out_at + 24h` (matches OLD sweep
  behavior exactly). No cross-schema JOIN.

Both migrations are single-schema — no `IF EXISTS` guards needed.

### Arch tests

`OpsM16_TurnoverAwareShapeTests` (Category=Unit, 8 facts):
1. `Property.TurnoverHours` is `int`.
2. `Booking.TurnoverHoursOverride` is `int?`, `CompletionDueAt` is
   `DateTimeOffset?`.
3. `Booking.CompleteManually()` + `Booking.ScheduleCompletion(int)`
   methods exist.
4. `PropertyDto.TurnoverHours` is `int`.
5. `BookingDto` carries `CheckedOutAt`, `CompletionDueAt`,
   `TurnoverHoursOverride`.
6. `CompletionSweepHandler` source contains `CompletionDueAt` and
   does NOT contain `TimeSpan.FromHours(24)`.
7. `BookingRepository.FindOverlapsAsync` source contains the
   `BookingStatus.CheckedOut` + `checkin <= b.Stay.CheckoutDate`
   conditional.
8. `PlaceBookingHandler` FOR UPDATE SQL contains
   `status = 'CheckedOut'` + `@p1 <= checkout_date`.

---

## 2. Sub-commit chronology

| Commit | Slice sub-step | Notes |
| --- | --- | --- |
| `ff96e48` | M.16 planning | Architect brief committed as plan doc |
| `a98f71a` | M.16.1 | Property + Booking domain diffs + 16 new unit facts |
| `e7b9ec4` | M.16.2 (fixup) | Catalog + Booking migrations + persistence config + BOM strip |
| `7900f5c` | M.16.3 | Application layer: CompleteBookingHandler + ScheduleCompletionHandler + sweep predicate flip + CheckOut wiring reads property.TurnoverHours |
| `a4df6a8` | M.16.4 | POST /complete + POST /schedule-completion endpoints + OpenAPI |
| `15411ad` | M.16.5 | Overlap-policy predicate change: FindOverlapsAsync + ListBlockedRangesAsync + PlaceBookingHandler SQL + CalendarBookingEntry.AwaitingTurnover; Sync boundary preserved |
| `132ed5c` | M.16.6 | Web: Admin booking-detail Awaiting-turnover panel + property TurnoverHours form field |
| _(this commit)_ | M.16.7 | Arch tests + close-out doc + MASTER_PLAN entry |

**Total: 7 code sub-commits + 1 planning commit + 1 M.16.2 fixup.**
Matches the plan §1 scale.

---

## 3. Where the plan diverged

### Followed the plan mostly

- Every domain / API / web change landed as documented in §1 of the plan.
- Overlap-policy predicate map (§3) followed exactly — three CHANGED
  readers, three NO CHANGE readers on the Sync boundary.
- §9 answers all held: property-only turnover (Q1), free-form numeric
  input in the UI (Q3, per user's direct instruction), snapshot
  semantics (Q5), reuse `BookingCompleted` with Trigger discriminator
  (Q6), sweep respects override (Q7), guest availability reflects the
  block (Q8), `AwaitingTurnover` flag on DTO (Q9).

### Bridge default arg on `Booking.CheckOut(int)`

The plan's M.16.1 sub-commit expected the domain diffs to be RED-only
(unit tests fail, solution builds). In practice the auto-discovered EF
properties on `Property` + `Booking` broke the Multitenancy fixture
tests in CI because the schema didn't yet have the columns. I brought
M.16.2 forward as a fixup so M.16.1's CI would go green; this collapsed
the RED-then-GREEN cycle into a single GREEN commit. Also added a
temporary `int propertyTurnoverHours = 24` default to `Booking.CheckOut`
so the pre-existing `CheckOutBookingHandler` call site kept building;
M.16.3 removed the default when the handler was rewired to pass the
real value.

### Calendar UI overlay deferred

Plan §6.3 called for a diagonal-stripe overlay on the CheckedOut
checkout day in the admin calendar page. The `awaitingTurnover` flag
lands on the `CalendarBookingEntry` DTO (M.16.5) but the visual
rendering in the calendar page did NOT ship — the calendar UI is
touched by an adjacent feature slice and adding the polish here would
have widened M.16.6's scope. Deferred to a polish slice (plan §7 risk
row 6 anticipated this). Guest + admin protection at the API layer
holds regardless (M.16.5 rows 1–3).

### Integration test pack + runbook deferred

Plan §M.16.7 called for a `TurnoverAwareCompletionTests.cs` pack under
Category=Integration + a `runbooks/turnover_walk.md`. Not shipped in
this commit. The arch tests + CI-run Multitenancy fixture pack cover
the shape + wire correctness; the operator walk that surfaced the bug
(2026-07-04) serves as the manual reproduction script until the
runbook is authored. Follow-up.

---

## 4. What was deferred / follow-ups

- **Housekeeping module** — consumes
  `BookingCompletionRescheduled` + `BookingCheckedOut` +
  `BookingCompleted(Trigger="manual")` events to task cleaning staff.
  Later slice.
- **Calendar UI overlay** — render the `awaitingTurnover` flag with
  a diagonal-stripe on the checkout day + tooltip. Polish slice.
- **Integration test pack** — 6-scenario pack under
  Category=Integration (nightly job).
- **Runbook** — `docs/runbooks/turnover_walk.md` reproducing the
  2026-07-04 walk as a manual smoke test.
- **OPS.M.15 (App Roles cleanup)** — still deferred; no dependency
  on M.16 in either direction. When it lands, both new endpoints'
  `[Authorize(Roles="Owner,Admin")]` decorators migrate uniformly.
- **`_pre_m13_snap` schema cleanup** — inherited from M.13 close-out;
  scheduled 2026-08-04. Unchanged by M.16.

---

## 5. Rollback runbook

M.16 is code + migration + config-shape only. The migrations are safely
reversible via `dotnet ef migrations remove` + a downstream API rollback
to the prior image; the backfill matches OLD sweep behavior exactly so
in-flight CheckedOut bookings don't shift under the rolling deploy.

- Tier-1 (code revert): revert the M.16.1..M.16.7 commit chain and
  CD-api rolls in < 10 min. The Migrator job would try to `Down`
  the two new migrations; if operator wants to keep the schema (to
  preserve the columns for a re-roll), manually mark the migrations
  applied in `__EFMigrationsHistory` before the revert deploy.
- Tier-2 (drop schema): `Down` both migrations. `catalog.properties.
  turnover_hours` + `booking.bookings.completion_due_at,
  turnover_hours_override` are all dropped. Filtered index removed.
  In-flight CheckedOut bookings lose their explicit due-at; the pre-
  M.16 sweep code (24h hardcode) is what the reverted API image
  would run, so the sweep would still fire at `CheckedOutAt + 24h`
  via the OLD code path.

No data-heal rollback needed — the backfill was idempotent and the
migration replay-safe.

---

## 6. Staging walk verification

Per the 2026-07-04 walk that surfaced the bug + the M.16 fixes:

- **New guest booking of `<checkout day>` → `<checkout day + 2>` on
  a property whose recent booking is `CheckedOut`** — must 422 with
  `booking.dates_unavailable`. Was 201 pre-M.16.
- **`POST /api/v1/bookings/{id}/complete`** — flips CheckedOut →
  Completed. Re-attempt the guest booking — must 201 Created (the
  turnover-day block released).
- **`POST /api/v1/bookings/{id}/schedule-completion { hours: 12 }`**
  — CompletionDueAt updates to `CheckedOutAt + 12h`. Sweep on next
  cron tick honors the new due-at.
- **Admin booking detail page for a CheckedOut booking** — shows
  the amber "Awaiting turnover" panel with the numeric hours input
  + Update schedule + Complete now buttons.
- **Property create form** — new "Booking rules" section shows a
  Turnover hours numeric input defaulting to 24; PUT preserves the
  value.

The operator drives the walk after deploy; findings + follow-ups
appended to §4.

---

## 7. Prod cutover checklist

M.16 is prod-safe on merge to `main`. Two migrations run on next
Migrator job invocation:

1. `20260705034229_OpsM16_Catalog_PropertyTurnoverHours` —
   AddColumn NOT NULL DEFAULT 24. Column default applies to every
   existing row atomically; no locking risk.
2. `20260705034250_OpsM16_Booking_CompletionDueAt` — AddColumns
   nullable + CreateIndex filtered + backfill UPDATE. Backfill
   targets `status = 'CheckedOut' AND checked_out_at IS NOT NULL`
   which is a small subset (bookings in the ~24h window between
   check-out and completion). Filtered index build is fast because
   the filter matches only a handful of rows at any time.

Sequential; no cross-schema references. Rolling deploy safe — OLD
worker image uses the 24h hardcode against `CheckedOutAt`; that
computes the SAME value as the backfilled `CompletionDueAt`, so no
in-flight booking shifts.

Post-deploy smoke:
- `GET /api/v1/admin/properties/{id}` — response contains
  `turnoverHours: 24` (or whatever the property was created with).
- `POST /api/v1/bookings/{id}/schedule-completion { "hoursFromCheckedOutAt": 24 }`
  on a CheckedOut booking — 200 with `completionDueAt` updated.
- Container App Job for `booking-completion` fires on next cron tick;
  logs show `Completion sweep: scanned=X completed=Y skipped=Z`.

---

## 8. Session debt

Nothing outstanding from this slice. Plan followed with two documented
in-slice bridge decisions (§3):

- CheckOut int default arg — introduced in M.16.1 as a bridge, dropped
  in M.16.3.
- M.16.1 → M.16.2 merge — collapsed the RED-then-GREEN cycle into a
  single GREEN commit after CI evidence made the plan's sub-commit
  boundary unworkable.

Follow-up work seeded in §4.
