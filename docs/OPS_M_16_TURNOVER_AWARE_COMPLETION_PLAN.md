# Slice OPS.M.16 — Turnover-aware completion + configurable turnover window

- **Status:** DRAFT for user review. Do NOT execute until §9 questions are locked.
- **Date:** 2026-07-04.
- **Author (role):** Platform Enterprise Architect.
- **Predecessors:** [`OPS_M_14_CLOSE_OUT.md`](./OPS_M_14_CLOSE_OUT.md) (shipping
  baseline; DevAuth retired, JwtBearer-only auth surface, mock test handler
  wired).
- **Skipped between M.14 and M.16:** the M.15 "App-roles legacy claim reads +
  `[Authorize(Roles=)]` drop" slice was implicitly deferred by the user
  redirecting to this UX-driven work after a staging walk. Nothing in M.16
  fights that deferral — every new endpoint here reuses the existing
  `[Authorize(Roles="Owner,Admin")]` decorator pattern, so the eventual
  M.15 sweep will pick them up alongside the rest.
- **Scope:** ONE vertical slice. Closes the domain-model gap between
  `CheckedOut` and `Completed` (the "turnover day" the daily sweep glosses
  over), and puts turnover duration under the tenant admin's control at
  both the property-default level and the per-booking override level.
- **Explicitly NOT in this slice:**
  - `[Authorize(Roles=)]` decorator drop — **OPS.M.15** (still deferred).
  - Social IdPs — **OPS.M.12**.
  - `_pre_m13_snap` schema cleanup — scheduled 2026-08-04 follow-up.
  - Housekeeping module (task assignment, damage-check workflow) —
    referenced in §7 as future work; **NOT** modelled here.
  - Calendar UI "awaiting turnover" pill visual redesign — the pill lands
    in this slice, but any deeper redesign of the admin calendar bar
    rendering is deferred to a polish slice.
  - Auto-scheduling based on housekeeping ready-signal from third-party
    integration — deferred.

---

## §0 What we're doing + why now

### §0.1 The staging-walk trigger

On 2026-07-04 the operator opened `/admin/bookings/<beach-villa-id>` for a
`CheckedOut` booking (2026-08-01 → 2026-08-03; check-out clicked ~90 minutes
prior). They then tried to place a new guest booking for
2026-08-03 → 2026-08-05 against the same property. It was **allowed**.

Two things are true here and both are technically correct in isolation:

- `BookingRepository.FindOverlapsAsync` uses half-open `[checkin, checkout)`
  semantics on both sides (`b.Stay.CheckinDate < checkout && checkin <
  b.Stay.CheckoutDate`) — the standard vacation-rental turnover-day-shared
  rule. A new stay's check-in date equal to an existing stay's check-out
  date is legal, because the incoming guest arrives after the outgoing
  guest leaves that same day.
- The `CompletionSweepHandler` (see `src/Modules/VrBook.Modules.Booking/
  Application/Commands/CompletionSweepCommand.cs:37`) fires once per day at
  06:00 UTC and only touches bookings whose `CheckedOutAt` is at least 24
  hours old. So a booking checked out at 15:00 today doesn't flip to
  `Completed` until tomorrow's sweep at earliest.

Composed, these two rules produce a **domain gap**: between the moment
check-out is clicked and the sweep firing, the property is in a state the
platform models as "still bookable on the same turnover day" but which the
operator experiences as "housekeeping in progress; damage check not done;
I don't know yet whether the incoming guest can arrive." Sharing the
turnover day here is the risk the walk surfaced.

### §0.2 What we're shipping

Three connected changes, one slice:

1. **Overlap-policy tightening while a booking is `CheckedOut`.** New
   incoming guests can't check in on `CheckedOutAt`'s stay's check-out
   date. Day-after-checkout onward stays fully bookable. As soon as the
   booking flips to `Completed` (sweep- or manually-triggered), the
   standard shared-turnover-day semantic returns and the same date pair
   becomes bookable again.

2. **Manual "Complete stay" button + "Schedule completion" dropdown in the
   admin booking detail's Stay-lifecycle panel** (only visible when
   `status = CheckedOut`). "Complete now" flips CheckedOut → Completed
   immediately, releasing the turnover-day block. "Schedule" writes a
   `CompletionDueAt` timestamp the sweep respects.

3. **Configurable turnover duration.** Property-level default
   `turnoverHours` (owner picks per property; system default of 24 when
   unset). Per-booking override written at check-out time or after
   ("push to 48h because damage report is delayed"). Sweep uses the
   snapshotted `CompletionDueAt` on the booking row, so property config
   changes mid-stay never surprise the operator.

### §0.3 Why now

The operator surfaced this on a staging walk; the shape of the fix
(**snapshot the due-at at check-out time**) touches enough surfaces
— domain, migration, worker, API, web — that patching only the overlap
predicate would leave the underlying "what is the turnover window and who
controls it" question unanswered. Ship it as one slice so the operator's
mental model ("I control when the property becomes available again")
matches the code end-to-end.

The gap has been latent since Slice 5 shipped in Phase-1; it did not
surface until now because turnover-day-adjacent bookings were rare on
staging. Any additional operator walking on staging in the next weeks
will re-trip this if it's not fixed.

### §0.4 What we're NOT doing

- Modelling housekeeping tasks / damage reports as their own aggregate.
  The plan below deliberately keeps `CompletionDueAt` as a simple
  timestamp; the housekeeping module (later slice) will hang off
  `BookingCompleted` and the newly-published `TurnoverScheduled` event
  without needing to reshape this data.
- Tenant-level default turnover (as opposed to property-level). See §2
  for the tradeoff — property-only is picked; §9-Q1 confirms.
- Auto-blocking day+1 after `CheckedOut` (i.e. always block the turnover
  day AND the following day). The walk did not ask for this; it's a
  policy choice the property owner can express with a `turnoverHours=48`
  default if they need it.

---

## §1 What ships in each sub-commit

Sub-commit convention: `Slice OPS.M.16.N: <summary>`. All commits target
`develop`. Each ends CI-green under the standard filter
`dotnet test --filter "Category!=Integration"`. TDD RED → GREEN per
sub-commit where the deletion-plus-test pattern doesn't collapse them.

Sequence:

```
M.16.1 — RED. Domain aggregate changes for Property + Booking (unit tests only).
M.16.2 — GREEN. Two migrations (Catalog turnover_hours, Booking completion_due_at + turnover_hours_override).
M.16.3 — GREEN. Application layer — override commands + CompletionSweep predicate change + CheckOut side-effect wiring.
M.16.4 — GREEN. API surface — new endpoints on BookingsController; Property CRUD form field.
M.16.5 — GREEN. Overlap-policy predicate change across every reader ("blocks this date range").
M.16.6 — GREEN. Web — admin booking detail Stay-lifecycle panel; property forms; api client + hooks.
M.16.7 — GREEN. Close-out — arch tests, MASTER_PLAN entry, integration test scenario, staging validation runbook.
```

### M.16.1 — RED. Domain aggregate changes for Property + Booking (unit tests only).

Files touched (production; DOMAIN ONLY — no persistence yet):

- `src/Modules/VrBook.Modules.Catalog/Domain/Property.cs`
  - ADD `public int TurnoverHours { get; private set; }` (default value
    assigned in `Create` from a new optional arg; existing rows migrated
    to 24 by M.16.2).
  - EXTEND `Property.Create(...)` to accept `int turnoverHours = 24`.
    Validate `>= 0 && <= 168` (i.e. one week upper bound — see §9-Q2).
    Ordering: appended to end of signature; no arg-swap. Only the
    Catalog `CreatePropertyHandler` calls this, and its call site is
    updated below.
  - EXTEND `Property.UpdateBasics(...)` with the same
    `int turnoverHours = 24` optional. Same validation. Named-argument
    call site in `UpdatePropertyHandler` — see M.16.3.
  - NO new domain event. `PropertyUpdated` already fires from
    `UpdateBasics`; the turnover-hours change piggybacks on it.

- `src/Modules/VrBook.Modules.Booking/Domain/Booking.cs`
  - ADD `public int? TurnoverHoursOverride { get; private set; }` —
    nullable snapshot of the override, or null when no override set.
  - ADD `public DateTimeOffset? CompletionDueAt { get; private set; }` —
    the snapshotted due-at the sweep reads. Set at CheckOut time.
  - MODIFY `CheckOut()` to accept `int propertyTurnoverHours` — no
    default; caller passes it. Compute:
    - `CheckedOutAt = DateTimeOffset.UtcNow`
    - `CompletionDueAt = CheckedOutAt.Value + TimeSpan.FromHours(
      TurnoverHoursOverride ?? propertyTurnoverHours)`. On first
      CheckOut, `TurnoverHoursOverride` is null → uses property default.
    Rationale: snapshot approach (§4). CheckOut is where the clock
    starts; snapshot the effective duration there so future property
    edits or override changes flow through explicit domain methods.
  - ADD `public void ScheduleCompletion(int hoursFromCheckedOutAt)`.
    Invariants:
    - Requires `Status == CheckedOut` (throws
      `BusinessRuleViolationException` "booking.state" otherwise).
    - Requires `hoursFromCheckedOutAt >= 0` and
      `hoursFromCheckedOutAt <= 168` (7 days — see §9-Q2).
    - Sets `TurnoverHoursOverride = hoursFromCheckedOutAt`, recomputes
      `CompletionDueAt = CheckedOutAt.Value + TimeSpan.FromHours(...)`.
    - Raises new `BookingCompletionRescheduled(bookingId, dueAt,
      hoursFromCheckedOutAt, TenantId)` event.
  - ADD `public void CompleteManually()`. Distinguishes admin manual
    completion from sweep-triggered completion for observability +
    downstream idempotency logging.
    - Requires `Status == CheckedOut`.
    - Same body as `Complete()` but raises a distinguishable event
      shape — see §2.4 for the decision to reuse `BookingCompleted` +
      a "trigger" field, OR add a separate `BookingManuallyCompleted`.
      Recommendation: reuse `BookingCompleted` with a new
      `string Trigger` field ("sweep" | "manual"), matching how
      `BookingConfirmed(...)` already carries a trigger. Locked in §9-Q6.
  - No change to `Complete()` — sweep still calls it; keep the shape
    stable. Adds a comment noting the manual path is `CompleteManually()`.

- `src/VrBook.Contracts/Events/BookingEvents.cs`
  - ADD `public sealed record BookingCompletionRescheduled(Guid BookingId,
    DateTimeOffset DueAt, int HoursFromCheckedOutAt, Guid TenantId) :
    IntegrationEvent;`
  - MODIFY `BookingCompleted` to carry `string Trigger` (see above).
    Wire-format change; existing consumers (Loyalty stay-count,
    Notifications thanks-for-staying + review request) don't read
    Trigger — they'll pattern-match on the new field's default when
    replaying old outbox rows. Backward-compat handled in §7 risk table.

Tests moved to RED (unit only):

- `tests/VrBook.Domain.Tests/Catalog/PropertyTurnoverHoursTests.cs` (NEW):
  - `Create_default_turnoverHours_is_24`.
  - `Create_rejects_negative_turnoverHours`.
  - `Create_rejects_turnoverHours_over_upper_bound`.
  - `UpdateBasics_updates_turnoverHours`.
- `tests/VrBook.Domain.Tests/Booking/BookingCheckOutTests.cs` (NEW or
  EXTEND):
  - `CheckOut_stamps_CompletionDueAt_using_property_default`.
  - `CheckOut_stamps_CompletionDueAt_using_override_when_present`.
  - `CheckOut_rejects_when_Status_is_not_CheckedIn` (regression).
- `tests/VrBook.Domain.Tests/Booking/BookingScheduleCompletionTests.cs`
  (NEW):
  - `ScheduleCompletion_requires_CheckedOut_state`.
  - `ScheduleCompletion_rejects_negative_hours`.
  - `ScheduleCompletion_rejects_hours_over_upper_bound`.
  - `ScheduleCompletion_recomputes_CompletionDueAt_from_CheckedOutAt`.
  - `ScheduleCompletion_raises_BookingCompletionRescheduled_event`.
- `tests/VrBook.Domain.Tests/Booking/BookingCompleteManuallyTests.cs`
  (NEW):
  - `CompleteManually_requires_CheckedOut_state`.
  - `CompleteManually_raises_BookingCompleted_with_Trigger_manual`.
  - `CompleteExisting_raises_BookingCompleted_with_Trigger_sweep` (guard the
    sweep path's contract).

CI expectation: **RED**. Domain tests compile against new members;
persistence + application layer not yet updated so full solution still
builds. Any lingering `Complete()` callers that were passing no args
still work; new `CheckOut(int)` callers need the arg — the two callers
today are `CheckOutBookingHandler` (updated in M.16.3) and one
`TransitionHandlersTests` fixture (updated in M.16.3). Solution builds;
new domain unit tests fail on assertions.

Local validation: `dotnet build`, then `dotnet test tests/VrBook.Domain.Tests`.

### M.16.2 — GREEN. Two migrations (Catalog + Booking).

Files touched:

- `src/Modules/VrBook.Modules.Catalog/Infrastructure/Persistence/PropertyConfiguration.cs`
  - ADD `b.Property(p => p.TurnoverHours).HasColumnName(
    "turnover_hours").HasDefaultValue(24).IsRequired();`
- `src/Modules/VrBook.Modules.Catalog/Infrastructure/Persistence/Migrations/
  {YYYYMMDDHHMMSS}_OpsM16_Catalog_PropertyTurnoverHours.cs` (NEW)
  - `AddColumn<int>(name: "turnover_hours", schema: "catalog", table:
    "properties", nullable: false, defaultValue: 24)`.
  - Existing rows get 24 via the column default; no explicit backfill
    UPDATE needed. NULL not possible — column is `NOT NULL DEFAULT 24`.
    Locks §9-Q4 answer "24h default; no NULL, no per-migration UPDATE."
  - `.Designer.cs` companion regenerated by `dotnet ef migrations add`.
  - Down: `DropColumn`.
  - NO cross-schema references. Migration is single-table single-schema;
    no `IF EXISTS` guard needed.

- `src/Modules/VrBook.Modules.Booking/Infrastructure/Persistence/BookingConfiguration.cs`
  - ADD `b.Property(x => x.TurnoverHoursOverride).HasColumnName(
    "turnover_hours_override").IsRequired(false);`
  - ADD `b.Property(x => x.CompletionDueAt).HasColumnName(
    "completion_due_at").IsRequired(false);`
  - ADD `b.HasIndex(x => x.CompletionDueAt).HasDatabaseName(
    "ix_bookings_completion_due_at").HasFilter(
    "status = 'CheckedOut' AND deleted_at IS NULL");`
    Filtered index because the sweep predicate only ever reads
    `Status = CheckedOut`; filter keeps the index tiny.
- `src/Modules/VrBook.Modules.Booking/Infrastructure/Persistence/Migrations/
  {YYYYMMDDHHMMSS}_OpsM16_Booking_CompletionDueAt.cs` (NEW)
  - `AddColumn<int?>(name: "turnover_hours_override", schema: "booking",
    table: "bookings", nullable: true)`.
  - `AddColumn<DateTimeOffset?>(name: "completion_due_at", schema:
    "booking", table: "bookings", nullable: true)`.
  - **BACKFILL** for existing `CheckedOut` rows: compute
    `completion_due_at = checked_out_at + interval '24 hours'` for every
    row where `status = 'CheckedOut' AND completion_due_at IS NULL AND
    checked_out_at IS NOT NULL`. Half-baked M.13 close-out lesson: cross-
    schema updates need `IF EXISTS` guards.
    - The UPDATE stays within `booking.bookings` — same schema, so no
      `IF EXISTS` guard needed. However the SQL block itself is guarded
      with `EXECUTE 'UPDATE ...'` inside a DO block so the migration is
      replay-safe if the column already exists from a partial prior run.
    - No JOIN to `catalog.properties` — we deliberately backfill with the
      **system default of 24h**, not the property-specific default. Any
      per-property override the tenant admin later sets applies only to
      NEW check-outs. Justification: (a) matches previous
      `CompletionDelay = TimeSpan.FromHours(24)` behavior exactly, so no
      row's due-at shifts under an in-flight sweep window; (b) avoids a
      cross-schema UPDATE that would otherwise need the `IF EXISTS` trap
      guard; (c) admin can `POST /schedule-completion` on any
      still-CheckedOut booking to move its due-at forward or back.
  - `CREATE INDEX CONCURRENTLY` is not used (EF ef migrations don't
    author CONCURRENTLY by default; index build is fast in practice
    given the filter + expected row count).
  - Down: `DropIndex` + `DropColumn` × 2.
- `src/Modules/VrBook.Modules.Booking/Infrastructure/Persistence/Migrations/
  BookingDbContextModelSnapshot.cs` (regenerated).
- `src/Modules/VrBook.Modules.Catalog/Infrastructure/Persistence/Migrations/
  CatalogDbContextModelSnapshot.cs` (regenerated).

Tests moved to GREEN:

- The domain unit tests from M.16.1 stay green (RED only failed due to
  missing production wiring; M.16.1's production diff already stubs the
  fields — this commit adds persistence). No new persistence-shape arch
  tests here — the `SchemaShapeTests` in `tests/VrBook.Architecture.Tests`
  don't yet cover these columns; M.16.7 adds the arch tests.

CI expectation: **GREEN**. `dotnet test` passes end-to-end (unit +
architecture; integration filtered out per the convention). Migration
applied against local Postgres via `dotnet ef database update` — see
Local validation.

Local validation:
```
dotnet ef database update --project src/Modules/VrBook.Modules.Catalog/VrBook.Modules.Catalog.csproj --startup-project src/VrBook.Api
dotnet ef database update --project src/Modules/VrBook.Modules.Booking/VrBook.Modules.Booking.csproj --startup-project src/VrBook.Api
psql -c "SELECT column_name, data_type, column_default FROM information_schema.columns WHERE table_schema='catalog' AND table_name='properties' AND column_name='turnover_hours';"
psql -c "SELECT column_name, data_type, is_nullable FROM information_schema.columns WHERE table_schema='booking' AND table_name='bookings' AND column_name IN ('turnover_hours_override','completion_due_at');"
psql -c "SELECT COUNT(*) FROM booking.bookings WHERE status='CheckedOut' AND completion_due_at IS NOT NULL;"
```

### M.16.3 — GREEN. Application layer — override commands + sweep predicate + CheckOut wiring.

Files touched (production):

- `src/Modules/VrBook.Modules.Booking/Application/Commands/TransitionCommands.cs`
  - ADD `public sealed record CompleteBookingCommand(Guid Id, Guid TenantId)
    : IRequest<BookingDto>, ITenantScoped;` — manual completion by admin.
  - ADD `public sealed record ScheduleCompletionCommand(Guid Id, int
    HoursFromCheckedOutAt, Guid TenantId) : IRequest<BookingDto>,
    ITenantScoped;`
- `src/Modules/VrBook.Modules.Booking/Application/Commands/TransitionHandlers.cs`
  - MODIFY `CheckOutBookingHandler` — inject
    `IPropertyBasicInfoLookup` (or `IMediator` + `GetPropertyByIdQuery`)
    so the handler can read `property.TurnoverHours` before invoking
    `booking.CheckOut(property.TurnoverHours)`. Uses the existing
    cross-module lookup pattern from `PlaceBookingHandler:105`
    (`propertyOwners.GetAsync`) — we EXTEND `IPropertyOwnerLookup` OR
    add a sibling `IPropertyBasicInfoLookup.GetTurnoverHoursAsync`.
    Decision: extend the existing lookup by adding a
    `int TurnoverHours` field to the returned snapshot record;
    minimizes new contract surface. Snapshot rename: not required — the
    lookup already returns a tuple/record; add a property.
  - ADD `CompleteBookingHandler` — mirrors `CheckOutBookingHandler`
    shape (`OwnerActionHandler` base), calls
    `TransitionAsync(id, b => b.CompleteManually(), ct)`.
  - ADD `ScheduleCompletionHandler` —
    `TransitionAsync(id, b => b.ScheduleCompletion(hours), ct)`.
- `src/Modules/VrBook.Modules.Booking/Application/Commands/CompletionSweepCommand.cs`
  - REPLACE the hardcoded `CompletionDelay = TimeSpan.FromHours(24)` +
    `CheckedOutAt <= cutoff` predicate with `CompletionDueAt <= now`:
    ```csharp
    var now = clock.UtcNow;
    var dueBookings = await db.Bookings
        .Where(b => b.Status == BookingStatus.CheckedOut
                    && b.CompletionDueAt != null
                    && b.CompletionDueAt <= now)
        .ToListAsync(cancellationToken);
    ```
  - Rows where `CompletionDueAt IS NULL` are skipped by the predicate; the
    M.16.2 backfill ensures every historical CheckedOut row has a
    non-null due-at, so this is a no-op safety belt in practice. Any
    future path that produces a `CheckedOut` booking without a due-at
    is a domain bug — surface it in logs (add a warning-scan step:
    query for `Status = CheckedOut AND CompletionDueAt IS NULL`; log
    IDs if > 0). Kept as a separate diagnostic query so the sweep's
    main loop stays clean.
  - The sweep still calls `booking.Complete()` (not `CompleteManually`) —
    `Complete` raises `BookingCompleted` with `Trigger = "sweep"`.
- `src/Modules/VrBook.Modules.Booking/Application/Common/BookingMapping.cs`
  (or wherever `ToDto` lives — colocated near `BookingDto`)
  - MODIFY `Booking.ToDto()` to include `TurnoverHoursOverride` and
    `CompletionDueAt` in the response.
- `src/Modules/VrBook.Modules.Catalog/Application/Properties/Commands/CreatePropertyHandler.cs`
  - PASS `r.TurnoverHours` (defaulting to 24 if the request omits it,
    matching the CreatePropertyRequest field's default in M.16.4) into
    `Property.Create(...)`.
- `src/Modules/VrBook.Modules.Catalog/Application/Properties/Commands/UpdatePropertyHandler.cs`
  - EXTEND the `ExecuteUpdateAsync` chain with
    `.SetProperty(p => p.TurnoverHours, r.TurnoverHours)`. Preserves the
    procedural-update pattern the handler notes (line 15 comment).
- `src/Modules/VrBook.Modules.Catalog/Application/Common/PropertyMapping.cs`
  - MODIFY `Property.ToDto()` to include `TurnoverHours` in the
    `PropertyDto`.

Contracts changes:

- `src/VrBook.Contracts/Dtos/Booking.cs`
  - EXTEND `BookingDto` with:
    - `int? TurnoverHoursOverride`
    - `DateTimeOffset? CompletionDueAt`
    - `DateTimeOffset? CheckedOutAt` — currently not on the DTO;
      needed so the web UI can display "checked out X hours ago" +
      compute the schedule dropdown's option labels.
  - ADD `public sealed record ScheduleCompletionRequest(
    int HoursFromCheckedOutAt);`
- `src/VrBook.Contracts/Dtos/Catalog.cs`
  - EXTEND `PropertyDto` with `int TurnoverHours`.
  - EXTEND `CreatePropertyRequest` with `int TurnoverHours = 24`.
  - EXTEND `UpdatePropertyRequest` with `int TurnoverHours`. No default
    on the update side — client sends the current value on every PUT
    (matches how the form field will bind).

Tests moved to GREEN:

- Existing `CheckOutBookingHandlerTests` — the new sig break is fixed by
  the handler updates above. Extend one test to assert
  `booking.CompletionDueAt` is stamped.
- NEW `tests/VrBook.Modules.Booking.Tests/CompletionSweepPredicateTests.cs`
  (Unit) — 4 facts:
  - Sweep flips a booking whose `CompletionDueAt` is in the past.
  - Sweep skips a booking whose `CompletionDueAt` is in the future.
  - Sweep skips a booking with null `CompletionDueAt` (belt-and-braces).
  - Sweep is idempotent — running twice against the same set doesn't
    double-fire `BookingCompleted`.
- NEW `tests/VrBook.Modules.Booking.Tests/CompleteBookingHandlerTests.cs`:
  - Manual complete flips CheckedOut → Completed.
  - Manual complete on non-CheckedOut booking → BusinessRuleViolation.
- NEW `tests/VrBook.Modules.Booking.Tests/ScheduleCompletionHandlerTests.cs`:
  - 12/24/36/48h all accepted; recompute `CompletionDueAt`.
  - Negative hours → validation exception.
  - 200h > upper bound → validation exception.

CI expectation: **GREEN**. Full unit + arch suite passes.

Local validation: `dotnet test --filter "Category!=Integration"`.

### M.16.4 — GREEN. API surface + property CRUD form field.

Files touched:

- `src/VrBook.Api/Controllers/BookingsController.cs`
  - ADD (after `CheckOut` at line 115-121):
    ```csharp
    [HttpPost("{id:guid}/complete")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Manually complete a CheckedOut booking.")]
    public async Task<ActionResult<BookingDto>> Complete(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = await ResolveBookingTenantAsync(id, cancellationToken);
        return Ok(await mediator.Send(new CompleteBookingCommand(id, tenantId), cancellationToken));
    }

    [HttpPost("{id:guid}/schedule-completion")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Schedule when a CheckedOut booking auto-completes.")]
    public async Task<ActionResult<BookingDto>> ScheduleCompletion(
        Guid id, [FromBody] ScheduleCompletionRequest request, CancellationToken cancellationToken)
    {
        var tenantId = await ResolveBookingTenantAsync(id, cancellationToken);
        return Ok(await mediator.Send(
            new ScheduleCompletionCommand(id, request.HoursFromCheckedOutAt, tenantId),
            cancellationToken));
    }
    ```
  - Both endpoints reuse `ResolveBookingTenantAsync` (path-resolved
    tenant, F11.7.5.2 pattern). Both under
    `[Authorize(Roles="Owner,Admin")]` — same gate as CheckOut. When
    M.15 lands, decorators will migrate uniformly. Not our problem.

- `src/VrBook.Api/Controllers/PropertiesController.cs`
  - No new endpoints. `POST /api/v1/properties` and `PUT
    /api/v1/properties/{id}` accept the extended request DTOs from
    M.16.3.

Tests moved to GREEN:

- `tests/VrBook.Architecture.Tests/EndpointCoverageArchTest.cs` — the
  arch pack scans controllers for `[HttpPost]` + auth attributes; adding
  two new endpoints under the same pattern is transparent. No shape
  test change needed.
- NEW `tests/VrBook.Architecture.Tests/OpsM16_TurnoverEndpointsShapeTests.cs`
  (Unit) — 4 facts:
  - `POST /api/v1/bookings/{id}/complete` exists.
  - `POST /api/v1/bookings/{id}/schedule-completion` exists.
  - Both endpoints carry `[Authorize(Roles = "Owner,Admin")]`.
  - `ScheduleCompletionRequest` record has a single `int HoursFromCheckedOutAt`
    positional property.

CI expectation: **GREEN**. Solution builds. Endpoints reachable via
Swagger.

Local validation:
```
dotnet run --project src/VrBook.Api
# Swagger UI shows the two new endpoints under Booking tag.
```

### M.16.5 — GREEN. Overlap-policy predicate change across every reader.

The key insight from §3 exploration below: THREE readers of "booking
blocks this date range" need the CheckedOut turnover-day-block treatment;
TWO do not; ONE is a Sync-boundary and gets a narrower rule.

Files touched (production):

- `src/Modules/VrBook.Modules.Booking/Infrastructure/Persistence/BookingRepository.cs`
  - MODIFY `FindOverlapsAsync` (line 35-46) — change the predicate:
    ```csharp
    // Existing: b.Stay.CheckinDate < checkout && checkin < b.Stay.CheckoutDate
    // For CheckedOut bookings ONLY, treat the checkout date as INCLUSIVE
    // of the turnover-day block until the booking flips to Completed.
    .Where(b => b.PropertyId == propertyId
        && b.Status != BookingStatus.Cancelled
        && b.Status != BookingStatus.Rejected
        && b.Status != BookingStatus.Refunded
        && b.Stay.CheckinDate < checkout
        && (b.Status == BookingStatus.CheckedOut
              ? checkin <= b.Stay.CheckoutDate   // block through checkout-day
              : checkin < b.Stay.CheckoutDate))  // strict-less-than for others
    ```
    Rationale: the semantic is "for `CheckedOut`, checkout-day is still
    blocked; for every other still-active state (Tentative, Confirmed,
    CheckedIn, Completed), the standard half-open rule applies." Same
    booking, after flipping to `Completed`, becomes turnover-day-shared
    again — desired behavior.
  - MODIFY `ListBlockedRangesAsync` (line 48-63) — same conditional
    treatment. The blocked-range projection returns `(Checkin,
    Checkout)`; for CheckedOut bookings, project
    `(Checkin, Checkout.AddDays(1))` so the availability calendar
    reflects the turnover-day block. Wrapping into `AddDays(1)` on
    the DTO layer (after materializing to memory — `DateOnly.AddDays`
    doesn't translate well in EF for DateOnly).
- `src/Modules/VrBook.Modules.Booking/Infrastructure/Persistence/ConfirmedBookingLookup.cs`
  - `FindOverlappingAsync` at line 21-42 — this is the **Sync module's
    conflict checker** (used to reconcile inbound iCal reservations
    with direct bookings). For that use-case, the turnover-day-block
    rule adds noise — Sync should keep the strict half-open rule.
    Rationale: an inbound Airbnb reservation starting on our booking's
    checkout date is NOT a conflict, because the outgoing guest leaves
    that morning and Airbnb's guest arrives that afternoon; the property
    can serve both. The one-day-block rule is a **UI/UX policy for
    direct bookings placed while housekeeping is pending**, not a
    physical impossibility. Leave this method's predicate unchanged.
  - `ListForOutboundFeedAsync` at line 44-67 — outbound iCal feed to
    Airbnb/VRBO/Google Calendar. Should this reflect the CheckedOut
    turnover-day block? **No** — same rationale as above: external OTAs
    consume our iCal to know when the property is *physically*
    unavailable. Blocking the turnover day on the outbound feed would
    tell Airbnb we're occupied on 2026-08-03 even after our guest left
    that morning. Leave unchanged (`Status = Tentative | Confirmed |
    CheckedIn` — CheckedOut is already excluded).
- `src/Modules/VrBook.Modules.Booking/Application/Queries/GetPropertyCalendarQuery.cs`
  - `GetPropertyCalendarHandler` at line 56-68 — the admin calendar
    view. Every reader that surfaces "is this date blocked" to the
    operator should reflect the CheckedOut turnover-day block. MODIFY
    the projection to emit an `awaitingTurnover: bool` field on
    `CalendarBookingEntry` (true when `Status == CheckedOut`). The
    range itself stays as (`CheckinDate`, `CheckoutDate`) — the web
    layer renders the turnover-day-block visually based on the new
    flag. Rationale: separating "the booking's dates" from "the block
    it induces" is cleaner than lying about the checkout date; the
    calendar UI can render a striped/half-shaded box for the turnover
    day only.
  - EXTEND `CalendarBookingEntry` DTO (in `src/VrBook.Contracts/Dtos/
    Booking.cs` or wherever it lives) with `bool AwaitingTurnover`.
    Verify with the codebase — likely lives in `BookingCalendarDto.cs`
    or similar. Grep for `CalendarBookingEntry` and add there.
- `src/Modules/VrBook.Modules.Booking/Application/Queries/
  GetPropertyAvailabilityHandler.cs`
  - Delegates to `BookingRepository.ListBlockedRangesAsync` — the
    predicate change above flows through. This is the **guest-facing
    availability endpoint** (`GET /api/v1/properties/{id}/availability`
    at `PropertiesController:45-51`), served [AllowAnonymous], used by
    the guest checkout calendar. It NEEDS the CheckedOut turnover-day
    block — that's the surface where guests must not check in on the
    same day. Confirmed by the walk's report.

- `src/Modules/VrBook.Modules.Booking/Application/Commands/PlaceBookingHandler.cs`
  - The SQL `overlapSql` at line 148-156 uses the same strict-less-than
    semantic. UPDATE the SQL to conditionally treat `CheckedOut` as
    inclusive-on-checkout:
    ```sql
    SELECT "Id" FROM booking.bookings
    WHERE property_id = @p0
      AND status NOT IN ('Cancelled', 'Rejected', 'Refunded')
      AND deleted_at IS NULL
      AND checkin_date < @p2
      AND (
        (status = 'CheckedOut' AND @p1 <= checkout_date)
        OR (status <> 'CheckedOut' AND @p1 < checkout_date)
      )
    FOR UPDATE
    ```
    Race-safety: unchanged — still serializable-tx + FOR UPDATE. New
    predicate is deterministic across replicas.
- No change to `AvailabilityBlockHandlers.cs` — owner-created blocks
  have their own semantics (start_date/end_date), not bookings.

Every reader table (see §3):

| Reader | Currently blocks | After M.16 | Semantic |
|---|---|---|---|
| `FindOverlapsAsync` (Repo) | Tentative..Completed | Same; CheckedOut checkout-day inclusive | Change |
| `ListBlockedRangesAsync` (Repo) | Tentative..Completed | Same; CheckedOut checkout-day extended | Change |
| `PlaceBookingHandler` FOR UPDATE | Not (Cancelled/Rejected/Refunded) | Same; CheckedOut checkout-day inclusive | Change |
| `GetPropertyCalendarHandler` | Not (Cancelled/Rejected/Refunded) | Same; emit `awaitingTurnover` flag | Add flag |
| `GetPropertyAvailabilityHandler` | Delegates to Repo | Automatic via Repo change | Automatic |
| `ConfirmedBookingLookup.FindOverlappingAsync` | Confirmed/CheckedIn/CheckedOut | Unchanged | No change (Sync boundary) |
| `ConfirmedBookingLookup.ListForOutboundFeedAsync` | Tentative/Confirmed/CheckedIn | Unchanged | No change (outbound iCal) |

Tests moved to GREEN:

- NEW `tests/VrBook.Modules.Booking.Tests/OverlapPolicyCheckedOutTests.cs`:
  - `FindOverlaps_blocks_new_checkin_on_CheckedOut_booking_checkout_date`.
  - `FindOverlaps_allows_new_checkin_day_after_CheckedOut_checkout`.
  - `FindOverlaps_allows_shared_turnover_day_when_status_is_Completed`
    (regression — the fix must not block permanently).
  - `FindOverlaps_ignores_Cancelled_and_Rejected_regardless_of_status`.
- NEW `tests/VrBook.Modules.Booking.Tests/ListBlockedRangesCheckedOutTests.cs`:
  - `ListBlockedRanges_extends_CheckedOut_range_by_one_day`.
  - `ListBlockedRanges_Completed_range_uses_actual_checkout_date`.
- NEW `tests/VrBook.Modules.Booking.Tests/CalendarAwaitingTurnoverFlagTests.cs`:
  - CheckedOut entry carries `AwaitingTurnover = true`.
  - Completed entry carries `AwaitingTurnover = false`.
- MODIFY `tests/VrBook.Api.IntegrationTests/Sync/OutboundIcalFeedTests.cs`
  (if it exists) — verify CheckedOut bookings still excluded from
  outbound feed. Regression guard for the Sync-boundary decision.

CI expectation: **GREEN**.

Local validation: run the failing scenario against local Postgres:
```
# Seed: booking A on beach-villa 2026-08-01 → 2026-08-03, status = CheckedOut, CompletionDueAt in the future
# Try: place booking B on 2026-08-03 → 2026-08-05 same property
# Expect: 422 booking.dates_unavailable
# Then: POST /api/v1/bookings/{A}/complete
# Expect: 200 with status = Completed
# Retry place booking B — expect: 201 Created
```

### M.16.6 — GREEN. Web — admin booking detail Stay-lifecycle panel + property forms.

Files touched (web):

- `web/src/lib/api/booking.ts`
  - EXTEND `Booking` interface with:
    - `readonly checkedOutAt: string | null;`
    - `readonly completionDueAt: string | null;`
    - `readonly turnoverHoursOverride: number | null;`
  - EXTEND `CalendarBookingEntry` with `readonly awaitingTurnover: boolean;`
  - ADD:
    ```ts
    export const completeBookingManually = (id: string): Promise<Booking> =>
      apiFetch<Booking>(`/api/v1/bookings/${encodeURIComponent(id)}/complete`,
                        { method: 'POST' });

    export const scheduleBookingCompletion = (id: string, hoursFromCheckedOutAt: number): Promise<Booking> =>
      apiFetch<Booking>(`/api/v1/bookings/${encodeURIComponent(id)}/schedule-completion`,
                        { method: 'POST', body: { hoursFromCheckedOutAt } });
    ```
- `web/src/lib/api/property.ts` (or wherever `Property` interface lives)
  - EXTEND `PropertyDto` interface + `CreatePropertyRequest` +
    `UpdatePropertyRequest` with `turnoverHours: number`.

- `web/src/app/admin/bookings/[id]/page.tsx`
  - The Stay-lifecycle panel (currently lines 201-236) currently covers
    Confirmed/CheckedIn/CheckedOut with copy that reads "24h later the
    booking flips to Completed." REWRITE for the CheckedOut branch:
    - Show "Awaiting turnover" pill (yellow amber, distinct from status
      pill). Copy: "This property is unavailable for new same-day
      arrivals until you complete the stay. Scheduled auto-complete at
      {completionDueAt}."
    - Two controls:
      - Primary button `Complete now` — calls
        `completeBookingManually(id)`, refresh, done.
      - Secondary select `Reschedule auto-complete: [12h|24h|36h|48h|72h]`
        with an inline `Update` button — calls
        `scheduleBookingCompletion(id, hours)`. See §9-Q3 for the
        options list.
    - Show "Checked out at {checkedOutAt}. Auto-completes in
      {relative-time until completionDueAt}." Helper displays "in 12
      hours" / "5 minutes ago (any moment now)".
  - Confirmed / CheckedIn branches unchanged.
  - Timeline card (lines 421-460) gains a "Auto-complete scheduled" +
    "Completed" entries when applicable.
- `web/src/app/admin/properties/[id]/page.tsx` and
  `web/src/app/admin/properties/new/page.tsx`
  - Add a form field labelled "Turnover hours". Input: `<select>` with
    the same options (§9-Q3). Default 24. Field lives in the
    "Availability" / "Booking rules" section of the form. Two-way bound
    to `turnoverHours`; on submit, sent to
    `POST /api/v1/properties` / `PUT /api/v1/properties/{id}`.
  - Helper copy under the field: "How long after check-out before a new
    guest can check in on the same turnover day. Defaults to 24 hours.
    Individual bookings can override this."

- `web/src/app/admin/calendar/[propertyId]/page.tsx` (if exists — the
  admin calendar screen from Slice 0.6)
  - When a `CalendarBookingEntry` has `awaitingTurnover: true`, render a
    diagonal-stripe / half-shade overlay on the checkout date. A small
    "!" badge tooltip: "Awaiting turnover — new same-day arrivals
    blocked. Complete the stay to unblock."
  - If the page doesn't exist yet (path uncertain from exploration —
    verify), this bullet is a no-op and lands in a polish slice.

Tests moved to GREEN:

- No web unit tests exist for these pages today — align with the M.14.4
  cleanup convention (assertions land in the integration walk playbook,
  not JS unit tests). Add one Playwright scenario in the close-out
  runbook (§7) that walks the fix:
  - CheckOut a booking → detail page shows Awaiting turnover pill.
  - `GET /availability` for the same property confirms same-day check-in
    is blocked.
  - Click Complete now → pill flips to Completed; retry `GET
    /availability` returns the same-day slot as bookable.

CI expectation: **GREEN**. Web build passes (`npm run build`), lint
green, no unit tests to fail.

Local validation:
```
npm run build --workspace web
# Manual walk of the four surfaces above.
```

### M.16.7 — GREEN. Close-out — arch tests, MASTER_PLAN, integration test scenario, staging runbook.

Files touched:

- `tests/VrBook.Architecture.Tests/OpsM16_TurnoverAwareShapeTests.cs`
  (NEW) — 6 facts:
  1. `Property` type has `TurnoverHours` (int).
  2. `Booking` type has `TurnoverHoursOverride` (int?) and
     `CompletionDueAt` (DateTimeOffset?).
  3. `PropertyDto` includes `TurnoverHours`.
  4. `BookingDto` includes `TurnoverHoursOverride`, `CompletionDueAt`,
     `CheckedOutAt`.
  5. `CompletionSweepHandler` source text contains "CompletionDueAt"
     (not "CompletionDelay"). Guards against a future refactor
     accidentally reverting to the hardcoded-24h shape.
  6. `BookingRepository.FindOverlapsAsync` source text contains
     "CheckedOut" (i.e. still has the conditional predicate).
- `tests/VrBook.Api.IntegrationTests/Booking/TurnoverAwareCompletionTests.cs`
  (NEW; Category=Integration — runs on the nightly job, not the
  standard filter). Six scenarios:
  1. Place booking, check-in, check-out, retry same-turnover-day
     new booking → 422.
  2. Same as (1), then hit `/complete` → new booking succeeds.
  3. Sweep runs with `CompletionDueAt` in past → booking flips to
     Completed.
  4. Sweep runs with `CompletionDueAt` in future → booking stays
     CheckedOut.
  5. `POST /schedule-completion` with 12h → `CompletionDueAt` moves
     to `CheckedOutAt + 12h`.
  6. Property PUT with `turnoverHours = 48` → new booking's check-out
     stamps `CompletionDueAt = CheckedOutAt + 48h`.

- `docs/MASTER_PLAN.md`
  - Bump the "Last revised" date to today.
  - Add a row after M.14 in the Phase-1.5 status table:
    `| Slice OPS.M.15 — App-role legacy claim reads / [Authorize(Roles=)] drop | ⏭ deferred | plan: TBD | user redirected to M.16 UX-driven work; still open |`
    `| Slice OPS.M.16 — Turnover-aware completion + configurable turnover window | ✅ | commit range | staging |`

- `docs/OPS_M_16_CLOSE_OUT.md` (NEW)
  - Standard close-out shape: what shipped, commit range, walk playbook
    diff, follow-ups list. Follow-up seeds:
    - "Housekeeping module lands off `TurnoverScheduled` +
      `BookingCompletionRescheduled` events (later slice)."
    - "Consider auto-scheduling based on housekeeping ready-signal
      from third-party integration."
    - "Calendar UI 'awaiting turnover' polish — visual language could
      be richer than diagonal stripe (design bandwidth permitting)."
    - "M.15 remains deferred; no dependency in either direction."

- `docs/runbooks/turnover_walk.md` (NEW; two paragraphs)
  - Reproduces the 2026-07-04 walk that surfaced the bug. Serves as
    manual smoke test the operator can run after this slice.

Tests moved to GREEN:

- The new arch tests + integration tests all pass. Unit + arch under
  standard filter; integration runs green in nightly.

CI expectation: **GREEN**. Standard filter passes. Nightly integration
job passes on the next run.

Local validation:
```
dotnet test --filter "Category!=Integration"
# Optional: dotnet test --filter "Category=Integration" (needs local Postgres + Docker fixtures).
```

---

## §2 Domain-model decisions locked

### §2.1 `TurnoverHours` lives on Property, not on Tenant

**Recommendation:** property-level only. No tenant-level default.

Rationale: the operator surfaced this per-property (Beach Villa specifically);
housekeeping-turnaround varies dramatically by property type inside one
tenant (a beach villa needs a 24-48h damage-check window; a downtown
studio can turn in 4h). Tenant-level defaults would only paper over
that variance. The property-level field lets each owner tune their own
inventory; the booking-level override handles the exceptional case.

If the future demands a tenant-level default, it lands as a `Tenant.
DefaultTurnoverHours` with the resolution chain `booking.override ??
property.turnoverHours ?? tenant.default ?? 24`. Purely additive; not
in this slice. §9-Q1 confirms this pick.

### §2.2 `TurnoverHoursOverride` is a stored int? — but the effective due-at is a snapshot

**Recommendation:** store BOTH `int? TurnoverHoursOverride` (nullable,
per-booking override) AND `DateTimeOffset? CompletionDueAt` (the
snapshotted absolute timestamp).

Two-field model justification (this is the interesting piece):

- `TurnoverHoursOverride` is the operator's INPUT — the value they
  picked in the UI ("give me 48h"). Persisting the raw override lets the
  UI surface it back ("current override: 48h") without recomputing.
- `CompletionDueAt` is the DERIVED absolute timestamp — the OUTPUT the
  sweep predicate reads. Snapshotting it at CheckOut / ScheduleCompletion
  time means:
  - If the property's `TurnoverHours` changes mid-stay, existing
    CheckedOut bookings' due-at is UNAFFECTED. The user asked for this
    behavior explicitly ("avoids surprise if property config changes
    mid-stay") — that's exactly right.
  - The sweep predicate is trivially indexable (`WHERE
    completion_due_at <= now`), no JOIN to `catalog.properties`, no
    cross-schema reads inside the worker.
  - Observability is friendlier: `SELECT id, completion_due_at FROM
    booking.bookings WHERE status = 'CheckedOut' ORDER BY
    completion_due_at` is a straight query the operator can run to
    see "what's about to auto-complete."

The alternative — compute on the fly at sweep time via a JOIN to
`catalog.properties` — was rejected for the same reason M.9's
per-statement GUC binding was preferred over per-connection: making the
runtime read the smallest possible view of state gives cleaner
reasoning + fewer failure modes.

### §2.3 Domain method signatures

- `booking.CheckOut(int propertyTurnoverHours)`. Renamed sig (was
  no-arg). Invariants: `propertyTurnoverHours >= 0` (defensive; caller
  passes `Property.TurnoverHours` which is already validated). Requires
  `Status == CheckedIn`. Body computes and stamps `CompletionDueAt`
  from `TurnoverHoursOverride ?? propertyTurnoverHours`.

- `booking.ScheduleCompletion(int hoursFromCheckedOutAt)`. Invariants:
  - `Status == CheckedOut` — throws `BusinessRuleViolationException`
    "booking.state" otherwise (mirrors existing `Require()` idiom).
  - `hoursFromCheckedOutAt >= 0` (0h means "auto-complete on next
    sweep tick"; use `CompleteManually()` for truly-immediate).
  - `hoursFromCheckedOutAt <= 168` (7d upper bound — see §9-Q2).
  - Updates BOTH `TurnoverHoursOverride` (new value) AND
    `CompletionDueAt` (recomputed from `CheckedOutAt + hours`).
  - Raises `BookingCompletionRescheduled`.

- `booking.CompleteManually()`. Distinct method from `Complete()` for
  domain expressiveness. Requires `Status == CheckedOut`. Same status
  flip, distinguishable event (Trigger = "manual" — see §9-Q6).

### §2.4 Why a distinct `CompleteManually()` method (not just a nullable "actor" on `Complete()`)

Domain hygiene: `Complete()` is called by the daily sweep worker, which
has no user context (`AnonymousCurrentUser`). Passing an "actor" argument
opens a shape where the sweep might accidentally stamp a real user id.
Two methods, one owned by the sweep and one by the admin, sidesteps the
question by construction.

The event carries the trigger discriminator (`"sweep"` vs `"manual"`)
so downstream handlers can differentiate if they need to. Loyalty +
Notifications ignore it today; nothing breaks. Wire format change is
additive — existing `BookingCompleted` consumers pattern-match on the
fields they care about (BookingId, Reference, GuestUserId, TenantId);
the new `Trigger` field defaults to `"sweep"` when a legacy outbox
message is replayed post-deploy. Concrete backward-compat: see §7 risk
table row "Outbox replay of pre-M.16 BookingCompleted."

### §2.5 The invariant table

| Method | Requires status | Additional checks | Raises |
|---|---|---|---|
| `CheckOut(int propertyTurnoverHours)` | `CheckedIn` | `propertyTurnoverHours >= 0` | `BookingCheckedOut` |
| `ScheduleCompletion(int hours)` | `CheckedOut` | `0 <= hours <= 168` | `BookingCompletionRescheduled` |
| `CompleteManually()` | `CheckedOut` | — | `BookingCompleted(Trigger="manual")` |
| `Complete()` (existing) | `CheckedOut` | — | `BookingCompleted(Trigger="sweep")` |

---

## §3 Overlap-policy semantics — precise predicate map

Every reader in the codebase that answers "does this stay range overlap
these dates" enumerated + decision recorded. Search anchor:
`Stay.CheckinDate < ... && ... < Stay.CheckoutDate`.

| # | Location | Consumer | Current predicate | Decision | Why |
|---|---|---|---|---|---|
| 1 | `BookingRepository.FindOverlapsAsync` (line 35) | `PlaceBookingHandler` (upstream call) | `Status != Cancelled && != Rejected && Stay.CheckinDate < checkout && checkin < Stay.CheckoutDate` | **CHANGE**: conditional-on-CheckedOut inclusive | Direct-booking safety on turnover day — the walk fix |
| 2 | `BookingRepository.ListBlockedRangesAsync` (line 48) | `GetPropertyAvailabilityHandler` (which delegates from `GET /api/v1/properties/{id}/availability`) | 5-status set incl. CheckedOut + Completed; standard half-open | **CHANGE**: CheckedOut range projected as `(Checkin, Checkout.AddDays(1))` before returning | Same rule as (1), surfaces to guest-facing availability calendar |
| 3 | `PlaceBookingHandler` FOR UPDATE SQL (line 148) | `POST /api/v1/bookings` | `Status NOT IN Cancelled/Rejected/Refunded; checkin_date < @p2 AND @p1 < checkout_date` | **CHANGE**: conditional-on-status SQL branch | Same rule as (1), race-safe path — locks + serializable-tx unchanged |
| 4 | `GetPropertyCalendarHandler` (line 56) | `GET /api/v1/properties/{id}/calendar` (admin calendar) | `Status != Cancelled/Rejected/Refunded && standard half-open` | **CHANGE**: emit `AwaitingTurnover: bool` flag on the entry; keep the date range as-is | Admin needs to *see* the turnover day is soft-blocked without the DTO lying about the checkout date |
| 5 | `ConfirmedBookingLookup.FindOverlappingAsync` (line 21) | Sync module's iCal conflict checker | Confirmed / CheckedIn / CheckedOut + standard half-open | **NO CHANGE** | Sync boundary — external OTAs need physical-reality semantics, not our internal turnover policy |
| 6 | `ConfirmedBookingLookup.ListForOutboundFeedAsync` (line 44) | Outbound iCal feed → Airbnb/VRBO/Google | Tentative / Confirmed / CheckedIn + `Stay.CheckoutDate >= from` | **NO CHANGE** | Outbound feed reports physical unavailability; blocking turnover day would over-report to OTAs |

The above list is exhaustive against `grep -rE "Stay\.CheckinDate\s*<\s*.*&&.*<\s*b\.Stay\.CheckoutDate"` + `grep -r "checkin_date < .*checkout_date"`. Two matches outside module code:

- Migration file source text (docstrings + snapshot files) — not
  executed logic; no change.
- `docs/OPS_M_4_PLAN.md` reference — pure prose; no change.

`AvailabilityBlockHandlers` was called out in the problem statement as a
potential change site — verified NO CHANGE needed. Owner-created blocks
are their own aggregate (`AvailabilityBlock`) with `StartDate` +
`EndDate` in day-precision; they carry no "checked-out but not
completed" state. The block predicate at `PlaceBookingHandler:178-185`
uses the standard strict-less-than and stays as-is.

Sync module outbound feeds (row 6) — the plan explicitly does NOT
reflect the CheckedOut turnover-day block. External calendars will
continue to see the property as available on the checkout day, which
is correct: the property is physically available; we're just choosing
to soft-block DIRECT bookings on that day while housekeeping is in flight.

---

## §4 Sweep-worker changes

### §4.1 The predicate change

Pre-M.16:
```csharp
private static readonly TimeSpan CompletionDelay = TimeSpan.FromHours(24);
var cutoff = clock.UtcNow - CompletionDelay;
db.Bookings.Where(b => b.Status == BookingStatus.CheckedOut
                    && b.CheckedOutAt != null
                    && b.CheckedOutAt <= cutoff)
```

Post-M.16:
```csharp
var now = clock.UtcNow;
db.Bookings.Where(b => b.Status == BookingStatus.CheckedOut
                    && b.CompletionDueAt != null
                    && b.CompletionDueAt <= now)
```

### §4.2 Snapshot vs on-the-fly — locked

Snapshot approach picked (as recommended in the problem statement).
Rationale detailed in §2.2. The `CompletionDueAt` column is written in
exactly two places:

- `Booking.CheckOut(int propertyTurnoverHours)` at CheckedIn → CheckedOut
  transition. Uses `TurnoverHoursOverride ?? propertyTurnoverHours` —
  override wins if set from a previous ScheduleCompletion call (edge
  case: admin scheduled a 48h window during previous CheckedOut state
  that was then rolled back — this shouldn't happen in the current
  state machine but the assumption is worth calling out).
- `Booking.ScheduleCompletion(int hours)` — recomputes from
  `CheckedOutAt + hours`. Always overwrites both fields.

Never written by:
- The sweep itself (it only reads).
- Any migration other than M.16.2's backfill (which stamps existing
  CheckedOut rows with `CheckedOutAt + 24h`).
- Web / API paths other than the two commands above (arch-test enforced
  via source-text scan in M.16.7 — no `CompletionDueAt =` outside
  Booking.cs).

### §4.3 Sweep worker behavior otherwise unchanged

- Cron `0 6 * * *` — kept. §9-Q7 discusses whether the cron cadence
  should shorten given some bookings will have due-at within the same
  day. Recommendation: keep cron 06:00 UTC + accept up to 24h latency;
  operators wanting immediate completion click "Complete now."
  Alternative: run every hour. Trade-off in §9-Q7.
- `BookingCompleted` event still raised, still ripples to Loyalty +
  Notifications through in-process MediatR. Adding the new `Trigger`
  field to the event does NOT force any downstream module change; the
  existing handlers ignore the field.
- Idempotency preserved: sweep skips a booking already flipped to
  Completed (the predicate filters `Status == CheckedOut` — a
  manually-completed booking has `Status == Completed` and is excluded
  automatically).

### §4.4 Bicep / infra impact

- `infra/main.bicep` line 428-449 (`bookingCompletionJob`) —
  **NO CHANGE** to the Bicep module. The cron cadence stays 06:00 UTC.
  The image is the same worker image; `--mode=completion` still routes
  to `CompletionSweepCommand` which now honors `CompletionDueAt`.
- Rolling deploy: safe. During the deploy window, an in-flight sweep
  running the OLD image still uses `CheckedOutAt + 24h`. The M.16.2
  backfill guarantees every CheckedOut row already has a
  `CompletionDueAt = CheckedOutAt + 24h`. So OLD-code behavior ≡
  NEW-code behavior for pre-M.16 rows. Post-deploy, new CheckOuts
  stamp `CompletionDueAt` up-front; the OLD-code path is retired.

---

## §5 API surface additions

### §5.1 Booking module

| Method + path | Auth | Body | Response | Notes |
|---|---|---|---|---|
| `POST /api/v1/bookings/{id}/complete` | `[Authorize(Roles = "Owner,Admin")]` | — (no body) | `200 OK` with `BookingDto` | Manual completion. Transitions `CheckedOut → Completed`, raises `BookingCompleted(Trigger="manual")`. 422 on wrong status. |
| `POST /api/v1/bookings/{id}/schedule-completion` | `[Authorize(Roles = "Owner,Admin")]` | `{ "hoursFromCheckedOutAt": 24 }` (int, `[0, 168]`) | `200 OK` with updated `BookingDto` (fresh `CompletionDueAt`) | Sets `TurnoverHoursOverride` + recomputes `CompletionDueAt`. 422 on out-of-range OR wrong status. |

Both endpoints use the `ResolveBookingTenantAsync` path-resolution
pattern (F11.7.5.2). Both live on `BookingsController` alongside
CheckIn / CheckOut so the RBAC gate is uniform.

### §5.2 Catalog module

No new endpoints. `POST /api/v1/properties` and `PUT
/api/v1/properties/{id}` accept the extended request DTOs
(`turnoverHours` added). `GET /api/v1/properties/{slug}` and `GET
/api/v1/admin/properties/{id}` surface `turnoverHours` in
`PropertyDto`.

### §5.3 Not-added

- No `DELETE /api/v1/bookings/{id}/scheduled-completion` — to "cancel"
  a schedule, call `POST /schedule-completion` with the property
  default. Simpler surface.
- No `GET /api/v1/bookings/{id}/completion-schedule` — the info lives on
  `BookingDto` (`completionDueAt`, `turnoverHoursOverride`). No
  separate resource needed.
- No dedicated batch endpoint for "complete all overdue" — the
  daily sweep IS that endpoint.

---

## §6 Web UI additions

### §6.1 Admin booking detail — Stay-lifecycle panel

Wireframe (CheckedOut state only; Confirmed / CheckedIn unchanged):

```
[Blue-purple panel: Stay lifecycle]
[Icon: clock]  Awaiting turnover
Checked out 2 hours ago. Auto-completes in 22 hours (2026-08-04 15:32).
This property is unavailable for same-day arrivals on 2026-08-03 until
you complete the stay.

  [Reschedule auto-complete: 12h ▾]  [Update]     [Complete now]
                              ^ options: 12h, 24h, 36h, 48h, 72h
```

- The status pill in the header shows "CheckedOut" (existing behavior).
- A second, softer badge in the panel body reads "Awaiting turnover" —
  this is the operator's mental model.
- The relative-time text ("in 22 hours") uses `dayjs.from(dueAt)` or a
  helper — keep it live-updating on client render (page is a client
  component).
- `[Complete now]` is a primary CTA (matching the existing check-out
  button's green primary treatment). Clicking calls
  `completeBookingManually(id)`. Refreshes React Query keys `['admin',
  'booking', id]` + `['admin', 'bookings']` (same refresh pattern as
  the existing lifecycle actions).
- `[Reschedule auto-complete]` is a select + adjacent "Update" button.
  Options: `12h, 24h, 36h, 48h, 72h` — §9-Q3.
- If `turnoverHoursOverride` is non-null, the select pre-selects that
  value; label reads "Reschedule (current: 48h override)". If null,
  reads "Reschedule (property default: 24h)".

Error surface: reuses the existing `actionError` banner (line 349 of
`page.tsx`) — new errors from complete/schedule bubble through the same
`runAction` helper. No modal — the "Complete now" action is not
destructive enough to warrant a confirm dialog (contrast:
Confirm/Reject open modals because charges/refunds are involved).

### §6.2 Property create/edit forms

Add one field, likely in the "Booking rules" or "Availability" section:

```
[Field label] Turnover hours
[Select ▾ 24h]     [Help: How long after check-out before a new guest
                          can check in on the same turnover day.
                          Defaults to 24 hours. Individual bookings
                          can override this.]
```

Same option list as the booking-detail select (§9-Q3). Default 24. Two-
way bound. On PUT, the entire `UpdatePropertyRequest` is sent (matches
the existing form's submit shape at
`web/src/app/admin/properties/[id]/page.tsx`).

### §6.3 Admin calendar polish

`web/src/app/admin/calendar/[propertyId]/page.tsx` (if extant — verify)
receives one visual change: bookings with `awaitingTurnover: true`
render a diagonal-stripe overlay on the checkout day, with a small "!"
badge and tooltip: "Awaiting turnover — new same-day arrivals blocked.
Complete the stay to unblock." Verified against the walk: this is where
the operator will next look when a guest's same-day arrival is refused.

---

## §7 Risks + open questions

### §7.1 Risk table

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| 1 | Sweep already fired on an in-flight CheckedOut booking (idempotency) | Low | Low | Sweep filters `Status == CheckedOut`; a Completed booking is skipped. Manual complete on a Completed booking → 422 "booking.state." Test in M.16.7 integration pack. |
| 2 | Outbox replay of pre-M.16 `BookingCompleted` after deploy | Medium | Low | New `Trigger` field defaults to `"sweep"` when reading legacy JSON. Downstream handlers (Loyalty stay-count, Notifications review-request) don't read the field — no behavior change. |
| 3 | Cross-schema JOIN inside migration for the backfill | N/A | — | Explicitly avoided in §1 M.16.2 — backfill uses a fixed 24h, no JOIN to `catalog.properties`. |
| 4 | Admin schedules "72h" and never clicks Complete — property blocked for 3 days | Medium | Medium | Sweep fires at 72h regardless; that's the desired behavior. Alternative in §9-Q4 — could add a warning banner in web when override > 48h. Not scoped here. |
| 5 | Rolling-deploy race: OLD-image sweep hits NEW-schema DB during deploy | Low | Medium | See §4.4 — OLD-code path reads `CheckedOutAt` and computes `+24h` on the fly. Backfill guarantees NEW column values are exactly what OLD-code would compute. No divergence. |
| 6 | Calendar UI "awaiting turnover" pill not implemented at time of ship (calendar page path uncertain) | Medium | Low | If the calendar page isn't shipped in this slice, direct availability endpoint still blocks the same-day check-in (M.16.5 rule 2) — the guest is protected at the API layer. Pill is polish, not correctness. Deferred to polish slice if scope tight. |
| 7 | RLS interaction with the new schedule/complete endpoints | Low | Low | Both endpoints go through `TransitionAsync` which uses `bookings.GetByIdAsync` — same RLS path as CheckIn/CheckOut. `TenantAuthorizationBehavior` gates. No new RLS surface. |
| 8 | Web behavior when `checkedOutAt` is present but `completionDueAt` is null (pre-M.16.2 backfill race) | Very low | Low | Post-backfill this state cannot occur. Belt: web renders "auto-complete unscheduled — click Complete now" as the fallback. |

### §7.2 Arch tests needed

Listed in M.16.7 (`OpsM16_TurnoverAwareShapeTests`). Additional
optional guards:

- Roslyn arch test asserting `Booking.CheckOut` signature takes exactly
  one `int` parameter — protects against a future refactor going back
  to zero-arg by mistake.
- Roslyn arch test asserting `CompletionSweepHandler` does NOT contain
  the string "TimeSpan.FromHours(24)" — same protection at the sweep
  handler.

Both included in M.16.7.

### §7.3 What if the admin schedules 72h then never completes

The sweep fires when `CompletionDueAt <= now`. The 72h due-at is
respected; no override "expires." Locked in §9-Q7 (the sweep is the
sole automatic trigger; humans do everything else).

### §7.4 What about a booking with `Status = CheckedOut` and a null `CompletionDueAt`

Post-M.16.2 backfill, this state doesn't occur. If it does (data-heal
gone sideways), the sweep skips the row (predicate filters it out) and
the manual "Complete now" button still works (the domain method only
requires `Status == CheckedOut`). The extra diagnostic query in the
sweep handler (§1 M.16.3) logs a warning listing such rows so the
operator notices.

### §7.5 What about the calendar showing "awaiting turnover" for a booking that ALREADY has an override

The pill shape doesn't distinguish "default 24h" from "48h override"
visually. The tooltip surfaces the `completionDueAt` so operators can
disambiguate. If richer visual distinction is needed, that's polish.

### §7.6 What if a check-in booking becomes cancelled after check-out (edge)

Not possible — `CheckedOut` state cannot transition to `Cancelled` in
the current state machine. `CancelByGuest` gates `Tentative` or
`Confirmed` only. No new domain method opens this path.

---

## §8 Slice ordering + registry impact

### §8.1 Slice sequence

- Predecessor: OPS.M.14 (DevAuth retirement) ✅ shipped.
- **Intentionally skipped:** OPS.M.15 (App-role legacy claim reads /
  `[Authorize(Roles=)]` drop) — user redirected to M.16 UX-driven work.
  M.15 stays open; nothing in M.16 depends on it, and nothing in M.16
  will need to be re-touched when M.15 lands (both new endpoints use
  the same `[Authorize(Roles="Owner,Admin")]` decorator every other
  Owner endpoint uses; the eventual M.15 sweep migrates them
  uniformly).
- Successor: TBD by user. OPS.M.15 recommended next; alternatively the
  housekeeping module (which would hang off the events this slice
  publishes).

### §8.2 Dependencies

- Depends on: OPS.M.4 (`TenantAuthorizationBehavior`) — the new commands
  are `ITenantScoped`, so the gate applies as-is.
- Depends on: OPS.M.9 RLS — CheckedOut bookings visible to the tenant
  via the existing tenant-isolation policy. No new policy needed. The
  M.9.1 public-read carve-out on `catalog.properties` covers the guest
  availability endpoint's read path.
- Depends on: OPS.M.13 email-canonical users — no direct dependency;
  called out because the M.13 close-out baseline is where the mock
  JwtBearer test handler lives (integration tests in M.16.7 use it).
- Depends on: OPS.M.14 DevAuth retirement — no direct dependency; the
  mock handler is the test-side auth path.

### §8.3 Successors / follow-ups

- **Housekeeping module** (not scheduled). Would consume
  `BookingCompletionRescheduled` + `BookingCheckedOut` +
  `BookingCompleted(Trigger="manual")` events to task housekeeping
  staff. This slice publishes the events; module authoring is a
  separate slice.
- **OPS.M.15** — still deferred by the user; no dependency on M.16 in
  either direction.
- **Property availability API cursor** — the guest availability
  endpoint might grow richer (per-date "why is this blocked" narrative)
  in a later polish slice. Not scoped.

### §8.4 MASTER_PLAN entry (draft)

```
| Slice OPS.M.16 — Turnover-aware completion + configurable turnover window | ✅ | commit range TBD | staging |
```

Row added below the existing OPS.M.14 row in the Phase-1.5 status
table.

---

## §9 Questions to lock BEFORE execution

Each framed as a specific choice, not open-ended.

### §9-Q1: Does `turnoverHours` live only on Property, OR also on Tenant?

- **Option A (recommended):** Property only. `CreatePropertyRequest`
  gains `turnoverHours` (default 24). No new tenant field.
- Option B: Tenant carries `defaultTurnoverHours`; Property override
  optional; effective = `booking.override ?? property.override ??
  tenant.default ?? 24`. Two new columns, one new admin surface.

**Recommendation: A.** Rationale in §2.1. Adds one column; keeps
Slice OPS.M.16 to the scope in the trigger.

### §9-Q2: What's the upper bound on the schedule override / property default?

- **Option A (recommended):** 168 hours (7 days).
- Option B: 72 hours (3 days).
- Option C: No upper bound.

**Recommendation: A (168h).** Rationale: covers the plausibly-worst
operator scenario ("owner is out of town for a week; damage-check
delayed"). 72h is too tight for occasional real cases. No upper bound
opens misuse (accidentally typing 720 = 30 days). 168h is a natural
"one week" ceiling.

### §9-Q3: What are the schedule dropdown / property form options?

- **Option A (recommended):** discrete set `12h, 24h, 36h, 48h, 72h`.
- Option B: free-form int input `[0, 168]`.
- Option C: coarser set `24h, 48h`.

**Recommendation: A.** Discrete set covers 90% of cases; the domain
still accepts any int in `[0, 168]`, so a future admin surface can
send arbitrary values. Free-form input clutters the form for the
common case. If the discrete set is inadequate on staging walks, we
swap to Option B in a polish slice.

### §9-Q4: What migration default for existing rows?

- **Option A (recommended):** `property.turnover_hours` defaults to 24
  via column default (all existing rows get 24). `booking.
  completion_due_at` backfilled to `checked_out_at + interval '24h'`
  for `Status == CheckedOut`.
- Option B: `property.turnover_hours` NULL for existing rows; NULL
  interpreted as "24 in code."
- Option C: `booking.completion_due_at` NULL for existing rows; sweep
  falls back to `checked_out_at + 24h` compute when NULL.

**Recommendation: A.** Rationale: no NULL semantics to reason about
downstream; `IS NOT NULL` predicate on the sweep is unambiguous. B
introduces NULL-vs-24 branch everywhere `TurnoverHours` is read.
C weakens the "snapshot immutability" invariant. A is cleanest.

### §9-Q5: What happens to `completion_due_at` when the property's `turnover_hours` changes mid-stay?

- **Option A (recommended):** Snapshot — existing CheckedOut bookings'
  due-at is UNAFFECTED. Only NEW check-outs stamp the new default.
- Option B: Live-recompute — property update fires a downstream
  handler that recomputes due-at for every CheckedOut booking of that
  property.

**Recommendation: A.** User explicitly asked for this behavior in the
trigger. Rationale in §2.2. B invites operator confusion ("I changed
the default and my in-flight bookings shifted").

### §9-Q6: Does `CompleteManually()` raise a distinct event, or reuse `BookingCompleted` with a trigger discriminator?

- **Option A (recommended):** Reuse `BookingCompleted` with a new
  `string Trigger` field ("sweep" | "manual"). Mirrors existing
  `BookingConfirmed(...trigger="owner"|"sla")` shape.
- Option B: New `BookingManuallyCompleted` event alongside
  `BookingCompleted`.

**Recommendation: A.** Consistent with `BookingConfirmed`'s existing
discriminator pattern (see line 130 of Booking.cs). B forces two
outbox handlers on every consumer for what is one semantic event.

### §9-Q7: When admin schedules "72h" and never clicks Complete — does the sweep fire at 72h regardless?

- **Option A (recommended):** Yes — sweep respects `CompletionDueAt`
  as absolute truth. 72h after check-out, the booking auto-completes.
- Option B: No — schedule overrides are advisory; sweep still fires at
  24h.
- Option C: Yes, but shorten the cron cadence so 72h is fired
  precisely at ~72h (currently daily cron means 24h latency).

**Recommendation: A + keep cron `0 6 * * *`.** Rationale: the operator
who explicitly scheduled 72h expects 72h. Sub-day latency is not the
usability issue the walk surfaced. If operators want tighter response,
they use "Complete now." If we later find operators regularly want,
say, "complete at exactly 36h" and hate the daily-cron latency, we
shorten the cron in a follow-up (trivial infra change).

### §9-Q8: Should the guest-facing availability endpoint (`GET /api/v1/properties/{id}/availability`) reflect the CheckedOut turnover-day block?

- **Option A (recommended):** Yes — same rule as `FindOverlapsAsync`.
  Guest browsing the calendar sees the checkout day as blocked;
  cannot start a hold on that day.
- Option B: No — availability endpoint reports physical unavailability
  only; the "won't allow same-day check-in" rule lives in
  `PlaceBookingHandler` alone.

**Recommendation: A.** UX-critical: a guest picking dates on the
calendar shouldn't see a green day, click "Book," get to checkout,
receive a hold, then fail with 422. Consistency between calendar
availability and booking success is a hard requirement. B introduces
that inconsistency.

### §9-Q9: How does the `awaitingTurnover` flag propagate through the calendar DTO?

- **Option A (recommended):** New boolean field on
  `CalendarBookingEntry` (in `PropertyCalendarDto`). Web renders visual
  overlay based on the flag. Range remains
  `(Checkin, Checkout)` (truthful — that's the booking's actual dates).
- Option B: Extend the range in the DTO — for CheckedOut, send
  `(Checkin, Checkout.AddDays(1))`. Web renders it opaquely.

**Recommendation: A.** Cleaner separation of "what the booking IS"
from "what block it induces." Web can render both without lying about
the underlying dates. §3 row 4 embeds this decision.

---

## §10 Summary — sub-commit checklist

- [ ] M.16.1 RED: Domain diffs + unit tests.
- [ ] M.16.2 GREEN: 2 migrations + config wiring + backfill.
- [ ] M.16.3 GREEN: Application layer + sweep predicate + CheckOut wiring + DTOs.
- [ ] M.16.4 GREEN: API endpoints + Property CRUD DTOs.
- [ ] M.16.5 GREEN: Overlap-policy predicate change across every reader.
- [ ] M.16.6 GREEN: Web — booking detail Stay-lifecycle panel + property forms + api client.
- [ ] M.16.7 GREEN: Arch tests + integration tests + MASTER_PLAN + close-out doc + runbook.

Each ends `dotnet test --filter "Category!=Integration"` green + `npm
run build --workspace web` green. Integration tests validate on the
nightly CI job before ship.

---

**End of plan. Awaiting user sign-off on §9 answers before starting M.16.1.**
