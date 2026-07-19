using FluentAssertions;
using VrBook.Modules.Sync.Application.ChannelFeeds.Commands;
using Xunit;

namespace VrBook.Api.IntegrationTests.Configuration;

/// <summary>
/// VRB-214 — the inbound-feed poll cadence must be validated to a sane range
/// (15–1440 min) on the availability-settings write. No Docker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FeedCadenceValidationTests
{
    private static SetFeedCadenceCommand Cmd(int minutes) =>
        new(FeedId: Guid.NewGuid(), PollIntervalMinutes: minutes, TenantId: Guid.NewGuid());

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(720)]
    [InlineData(1440)]
    public void InRange_Passes(int minutes)
    {
        new SetFeedCadenceValidator().Validate(Cmd(minutes)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(14)]   // below floor
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1441)] // above ceiling
    [InlineData(10080)]
    public void OutOfRange_Fails(int minutes)
    {
        var result = new SetFeedCadenceValidator().Validate(Cmd(minutes));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SetFeedCadenceCommand.PollIntervalMinutes));
    }

    [Fact]
    public void EmptyFeedId_Fails()
    {
        new SetFeedCadenceValidator()
            .Validate(new SetFeedCadenceCommand(Guid.Empty, 30, Guid.NewGuid()))
            .IsValid.Should().BeFalse();
    }
}
