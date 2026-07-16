using FluentAssertions;
using VrBook.Contracts.Enums;
using VrBook.Modules.Booking;
using VrBook.Modules.Booking.Domain;
using Xunit;
using DomainBooking = VrBook.Modules.Booking.Domain.Booking;

namespace VrBook.Api.IntegrationTests.Configuration;

/// <summary>
/// VRB-207 (gap G2 / Q1) — the Tentative-booking hold window is config-driven with
/// the owner-locked default of 48h, replacing the hard-coded <c>AddHours(24)</c> in
/// <see cref="DomainBooking.Place"/>. No Docker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TentativeSlaTests
{
    private static readonly Guid AnyId = new("00000000-0000-0000-0000-000000000001");

    private static DomainBooking PlaceWith(TimeSpan sla) =>
        DomainBooking.Place(
            tenantId: AnyId,
            propertyId: AnyId,
            propertyTitle: "Test",
            guestUserId: AnyId,
            guestDisplayName: "Guest",
            stay: new Stay(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 3)),
            guestCount: 1,
            currency: "usd",
            subtotal: 0, fees: 0, taxes: 0, total: 0,
            lineItems: [], guests: [], specialRequests: null,
            tentativeSla: sla);

    [Fact]
    public void DefaultSla_Is48Hours()
    {
        var options = new BookingSlaOptions();

        options.TentativeSlaHours.Should().Be(48);
        options.TentativeSla.Should().Be(TimeSpan.FromHours(48));
    }

    [Fact]
    public void ConfiguredSla_Overrides()
    {
        var options = new BookingSlaOptions { TentativeSlaHours = 72 };

        options.TentativeSla.Should().Be(TimeSpan.FromHours(72));
    }

    [Fact]
    public void Place_SetsTentativeUntil_FromConfig()
    {
        var before = DateTimeOffset.UtcNow;

        var booking = PlaceWith(TimeSpan.FromHours(48));

        booking.TentativeUntil.Should().NotBeNull();
        booking.TentativeUntil!.Value.Should().BeOnOrAfter(before.AddHours(48).AddSeconds(-1));
        booking.TentativeUntil!.Value.Should().BeOnOrBefore(DateTimeOffset.UtcNow.AddHours(48).AddSeconds(1));
        booking.Status.Should().Be(BookingStatus.Tentative);
    }

    [Fact]
    public void Place_HonoursNonDefaultSla()
    {
        var before = DateTimeOffset.UtcNow;

        var booking = PlaceWith(TimeSpan.FromHours(6));

        booking.TentativeUntil!.Value.Should().BeOnOrBefore(before.AddHours(6).AddSeconds(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositiveSla_FailsValidation(int hours)
    {
        var result = new BookingSlaOptionsValidator()
            .Validate(null, new BookingSlaOptions { TentativeSlaHours = hours });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("TentativeSlaHours");
    }
}
