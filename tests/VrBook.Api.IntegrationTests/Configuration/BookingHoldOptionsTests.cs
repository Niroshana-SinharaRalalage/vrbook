using FluentAssertions;
using VrBook.Modules.Booking;
using Xunit;

namespace VrBook.Api.IntegrationTests.Configuration;

/// <summary>
/// VRB-208 (gap G3) — the retained <c>Booking:HoldDurationMinutes</c> key now has a
/// real typed consumer (<see cref="BookingHoldOptions"/>) feeding the checkout-hold
/// TTL, instead of being dead config. No Docker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BookingHoldOptionsTests
{
    [Fact]
    public void Default_ReproducesOld15MinuteConstant()
    {
        var options = new BookingHoldOptions();

        options.HoldDurationMinutes.Should().Be(15);
        options.HoldTtl.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void ConfiguredValue_FlowsToTtl()
    {
        var options = new BookingHoldOptions { HoldDurationMinutes = 30 };

        options.HoldTtl.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositiveDuration_FailsValidation(int minutes)
    {
        var result = new BookingHoldOptionsValidator()
            .Validate(null, new BookingHoldOptions { HoldDurationMinutes = minutes });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("HoldDurationMinutes");
    }

    [Fact]
    public void ValidDuration_PassesValidation()
    {
        var result = new BookingHoldOptionsValidator()
            .Validate(null, new BookingHoldOptions { HoldDurationMinutes = 15 });

        result.Succeeded.Should().BeTrue();
    }
}
