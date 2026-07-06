# Turnover-aware completion walk (OPS.M.16)

Manual smoke test reproducing the 2026-07-04 staging walk that surfaced
the pre-M.16 same-day-booking-during-turnover bug. Run after any change
that touches:
- `BookingRepository.FindOverlapsAsync` or `ListBlockedRangesAsync`
- `PlaceBookingHandler` FOR UPDATE SQL
- `CompletionSweepHandler` (worker cron)
- `Booking.CheckOut` / `CompleteManually` / `ScheduleCompletion`
- `Property.TurnoverHours`
- The admin calendar page (`web/src/app/admin/calendar/page.tsx`)

Related design docs:
- [`OPS_M_16_TURNOVER_AWARE_COMPLETION_PLAN.md`](../OPS_M_16_TURNOVER_AWARE_COMPLETION_PLAN.md)
- [`OPS_M_16_CLOSE_OUT.md`](../OPS_M_16_CLOSE_OUT.md)

---

## Prerequisites

- A staging tenant with at least one property + admin user + one guest
  user + Stripe PaymentIntent that will capture cleanly (or the stub
  configured to no-op on capture).
- The property should have `turnoverHours=24` (M.16 default) or a
  bespoke value the walk is designed to exercise. The walk assumes 24
  in every step unless noted.
- Admin session at `https://<web>/admin` with a tenant_admin
  membership (M.15.4 handler guard requires this).
- Guest session at `https://<web>/` (unauthenticated is fine — placing
  a Tentative booking as a guest requires only email + name).

## 1. Set up a bookable window

Guest places a booking with checkout day = **D**.

1. Guest picks the property → checkin = D-3, checkout = D.
2. Payment intent captured. Booking transitions Tentative → Confirmed
   → CheckedIn → CheckedOut on the operator's normal flow. Reach
   CheckedOut at some point on D-1 or D.
3. Verify `POST /api/v1/bookings/{id}/check-out` succeeded via the
   admin booking-detail page. The booking DTO should now show:
   - `status: "CheckedOut"`
   - `checkedOutAt: <timestamp>`
   - `completionDueAt: <checkedOutAt + 24h>`
   - `turnoverHoursOverride: null` (unless customized in step 5 below)
4. Verify the admin calendar (`/admin/calendar` → pick the property)
   renders the checkout day **D** with the amber "Awaiting turnover"
   chip (M.16 polish). The chip is only present when
   `awaitingTurnover=true` on the `CalendarBookingEntry` DTO.

## 2. Verify the same-day new booking is rejected

Immediately after step 1's CheckedOut transition:

1. As a separate guest, attempt to place a booking checkin = D,
   checkout = D+2 on the same property.
2. Expect **HTTP 422** with `booking.dates_unavailable`. Before M.16
   this was silently accepted (bug the walk originally surfaced).
3. Confirm the M.16.5 predicate: `FindOverlapsAsync` returns the
   CheckedOut booking with a conditional inclusive check on checkout
   day so the new attempt overlaps.

## 3. Verify manual completion releases the block

1. Admin navigates to `/admin/bookings/{id}` (the CheckedOut booking
   from step 1).
2. The "Awaiting turnover" amber panel is visible with:
   - Checked-out timestamp.
   - Scheduled auto-complete timestamp (`completionDueAt`).
   - Numeric input (0–168h) + "Update schedule" button.
   - Primary green "Complete now" button.
3. Click **Complete now**. Expect `POST /api/v1/bookings/{id}/complete`
   → 200 with `status: "Completed"`, `completionDueAt: null`,
   `awaitingTurnover: false`.
4. Retry the step-2 guest booking (checkin = D, checkout = D+2) —
   expect **HTTP 201 Created**. The turnover-day block is released.
5. Refresh the admin calendar — the amber "Awaiting turnover" chip
   on day D has disappeared.

## 4. Verify per-booking schedule override

Reset by placing a fresh Tentative → CheckedOut booking on the same
property with checkout = D'.

1. Admin navigates to `/admin/bookings/{id'}` (the new CheckedOut
   booking).
2. Enter `12` in the "Reschedule turnover (hours)" input → click
   "Update schedule".
3. Expect `POST /api/v1/bookings/{id'}/schedule-completion` with
   body `{"hoursFromCheckedOutAt": 12}` → 200.
4. `completionDueAt` on the response is `checkedOutAt + 12h`.
5. Wait for the `booking-completion` cron worker to run (or trigger
   manually via `az containerapp job start -n cj-vrbook-booking-
   completion-staging -g rg-vrbook-staging`).
6. Post-worker: booking status is `Completed`,
   `awaitingTurnover=false`, DueAt cleared.

## 5. Verify property-level turnover default

Admin edits the property's `turnoverHours` value from 24 to 6.

1. Navigate to `/admin/properties/{id}` → find the "Booking rules"
   section → change `Turnover hours` from 24 to 6 → Save.
2. Verify `PUT /api/v1/admin/properties/{id}` returns 200 with
   `turnoverHours: 6`.
3. Place + progress a fresh booking to CheckedOut. The new booking's
   `completionDueAt` should be `checkedOutAt + 6h` (property-level
   default), not `checkedOutAt + 24h`.

## 6. Verify integer bounds

- `POST /schedule-completion` with `hoursFromCheckedOutAt: -1` → 422
  `booking.hours_out_of_range`.
- Same with `hoursFromCheckedOutAt: 169` → 422 (168h = 1 week ceiling).
- `PUT /admin/properties/{id}` with `turnoverHours: -1` → 422.
- Same with `turnoverHours: 200` → 422.

## Regression signals to watch during the walk

- Admin calendar for a CheckedOut booking whose `completionDueAt` is in
  the past MUST NOT show the amber chip. If it does, the backend
  `awaitingTurnover` flag is stale — the sweep worker didn't run OR
  the DTO doesn't respect the flip.
- A NEW booking accepted checkin = D, checkout > D on a property whose
  D-1..D-3 CheckedOut booking is not yet Completed = the M.16.5
  overlap predicate regressed. `FindOverlapsAsync` + `PlaceBookingHandler`
  FOR UPDATE SQL are the two surfaces to inspect.
- A NEW booking rejected 422 on checkin = D+1 when the D-checkin
  booking is Completed already = false positive. The sweep should have
  cleared `completionDueAt`; check the worker logs.
- `POST /complete` returns 422 `booking.state_transition` when the
  booking is not in CheckedOut = correct rejection; not a regression.

## Post-walk: capture findings

Append walk outcome to the OPS.M.16 close-out §6 (or a new dated
section if the walk introduces a fix). Findings should include:

- Which steps passed/failed with observed HTTP status codes.
- Screenshots of the admin calendar for D showing (or not showing) the
  amber chip.
- Any diagnostic KQL query results if the backend behaved unexpectedly.

Related runbooks:
- [`booking-sla-worker-silent.md`](./booking-sla-worker-silent.md) — the sweep
  worker's silent-failure runbook.
