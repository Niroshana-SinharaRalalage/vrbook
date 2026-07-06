using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Api.IntegrationTests.Multitenancy;
using VrBook.Contracts.Enums;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Booking.Application.Commands;
using VrBook.Modules.Booking.Domain;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using Xunit;
using DomainBooking = VrBook.Modules.Booking.Domain.Booking;

namespace VrBook.Api.IntegrationTests.BookingModule;

/// <summary>
/// Slice OPS.M.16 polish part 3 (shipped as OPS.M.20) — integration
/// scenarios covering the M.16 turnover-aware completion invariants at
/// the storage boundary. Reproduces the 2026-07-04 staging walk that
/// surfaced the pre-M.16 same-day-booking-during-turnover bug.
///
/// <para>Scope is the sweep-handler behavior against real Postgres +
/// the persistence roundtrip on <c>CompletionDueAt</c> /
/// <c>TurnoverHoursOverride</c>. Domain-only invariants (transition
/// preconditions, snapshot semantics, override math) are covered by
/// <c>BookingAggregateTests</c> under Category=Unit; NOT duplicated
/// here.</para>
///
/// <para>Uses <see cref="TwoTenantApiFixture"/> for the Postgres
/// testcontainer + module migrations. Each scenario TRUNCATEs
/// bookings under <see cref="RlsBypassScope"/> so state from one
/// scenario doesn't cross-fire into another; seeds new bookings via
/// the aggregate factory then reflection-overwrites
/// <c>CheckedOutAt</c> + <c>CompletionDueAt</c> to the precise values
/// each scenario needs (the aggregate would otherwise stamp them
/// from the clock, too coarse-grained for the past-vs-future sweep
/// predicate).</para>
///
/// <para>Cross-reference: manual smoke walk in
/// <c>docs/runbooks/turnover_walk.md</c>; close-out at
/// <c>docs/OPS_M_16_CLOSE_OUT.md</c> §4.</para>
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(TwoTenantApiCollection))]
public sealed class TurnoverAwareCompletionTests(TwoTenantApiFixture fixture)
{
    private static readonly Guid GuestUserId = Guid.Parse("cccccccc-1616-0000-0000-000000000001");

    private static Stay AnyStayEndingOn(DateTimeOffset checkoutInstant)
    {
        var checkout = DateOnly.FromDateTime(checkoutInstant.UtcDateTime);
        return new Stay(checkout.AddDays(-2), checkout);
    }

    private DomainBooking BuildBookingInCheckedOutState(
        DateTimeOffset checkedOutAt,
        DateTimeOffset? completionDueAt,
        int? turnoverHoursOverride = null)
    {
        var booking = DomainBooking.Place(
            tenantId: TwoTenantApiFixture.TenantA,
            propertyId: fixture.TenantAPropertyId,
            propertyTitle: "Tenant A's Villa",
            guestUserId: GuestUserId,
            guestDisplayName: "Turnover Guest",
            stay: AnyStayEndingOn(checkedOutAt),
            guestCount: 2,
            currency: "USD",
            subtotal: 360m,
            fees: 40m,
            taxes: 0m,
            total: 400m,
            lineItems: [],
            guests: [("Turnover Guest", true)],
            specialRequests: null);
        booking.Confirm();
        booking.CheckIn();
        booking.CheckOut(propertyTurnoverHours: 24);

        // Reflection-overwrite so the sweep predicate can operate against
        // deterministic instants rather than the aggregate's clock-driven
        // stamps. Load-bearing for the past-vs-future scenarios below.
        typeof(DomainBooking).GetProperty(nameof(DomainBooking.CheckedOutAt))!
            .SetValue(booking, checkedOutAt);
        typeof(DomainBooking).GetProperty(nameof(DomainBooking.CompletionDueAt))!
            .SetValue(booking, completionDueAt);
        if (turnoverHoursOverride is { } h)
        {
            typeof(DomainBooking).GetProperty(nameof(DomainBooking.TurnoverHoursOverride))!
                .SetValue(booking, h);
        }

        return booking;
    }

    private async Task<Guid> SeedAsync(DomainBooking booking)
    {
        using var scope = fixture.Services.CreateScope();
        using var _ = RlsBypassScope.Enter();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE booking.bookings CASCADE;");
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<DomainBooking> LoadAsync(Guid bookingId)
    {
        using var scope = fixture.Services.CreateScope();
        using var _ = RlsBypassScope.Enter();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        return await db.Bookings.AsNoTracking().FirstAsync(x => x.Id == bookingId);
    }

    private async Task<CompletionSweepResult> RunSweepAsync()
    {
        using var scope = fixture.Services.CreateScope();
        using var _ = RlsBypassScope.Enter();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(new CompletionSweepCommand());
    }

    [Fact]
    public async Task Sweep_completes_CheckedOut_booking_whose_CompletionDueAt_is_past()
    {
        var now = DateTimeOffset.UtcNow;
        var bookingId = await SeedAsync(BuildBookingInCheckedOutState(
            checkedOutAt: now.AddHours(-25),
            completionDueAt: now.AddHours(-1)));

        var result = await RunSweepAsync();

        result.Scanned.Should().BeGreaterThanOrEqualTo(1);
        result.Completed.Should().BeGreaterThanOrEqualTo(1);
        var reloaded = await LoadAsync(bookingId);
        reloaded.Status.Should().Be(BookingStatus.Completed,
            because: "CompletionDueAt was in the past; the sweep must transition CheckedOut → Completed.");
    }

    [Fact]
    public async Task Sweep_does_NOT_complete_CheckedOut_booking_whose_CompletionDueAt_is_future()
    {
        var now = DateTimeOffset.UtcNow;
        var bookingId = await SeedAsync(BuildBookingInCheckedOutState(
            checkedOutAt: now.AddHours(-1),
            completionDueAt: now.AddHours(6)));

        await RunSweepAsync();

        var reloaded = await LoadAsync(bookingId);
        reloaded.Status.Should().Be(BookingStatus.CheckedOut,
            because: "CompletionDueAt is in the future; the sweep must leave it.");
    }

    [Fact]
    public async Task Sweep_does_NOT_touch_CheckedOut_booking_with_null_CompletionDueAt()
    {
        var now = DateTimeOffset.UtcNow;
        var bookingId = await SeedAsync(BuildBookingInCheckedOutState(
            checkedOutAt: now.AddHours(-48),
            completionDueAt: null));

        await RunSweepAsync();

        var reloaded = await LoadAsync(bookingId);
        reloaded.Status.Should().Be(BookingStatus.CheckedOut,
            because: "CompletionDueAt IS NULL — defensively skipped. The M.16.2 backfill guarantees no NULLs exist in prod, but the predicate must be tolerant.");
    }

    [Fact]
    public async Task Sweep_reports_zero_scan_for_already_Completed_bookings()
    {
        var now = DateTimeOffset.UtcNow;
        var booking = BuildBookingInCheckedOutState(
            checkedOutAt: now.AddHours(-30),
            completionDueAt: now.AddHours(-6));
        booking.Complete();
        await SeedAsync(booking);

        var result = await RunSweepAsync();

        result.Scanned.Should().Be(0,
            because: "the predicate filters on Status=CheckedOut; already-Completed rows aren't in the scan.");
        result.Completed.Should().Be(0);
    }

    [Fact]
    public async Task Sweep_partitions_a_mixed_batch_into_past_due_and_future_due()
    {
        var now = DateTimeOffset.UtcNow;
        var pastDue = BuildBookingInCheckedOutState(
            checkedOutAt: now.AddHours(-30),
            completionDueAt: now.AddHours(-5));
        var futureDue = BuildBookingInCheckedOutState(
            checkedOutAt: now.AddHours(-2),
            completionDueAt: now.AddHours(4));

        // Seed BOTH in a single TRUNCATE-then-insert batch so they share the
        // sweep call's snapshot of the clock.
        using (var scope = fixture.Services.CreateScope())
        {
            using var _ = RlsBypassScope.Enter();
            var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
            await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE booking.bookings CASCADE;");
            db.Bookings.AddRange(pastDue, futureDue);
            await db.SaveChangesAsync();
        }

        var result = await RunSweepAsync();

        result.Scanned.Should().Be(1,
            because: "only the past-due row matches CompletionDueAt <= now.");
        result.Completed.Should().Be(1);

        var pastReloaded = await LoadAsync(pastDue.Id);
        var futureReloaded = await LoadAsync(futureDue.Id);
        pastReloaded.Status.Should().Be(BookingStatus.Completed);
        futureReloaded.Status.Should().Be(BookingStatus.CheckedOut,
            because: "future-due rows are left in place for the next sweep cycle.");
    }

    [Fact]
    public async Task CompletionDueAt_and_TurnoverHoursOverride_survive_persistence_roundtrip()
    {
        var now = DateTimeOffset.UtcNow;
        var expectedDueAt = now.AddHours(-2).AddHours(6);
        var booking = BuildBookingInCheckedOutState(
            checkedOutAt: now.AddHours(-2),
            completionDueAt: expectedDueAt,
            turnoverHoursOverride: 6);

        var id = await SeedAsync(booking);

        var reloaded = await LoadAsync(id);
        reloaded.CheckedOutAt.Should().NotBeNull();
        reloaded.CompletionDueAt.Should().NotBeNull();
        reloaded.CompletionDueAt!.Value.Should().BeCloseTo(expectedDueAt, TimeSpan.FromSeconds(1),
            because: "the EF Core mapping must roundtrip CompletionDueAt without precision loss beyond the DB's timestamp resolution.");
        reloaded.TurnoverHoursOverride.Should().Be(6,
            because: "the per-booking override survives persistence + reload. This is the load-bearing DB shape for M.16.");
    }
}
