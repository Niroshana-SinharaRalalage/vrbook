using FluentAssertions;
using VrBook.Contracts.Enums;
using VrBook.Modules.Loyalty;
using VrBook.Modules.Loyalty.Domain;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// VRB-206 (gap G1) — tier thresholds are config-driven via <see cref="LoyaltyThresholds"/>
/// (from <see cref="LoyaltyOptions"/>) instead of hard-coded consts. No Docker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LoyaltyTierResolutionTests
{
    [Fact]
    public void ConfiguredThresholds_Applied()
    {
        var thresholds = new LoyaltyThresholds(2, 5, 10);

        TierDefinition.Resolve(4, thresholds).Should().Be(LoyaltyTier.Bronze);
        TierDefinition.Resolve(5, thresholds).Should().Be(LoyaltyTier.Silver);
        TierDefinition.Resolve(9, thresholds).Should().Be(LoyaltyTier.Silver);
        TierDefinition.Resolve(10, thresholds).Should().Be(LoyaltyTier.Gold);
    }

    [Fact]
    public void MissingConfig_DefaultsTo1_3_6()
    {
        // LoyaltyOptions defaults reproduce the old hard-coded constants.
        var defaults = new LoyaltyOptions().ToThresholds();

        (defaults.Bronze, defaults.Silver, defaults.Gold).Should().Be((1, 3, 6));
        LoyaltyThresholds.Default.Should().Be(defaults);
        TierDefinition.Resolve(2, defaults).Should().Be(LoyaltyTier.Bronze);
        TierDefinition.Resolve(3, defaults).Should().Be(LoyaltyTier.Silver);
        TierDefinition.Resolve(6, defaults).Should().Be(LoyaltyTier.Gold);
    }

    [Fact]
    public void Boundary_ExactlyGoldThreshold_ReturnsGold()
    {
        TierDefinition.Resolve(6, LoyaltyThresholds.Default).Should().Be(LoyaltyTier.Gold);
        TierDefinition.Resolve(5, LoyaltyThresholds.Default).Should().Be(LoyaltyTier.Silver);
    }

    [Fact]
    public void RecordCompletedStay_UsesConfiguredThresholds()
    {
        var account = LoyaltyAccount.OpenForUser(Guid.NewGuid());
        var thresholds = new LoyaltyThresholds(1, 2, 3); // faster tiers than default

        account.RecordCompletedStay(thresholds); // 1 → Bronze
        account.RecordCompletedStay(thresholds); // 2 → Silver
        account.Tier.Should().Be(LoyaltyTier.Silver);

        account.RecordCompletedStay(thresholds); // 3 → Gold
        account.Tier.Should().Be(LoyaltyTier.Gold);
    }

    [Fact]
    public void NextTier_UsesConfiguredThresholds()
    {
        var thresholds = new LoyaltyThresholds(1, 4, 8);

        var (next, until) = TierDefinition.NextTier(2, thresholds);
        next.Should().Be(LoyaltyTier.Silver);
        until.Should().Be(2); // 4 - 2
    }

    [Theory]
    [InlineData(0, 2, 3)]   // Bronze < 1
    [InlineData(1, 1, 3)]   // Bronze == Silver
    [InlineData(1, 5, 5)]   // Silver == Gold
    [InlineData(3, 2, 6)]   // Bronze > Silver
    public void InvalidThresholds_FailValidation(int bronze, int silver, int gold)
    {
        var options = new LoyaltyOptions { BronzeThreshold = bronze, SilverThreshold = silver, GoldThreshold = gold };

        var result = new LoyaltyOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Bronze < Silver < Gold");
    }
}
