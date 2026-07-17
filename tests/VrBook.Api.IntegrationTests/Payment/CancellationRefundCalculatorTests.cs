using FluentAssertions;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Payment.Application;
using Xunit;

namespace VrBook.Api.IntegrationTests.Payment;

/// <summary>
/// VRB-102 — the cancellation refund engine. Tiered + refundable-upgrade models,
/// resolved from the immutable per-booking snapshot (grounded in OPEN-QUESTIONS Q24).
/// </summary>
[Trait("Category", "Unit")]
public sealed class CancellationRefundCalculatorTests
{
    // 7 days full / 2..7 days 50% / <48h none.
    private static readonly GlobalCancellationTiers Tiers = new(
        FirstTierDays: 7, SecondTierDays: 2, MiddleTierRefundPct: 50, FinalCutoffHours: 48,
        UpgradePricePct: 8, Version: 3);

    private static CancellationPolicySnapshot Tiered() => CancellationPolicySnapshot.Tiered(Tiers);

    private static CancellationPolicySnapshot Upgrade(bool purchased) => new(
        CancellationModel.RefundableUpgrade,
        FirstTierDays: null, SecondTierDays: null, MiddleTierRefundPct: null, FinalCutoffHours: null, TierVersion: null,
        RefundableUpgradePurchased: purchased,
        RefundableUpgradePriceAmount: 12m, RefundableUpgradePriceCurrency: "USD");

    // ---- Tiered ----

    [Fact]
    public void FullRefundAtOrBeyondFirstTier()
    {
        CancellationRefundCalculator.Resolve(Tiered(), TimeSpan.FromDays(10), 100m).Should().Be(100m);
        CancellationRefundCalculator.Resolve(Tiered(), TimeSpan.FromDays(7), 100m).Should().Be(100m);
    }

    [Fact]
    public void PartialRefundInMiddleTier()
    {
        // 4 days out → in [2,7) → 50%.
        CancellationRefundCalculator.Resolve(Tiered(), TimeSpan.FromDays(4), 100m).Should().Be(50m);
        CancellationRefundCalculator.Resolve(Tiered(), TimeSpan.FromDays(2), 100m).Should().Be(50m);
    }

    [Fact]
    public void NoRefundInsideFinalCutoff()
    {
        // 24h out < 48h cutoff → none.
        CancellationRefundCalculator.Resolve(Tiered(), TimeSpan.FromHours(24), 100m).Should().Be(0m);
    }

    [Fact]
    public void NoRefundBelowMiddleTierButOutsideCutoff()
    {
        // 60h out (2.5 days? no — 60h = 2.5 days ≥ 2 days → middle). Use 50h: 50h > 48h cutoff,
        // 50h ≈ 2.08 days ≥ SecondTierDays(2) → still middle tier (50%). To land below the
        // middle band we need < 2 days AND ≥ 48h — impossible (2 days = 48h), so the bands are
        // contiguous: anything ≥48h and <2d does not exist. Assert the boundary instead:
        // exactly 48h (= 2 days) is the middle-tier floor.
        CancellationRefundCalculator.Resolve(Tiered(), TimeSpan.FromHours(48), 100m).Should().Be(50m);
        // 47h → inside cutoff → none.
        CancellationRefundCalculator.Resolve(Tiered(), TimeSpan.FromHours(47), 100m).Should().Be(0m);
    }

    [Fact]
    public void PartialRefundRoundsToTwoDecimals()
    {
        // 33% of 99.99 = 32.9967 → 33.00 (2dp).
        var tiers = Tiers with { MiddleTierRefundPct = 33 };
        CancellationRefundCalculator.Resolve(CancellationPolicySnapshot.Tiered(tiers), TimeSpan.FromDays(3), 99.99m)
            .Should().Be(33.00m);
    }

    // ---- Refundable upgrade ----

    [Fact]
    public void NonRefundableWithoutUpgrade()
    {
        CancellationRefundCalculator.Resolve(Upgrade(purchased: false), TimeSpan.FromDays(5), 100m).Should().Be(0m);
    }

    [Fact]
    public void FullRefundWhenUpgradedAndBeforeCheckIn()
    {
        CancellationRefundCalculator.Resolve(Upgrade(purchased: true), TimeSpan.FromDays(1), 100m).Should().Be(100m);
    }

    [Fact]
    public void NoRefundAfterCheckInEvenWithUpgrade()
    {
        CancellationRefundCalculator.Resolve(Upgrade(purchased: true), TimeSpan.FromHours(-1), 100m).Should().Be(0m);
    }
}
