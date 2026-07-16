using FluentAssertions;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Modules.Loyalty.Domain;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// Slice 5 — unit tests for the LoyaltyAccount aggregate's tier-promotion semantics.
/// Run in the Category=Unit step of CI; no Docker required.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LoyaltyAccountAggregateTests
{
    private static readonly Guid AnyUser = Guid.NewGuid();

    [Fact]
    public void First_completed_stay_records_Bronze_without_promotion_event()
    {
        var a = LoyaltyAccount.OpenForUser(AnyUser);

        a.RecordCompletedStay(LoyaltyThresholds.Default);

        a.CompletedStayCount.Should().Be(1);
        a.Tier.Should().Be(LoyaltyTier.Bronze);
        a.DequeueEvents().Should().BeEmpty(
            "first stay stays at Bronze (already the opening tier); no TierPromoted should fire");
    }

    [Fact]
    public void Third_completed_stay_promotes_Bronze_to_Silver()
    {
        var a = LoyaltyAccount.OpenForUser(AnyUser);
        a.RecordCompletedStay(LoyaltyThresholds.Default); // 1, Bronze
        a.RecordCompletedStay(LoyaltyThresholds.Default); // 2, Bronze
        a.DequeueEvents(); // drain any prior events

        a.RecordCompletedStay(LoyaltyThresholds.Default); // 3, Silver

        a.CompletedStayCount.Should().Be(3);
        a.Tier.Should().Be(LoyaltyTier.Silver);
        var events = a.DequeueEvents();
        events.Should().HaveCount(1);
        var promoted = events.OfType<TierPromoted>().Single();
        promoted.UserId.Should().Be(AnyUser);
        promoted.FromTier.Should().Be(LoyaltyTier.Bronze);
        promoted.ToTier.Should().Be(LoyaltyTier.Silver);
        promoted.CompletedStayCount.Should().Be(3);
    }

    [Fact]
    public void Sixth_completed_stay_promotes_Silver_to_Gold()
    {
        var a = LoyaltyAccount.OpenForUser(AnyUser);
        for (var i = 1; i <= 5; i++)
        {
            a.RecordCompletedStay(LoyaltyThresholds.Default);
        }
        a.DequeueEvents();

        a.RecordCompletedStay(LoyaltyThresholds.Default); // 6, Gold

        a.Tier.Should().Be(LoyaltyTier.Gold);
        var promoted = a.DequeueEvents().OfType<TierPromoted>().Single();
        promoted.FromTier.Should().Be(LoyaltyTier.Silver);
        promoted.ToTier.Should().Be(LoyaltyTier.Gold);
        promoted.CompletedStayCount.Should().Be(6);
    }

    [Fact]
    public void Stays_within_same_tier_do_not_promote()
    {
        var a = LoyaltyAccount.OpenForUser(AnyUser);
        a.RecordCompletedStay(LoyaltyThresholds.Default); // 1 Bronze
        a.RecordCompletedStay(LoyaltyThresholds.Default); // 2 Bronze
        a.RecordCompletedStay(LoyaltyThresholds.Default); // 3 Silver  <-- this raises one event
        a.DequeueEvents();

        a.RecordCompletedStay(LoyaltyThresholds.Default); // 4 Silver
        a.RecordCompletedStay(LoyaltyThresholds.Default); // 5 Silver

        a.Tier.Should().Be(LoyaltyTier.Silver);
        a.CompletedStayCount.Should().Be(5);
        a.DequeueEvents().Should().BeEmpty();
    }

    [Fact]
    public void Gold_is_terminal_no_further_promotions()
    {
        var a = LoyaltyAccount.OpenForUser(AnyUser);
        for (var i = 1; i <= 6; i++)
        {
            a.RecordCompletedStay(LoyaltyThresholds.Default);
        }
        a.DequeueEvents();

        for (var i = 7; i <= 10; i++)
        {
            a.RecordCompletedStay(LoyaltyThresholds.Default);
            a.DequeueEvents().Should().BeEmpty($"stay #{i} stays at Gold");
        }
        a.Tier.Should().Be(LoyaltyTier.Gold);
        a.CompletedStayCount.Should().Be(10);
    }
}
