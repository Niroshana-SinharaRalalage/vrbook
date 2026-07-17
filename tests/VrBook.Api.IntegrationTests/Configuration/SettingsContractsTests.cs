using FluentAssertions;
using Microsoft.Extensions.Configuration;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Settings;
using Xunit;

namespace VrBook.Api.IntegrationTests.Configuration;

/// <summary>
/// VRB-216 Phase A — the §3 settings contract + its config-backed default impls (the
/// PAY VRB-102 unblock). Proves the config→DB swap boundary behaves: defaults seed
/// 7/2/50/48, config overrides bind, and the policy resolver produces a Tiered
/// snapshot PAY can compute refunds from. No Docker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SettingsContractsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => (string?)p.Value))
            .Build();

    [Fact]
    public void TierProvider_NoConfig_ReturnsSeedDefaults()
    {
        var tiers = new ConfigCancellationTierProvider(Config()).GetActiveAsync().Result;

        (tiers.FirstTierDays, tiers.SecondTierDays, tiers.MiddleTierRefundPct, tiers.FinalCutoffHours)
            .Should().Be((7, 2, 50, 48));
        tiers.Version.Should().Be(0);
        tiers.Should().Be(GlobalCancellationTiers.Default);
    }

    [Fact]
    public void TierProvider_Config_Overrides()
    {
        var tiers = new ConfigCancellationTierProvider(Config(
            ("Cancellation:Tiers:FirstTierDays", "14"),
            ("Cancellation:Tiers:SecondTierDays", "7"),
            ("Cancellation:Tiers:MiddleTierRefundPct", "40"),
            ("Cancellation:Tiers:FinalCutoffHours", "72"))).GetActiveAsync().Result;

        (tiers.FirstTierDays, tiers.SecondTierDays, tiers.MiddleTierRefundPct, tiers.FinalCutoffHours)
            .Should().Be((14, 7, 40, 72));
    }

    [Fact]
    public async Task PolicyResolver_ReturnsTieredSnapshot_FromActiveTiers()
    {
        var resolver = new ConfigCancellationPolicyResolver(new ConfigCancellationTierProvider(Config()));

        var snap = await resolver.ResolveAsync(Guid.NewGuid(), Guid.NewGuid());

        snap.Model.Should().Be(CancellationModel.Tiered);
        (snap.FirstTierDays, snap.SecondTierDays, snap.MiddleTierRefundPct, snap.FinalCutoffHours)
            .Should().Be((7, 2, 50, 48));
        snap.TierVersion.Should().Be(0);
        snap.RefundableUpgradePurchased.Should().BeFalse();
        snap.RefundableUpgradePriceAmount.Should().BeNull();
    }

    [Fact]
    public async Task FeeResolver_DefaultsTo1500_OverridableByConfig()
    {
        (await new ConfigPlatformFeeResolver(Config()).GetFeeBpsAsync(Guid.NewGuid())).Should().Be(1500);
        (await new ConfigPlatformFeeResolver(Config(("Payment:PlatformFeeBps", "1200")))
            .GetFeeBpsAsync(Guid.NewGuid())).Should().Be(1200);
    }

    [Fact]
    public async Task TaxPosture_DefaultsTo_FacilitatorActive_EmptyRoster()
    {
        var posture = await new ConfigTaxPostureProvider().GetAsync();

        posture.FacilitatorActive.Should().BeTrue();
        posture.PerStateEnabled.Should().BeEmpty();
    }

    [Fact]
    public void CancellationModel_HasExactlyTheTwoLaunchModels()
    {
        Enum.GetNames<CancellationModel>().Should().BeEquivalentTo("Tiered", "RefundableUpgrade");
    }
}
