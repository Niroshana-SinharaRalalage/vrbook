using FluentAssertions;
using VrBook.Modules.Payment.Infrastructure.Stripe;
using Xunit;

namespace VrBook.Api.IntegrationTests.Payment;

/// <summary>
/// VRB-105 (gap G38 / design C5) — the platform is the merchant-of-record on a
/// Connect destination charge. Funds + fee still route to the connected account
/// (<c>TransferData.Destination</c> + <c>ApplicationFeeAmount</c>), but
/// <c>OnBehalfOf</c> must NOT be set to the supplier — that would make the
/// supplier the settlement merchant + card-statement entity, contradicting the
/// marketplace-facilitator tax posture (VRB-103).
/// </summary>
[Trait("Category", "Unit")]
public sealed class StripeGatewayChargeTests
{
    private const string DestinationAccount = "acct_test_connected";

    [Fact]
    public void DestinationChargeDoesNotSetOnBehalfOf()
    {
        var opts = StripeGateway.BuildDestinationChargeOptions(
            amount: 100m, currency: "USD", metadata: null,
            destinationAccountId: DestinationAccount, applicationFeeAmount: 1500);

        opts.OnBehalfOf.Should().BeNull(
            because: "platform is merchant-of-record (VRB-105/G38); OnBehalfOf must not be the supplier account.");
    }

    [Fact]
    public void StillSetsTransferDataAndApplicationFee()
    {
        var opts = StripeGateway.BuildDestinationChargeOptions(
            amount: 100m, currency: "USD", metadata: null,
            destinationAccountId: DestinationAccount, applicationFeeAmount: 1500);

        opts.TransferData.Should().NotBeNull(because: "funds must still route to the connected account.");
        opts.TransferData!.Destination.Should().Be(DestinationAccount);
        opts.ApplicationFeeAmount.Should().Be(1500, because: "the platform fee must still be collected.");
    }

    [Fact]
    public void PlatformChargeSetsNeitherTransferNorFee()
    {
        // destinationAccountId null = a platform-only charge (no Connect split).
        var opts = StripeGateway.BuildDestinationChargeOptions(
            amount: 100m, currency: "USD", metadata: null,
            destinationAccountId: null, applicationFeeAmount: 0);

        opts.TransferData.Should().BeNull();
        opts.ApplicationFeeAmount.Should().BeNull();
        opts.OnBehalfOf.Should().BeNull();
    }
}
