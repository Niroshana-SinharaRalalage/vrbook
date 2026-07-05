using FluentAssertions;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Domain.Common;
using VrBook.Modules.Booking.Domain;
using Xunit;
using DomainBooking = VrBook.Modules.Booking.Domain.Booking;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// Unit tests for the Booking aggregate state machine — every transition + every guard
/// from proposal §7.1. Exercises domain code directly without API host or DB. Run in the
/// Category=Unit step of CI; no Docker required.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BookingAggregateTests
{
    private static Stay AnyStay() => new(
        new DateOnly(2026, 8, 1),
        new DateOnly(2026, 8, 4));

    private static DomainBooking PlaceTentative() =>
        DomainBooking.Place(new Guid("00000000-0000-0000-0000-000000000001"),
            propertyId: Guid.NewGuid(),
            propertyTitle: "Mountain Cabin",
            guestUserId: Guid.NewGuid(),
            guestDisplayName: "Test Guest",
            stay: AnyStay(),
            guestCount: 2,
            currency: "USD",
            subtotal: 360m,
            fees: 40m,
            taxes: 43.20m,
            total: 443.20m,
            lineItems: [],
            guests: [("Test Guest", true)],
            specialRequests: null);

    private static DomainBooking PlaceConfirmed()
    {
        var b = PlaceTentative();
        b.DequeueEvents();
        b.Confirm();
        return b;
    }

    private static DomainBooking PlaceCheckedIn()
    {
        var b = PlaceConfirmed();
        b.DequeueEvents();
        b.CheckIn();
        return b;
    }

    // ----- Place -----

    [Fact]
    public void Place_creates_tentative_booking_with_24h_window_and_raises_BookingPlaced()
    {
        var before = DateTimeOffset.UtcNow;
        var booking = PlaceTentative();

        booking.Status.Should().Be(BookingStatus.Tentative);
        booking.Reference.Should().StartWith("VRB-");
        booking.TentativeUntil.Should().NotBeNull();
        booking.TentativeUntil!.Value.Should().BeOnOrAfter(before.AddHours(24).AddSeconds(-1));
        booking.TentativeUntil!.Value.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddHours(24).AddSeconds(1));
        booking.Currency.Should().Be("USD");
        booking.Source.Should().Be(BookingSource.Direct);

        var events = booking.DequeueEvents();
        events.Should().ContainSingle().Which.Should().BeOfType<BookingPlaced>();
    }

    [Fact]
    public void Place_lowercases_currency_to_canonical_upper()
    {
        var booking = DomainBooking.Place(new Guid("00000000-0000-0000-0000-000000000001"),
            propertyId: Guid.NewGuid(), propertyTitle: "X", guestUserId: Guid.NewGuid(),
            guestDisplayName: "G", stay: AnyStay(), guestCount: 1, currency: "usd",
            subtotal: 0, fees: 0, taxes: 0, total: 0, lineItems: [], guests: [], specialRequests: null);

        booking.Currency.Should().Be("USD");
    }

    [Fact]
    public void Place_with_empty_property_title_throws()
    {
        var act = () => DomainBooking.Place(new Guid("00000000-0000-0000-0000-000000000001"),
            propertyId: Guid.NewGuid(), propertyTitle: "", guestUserId: Guid.NewGuid(),
            guestDisplayName: "G", stay: AnyStay(), guestCount: 1, currency: "USD",
            subtotal: 0, fees: 0, taxes: 0, total: 0, lineItems: [], guests: [], specialRequests: null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Place_with_zero_guests_throws()
    {
        var act = () => DomainBooking.Place(new Guid("00000000-0000-0000-0000-000000000001"),
            propertyId: Guid.NewGuid(), propertyTitle: "X", guestUserId: Guid.NewGuid(),
            guestDisplayName: "G", stay: AnyStay(), guestCount: 0, currency: "USD",
            subtotal: 0, fees: 0, taxes: 0, total: 0, lineItems: [], guests: [], specialRequests: null);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Place_skips_guests_with_empty_names()
    {
        var booking = DomainBooking.Place(new Guid("00000000-0000-0000-0000-000000000001"),
            propertyId: Guid.NewGuid(), propertyTitle: "X", guestUserId: Guid.NewGuid(),
            guestDisplayName: "G", stay: AnyStay(), guestCount: 2, currency: "USD",
            subtotal: 0, fees: 0, taxes: 0, total: 0, lineItems: [],
            guests: new (string, bool)[] { ("Alice", true), ("", false), ("   ", false), ("Bob", false) },
            specialRequests: null);

        booking.Guests.Should().HaveCount(2);
        booking.Guests.Select(g => g.FullName).Should().BeEquivalentTo("Alice", "Bob");
    }

    // ----- Confirm -----

    [Fact]
    public void Confirm_from_tentative_transitions_and_clears_tentative_until()
    {
        var b = PlaceTentative();
        b.DequeueEvents();

        b.Confirm();

        b.Status.Should().Be(BookingStatus.Confirmed);
        b.ConfirmedAt.Should().NotBeNull();
        b.TentativeUntil.Should().BeNull();
        b.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<BookingConfirmed>();
    }

    [Theory]
    [InlineData(nameof(DomainBooking.Confirm))]
    [InlineData(nameof(DomainBooking.CheckIn))]
    [InlineData(nameof(DomainBooking.CheckOut))]
    public void Cannot_invoke_state_op_when_status_mismatched(string opName)
    {
        var b = PlaceTentative();
        b.DequeueEvents();

        Action op = opName switch
        {
            nameof(DomainBooking.CheckIn) => () => b.CheckIn(),     // Tentative != Confirmed
            nameof(DomainBooking.CheckOut) => () => b.CheckOut(propertyTurnoverHours: 24),   // Tentative != CheckedIn
            _ => () => b.Reject("test"),                            // just to satisfy switch
        };

        if (opName == nameof(DomainBooking.Confirm))
        {
            b.Confirm();
            op = () => b.Confirm(); // already Confirmed
        }
        op.Should().Throw<BusinessRuleViolationException>()
          .WithMessage("*booking.state*");
    }

    // ----- Reject -----

    [Fact]
    public void Reject_from_tentative_transitions_and_records_reason()
    {
        var b = PlaceTentative();
        b.DequeueEvents();

        b.Reject("No availability");

        b.Status.Should().Be(BookingStatus.Rejected);
        b.CancelledAt.Should().NotBeNull();
        b.CancellationReason.Should().Be("No availability");
        b.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<BookingRejected>();
    }

    [Fact]
    public void Reject_with_blank_reason_uses_default_text()
    {
        var b = PlaceTentative();
        b.Reject("   ");
        b.CancellationReason.Should().Be("Rejected by host");
    }

    [Fact]
    public void Reject_from_confirmed_throws_state_violation()
    {
        var b = PlaceConfirmed();
        var act = () => b.Reject("too late");
        act.Should().Throw<BusinessRuleViolationException>()
           .WithMessage("*booking.state*");
    }

    // ----- CancelByGuest -----

    [Fact]
    public void CancelByGuest_from_tentative_transitions_to_cancelled()
    {
        var b = PlaceTentative();
        b.DequeueEvents();

        b.CancelByGuest("plans changed");

        b.Status.Should().Be(BookingStatus.Cancelled);
        b.CancelledAt.Should().NotBeNull();
        b.CancellationReason.Should().Be("plans changed");
        b.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<BookingCancelled>();
    }

    [Fact]
    public void CancelByGuest_from_confirmed_transitions_to_cancelled()
    {
        var b = PlaceConfirmed();
        b.DequeueEvents();

        b.CancelByGuest("");

        b.Status.Should().Be(BookingStatus.Cancelled);
        b.CancellationReason.Should().Be("Cancelled by guest");
    }

    [Theory]
    [InlineData("CheckedIn")]
    [InlineData("CheckedOut")]
    [InlineData("Cancelled")]
    [InlineData("Rejected")]
    public void CancelByGuest_from_non_cancellable_state_throws(string priorState)
    {
        var b = priorState switch
        {
            "CheckedIn" => PlaceCheckedIn(),
            "CheckedOut" => CheckedOut(),
            "Cancelled" => Cancelled(),
            _ => Rejected(),
        };

        var act = () => b.CancelByGuest("late");
        act.Should().Throw<BusinessRuleViolationException>()
           .WithMessage("*booking.cancel*");
    }

    // ----- CheckIn -----

    [Fact]
    public void CheckIn_from_confirmed_transitions_and_records_timestamp()
    {
        var b = PlaceConfirmed();
        b.DequeueEvents();

        b.CheckIn();

        b.Status.Should().Be(BookingStatus.CheckedIn);
        b.CheckedInAt.Should().NotBeNull();
        b.DequeueEvents().Should().ContainSingle().Which.Should().BeOfType<BookingCheckedIn>();
    }

    [Fact]
    public void CheckIn_from_tentative_throws()
    {
        var b = PlaceTentative();
        var act = () => b.CheckIn();
        act.Should().Throw<BusinessRuleViolationException>()
           .WithMessage("*booking.state*");
    }

    // ----- CheckOut -----

    [Fact]
    public void CheckOut_from_checked_in_transitions_and_records_timestamp()
    {
        var b = PlaceCheckedIn();
        b.DequeueEvents();

        b.CheckOut(propertyTurnoverHours: 24);

        b.Status.Should().Be(BookingStatus.CheckedOut);
        b.CheckedOutAt.Should().NotBeNull();
        // Slice 5: CheckOut no longer raises BookingCompleted — the daily
        // BookingCompletionWorker (cron 0 6 * * *) calls Booking.Complete()
        // at least 24h after CheckOut, which is the new sole trigger.
        var events = b.DequeueEvents();
        events.Select(e => e.GetType().Name).Should().Equal(
            nameof(BookingCheckedOut));
    }

    [Fact]
    public void CheckOut_from_confirmed_throws()
    {
        var b = PlaceConfirmed();
        var act = () => b.CheckOut(propertyTurnoverHours: 24);
        act.Should().Throw<BusinessRuleViolationException>();
    }

    // ----- Complete -----

    [Fact]
    public void Complete_from_checked_out_transitions_and_raises_BookingCompleted()
    {
        var b = CheckedOut();
        b.DequeueEvents();

        b.Complete();

        b.Status.Should().Be(BookingStatus.Completed);
        var events = b.DequeueEvents();
        events.Select(e => e.GetType().Name).Should().Equal(nameof(BookingCompleted));
        var completed = events.OfType<BookingCompleted>().Single();
        completed.BookingId.Should().Be(b.Id);
        completed.Reference.Should().Be(b.Reference);
        completed.GuestUserId.Should().Be(b.GuestUserId);
    }

    [Fact]
    public void Complete_from_checked_in_throws()
    {
        var b = PlaceCheckedIn();
        var act = () => b.Complete();
        act.Should().Throw<BusinessRuleViolationException>();
    }

    [Fact]
    public void Complete_from_completed_throws()
    {
        var b = CheckedOut();
        b.Complete();
        var act = () => b.Complete();
        act.Should().Throw<BusinessRuleViolationException>();
    }

    // ----- Slice OPS.M.16 turnover-aware completion -----

    [Fact]
    public void CheckOut_stamps_CompletionDueAt_using_property_turnover_hours()
    {
        var b = PlaceCheckedIn();
        b.DequeueEvents();

        b.CheckOut(propertyTurnoverHours: 36);

        b.CompletionDueAt.Should().NotBeNull();
        b.CompletionDueAt!.Value.Should().BeCloseTo(
            b.CheckedOutAt!.Value.AddHours(36),
            TimeSpan.FromSeconds(1));
        b.TurnoverHoursOverride.Should().BeNull("no override set — property default applied");
    }

    [Fact]
    public void CheckOut_uses_zero_turnover_hours_when_property_default_is_zero()
    {
        var b = PlaceCheckedIn();
        b.CheckOut(propertyTurnoverHours: 0);
        b.CompletionDueAt.Should().BeCloseTo(b.CheckedOutAt!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CheckOut_rejects_negative_propertyTurnoverHours()
    {
        var b = PlaceCheckedIn();
        var act = () => b.CheckOut(propertyTurnoverHours: -1);
        act.Should().Throw<BusinessRuleViolationException>()
            .WithMessage("*turnover_hours_negative*");
    }

    [Fact]
    public void ScheduleCompletion_recomputes_CompletionDueAt_from_CheckedOutAt()
    {
        var b = PlaceCheckedIn();
        b.CheckOut(propertyTurnoverHours: 24);
        b.DequeueEvents();

        b.ScheduleCompletion(hoursFromCheckedOutAt: 48);

        b.TurnoverHoursOverride.Should().Be(48);
        b.CompletionDueAt.Should().BeCloseTo(b.CheckedOutAt!.Value.AddHours(48), TimeSpan.FromSeconds(1));
        var evt = b.DequeueEvents().OfType<BookingCompletionRescheduled>().Single();
        evt.BookingId.Should().Be(b.Id);
        evt.HoursFromCheckedOutAt.Should().Be(48);
        evt.DueAt.Should().Be(b.CompletionDueAt!.Value);
        evt.TenantId.Should().Be(b.TenantId);
    }

    [Fact]
    public void ScheduleCompletion_requires_CheckedOut_state()
    {
        var b = PlaceCheckedIn();
        var act = () => b.ScheduleCompletion(24);
        act.Should().Throw<BusinessRuleViolationException>()
            .WithMessage("*Cannot transition from CheckedIn*");
    }

    [Fact]
    public void ScheduleCompletion_rejects_negative_hours()
    {
        var b = PlaceCheckedIn();
        b.CheckOut(propertyTurnoverHours: 24);
        var act = () => b.ScheduleCompletion(-1);
        act.Should().Throw<BusinessRuleViolationException>()
            .WithMessage("*turnover_hours_out_of_range*");
    }

    [Fact]
    public void ScheduleCompletion_rejects_hours_over_upper_bound_168()
    {
        var b = PlaceCheckedIn();
        b.CheckOut(propertyTurnoverHours: 24);
        var act = () => b.ScheduleCompletion(169);
        act.Should().Throw<BusinessRuleViolationException>()
            .WithMessage("*turnover_hours_out_of_range*");
    }

    [Fact]
    public void ScheduleCompletion_accepts_boundary_values_0_and_168()
    {
        var b = PlaceCheckedIn();
        b.CheckOut(propertyTurnoverHours: 24);

        b.ScheduleCompletion(0);
        b.TurnoverHoursOverride.Should().Be(0);

        b.ScheduleCompletion(168);
        b.TurnoverHoursOverride.Should().Be(168);
        b.CompletionDueAt.Should().BeCloseTo(b.CheckedOutAt!.Value.AddHours(168), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void CompleteManually_from_CheckedOut_transitions_and_raises_manual_trigger()
    {
        var b = PlaceCheckedIn();
        b.CheckOut(propertyTurnoverHours: 24);
        b.DequeueEvents();

        b.CompleteManually();

        b.Status.Should().Be(BookingStatus.Completed);
        var evt = b.DequeueEvents().OfType<BookingCompleted>().Single();
        evt.Trigger.Should().Be("manual");
    }

    [Fact]
    public void CompleteManually_from_CheckedIn_throws()
    {
        var b = PlaceCheckedIn();
        var act = () => b.CompleteManually();
        act.Should().Throw<BusinessRuleViolationException>();
    }

    [Fact]
    public void Complete_from_sweep_raises_sweep_trigger()
    {
        var b = PlaceCheckedIn();
        b.CheckOut(propertyTurnoverHours: 24);
        b.DequeueEvents();

        b.Complete();

        var evt = b.DequeueEvents().OfType<BookingCompleted>().Single();
        evt.Trigger.Should().Be("sweep");
    }

    // ----- Full happy-path lifecycle -----

    [Fact]
    public void Happy_path_Tentative_to_Completed_raises_events_in_order()
    {
        var b = PlaceTentative();
        b.Confirm();
        b.CheckIn();
        b.CheckOut(propertyTurnoverHours: 24);
        b.Complete();

        var events = b.DequeueEvents();
        events.Select(e => e.GetType().Name).Should().Equal(
            nameof(BookingPlaced),
            nameof(BookingConfirmed),
            nameof(BookingCheckedIn),
            nameof(BookingCheckedOut),
            nameof(BookingCompleted));
        b.Status.Should().Be(BookingStatus.Completed);
    }

    // ----- Helpers to construct non-public terminal states -----

    private static DomainBooking CheckedOut()
    {
        var b = PlaceCheckedIn();
        b.CheckOut(propertyTurnoverHours: 24);
        return b;
    }

    private static DomainBooking Cancelled()
    {
        var b = PlaceTentative();
        b.CancelByGuest("test");
        return b;
    }

    private static DomainBooking Rejected()
    {
        var b = PlaceTentative();
        b.Reject("test");
        return b;
    }
}
