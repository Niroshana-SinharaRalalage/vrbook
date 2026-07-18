using FluentAssertions;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Catalog.Application.Properties.Commands;
using Xunit;

namespace VrBook.Api.IntegrationTests.Configuration;

/// <summary>
/// VRB-215 — the per-property cancellation model: the resolver's snapshot factories
/// (the shape PAY's Place consumes) + the set-model command validation. No Docker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PropertyCancellationTests
{
    private static readonly GlobalCancellationTiers Tiers = new(7, 2, 50, 48, UpgradePricePct: 8, Version: 3);

    [Fact]
    public void TieredSnapshot_CarriesTiers_NoUpgrade()
    {
        var s = CancellationPolicySnapshot.Tiered(Tiers);

        s.Model.Should().Be(CancellationModel.Tiered);
        (s.FirstTierDays, s.SecondTierDays, s.MiddleTierRefundPct, s.FinalCutoffHours, s.TierVersion)
            .Should().Be((7, 2, 50, 48, 3));
        s.UpgradePricePct.Should().BeNull();
        s.RefundableUpgradePurchased.Should().BeFalse();
        s.RefundableUpgradePriceAmount.Should().BeNull();
    }

    [Fact]
    public void RefundableUpgradeSnapshot_CarriesUpgradePct_AmountLeftForBooking()
    {
        var s = CancellationPolicySnapshot.RefundableUpgrade(Tiers);

        s.Model.Should().Be(CancellationModel.RefundableUpgrade);
        s.UpgradePricePct.Should().Be(8);           // Booking computes amount = subtotal × 8/100
        s.TierVersion.Should().Be(3);               // provenance
        s.FirstTierDays.Should().BeNull();          // tier schedule doesn't apply
        s.RefundableUpgradePriceAmount.Should().BeNull(); // Booking's Place finalizes this
        s.RefundableUpgradePurchased.Should().BeFalse();
    }

    [Fact]
    public void Snapshot_UpgradePricePct_DefaultsNull_ForBackwardCompatibleConstruction()
    {
        // PAY constructs the snapshot positionally without the appended param — must compile + default null.
        var s = new CancellationPolicySnapshot(
            CancellationModel.Tiered, 7, 2, 50, 48, 1, false, null, null);
        s.UpgradePricePct.Should().BeNull();
    }

    [Fact]
    public void SetModel_Valid_Passes()
    {
        var r = new SetPropertyCancellationModelValidator()
            .Validate(new SetPropertyCancellationModelCommand(Guid.NewGuid(), Guid.NewGuid(), CancellationModel.RefundableUpgrade));
        r.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, true)]   // empty tenant
    [InlineData(true, false)]   // empty property
    public void SetModel_EmptyIds_Fails(bool tenantSet, bool propertySet)
    {
        var cmd = new SetPropertyCancellationModelCommand(
            tenantSet ? Guid.NewGuid() : Guid.Empty,
            propertySet ? Guid.NewGuid() : Guid.Empty,
            CancellationModel.Tiered);
        new SetPropertyCancellationModelValidator().Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void SetModel_UndefinedEnum_Fails()
    {
        var cmd = new SetPropertyCancellationModelCommand(Guid.NewGuid(), Guid.NewGuid(), (CancellationModel)99);
        new SetPropertyCancellationModelValidator().Validate(cmd).IsValid.Should().BeFalse();
    }
}
