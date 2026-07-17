using FluentAssertions;
using VrBook.Modules.Payment.Infrastructure.Stripe;
using Xunit;

namespace VrBook.Api.IntegrationTests.Payment;

/// <summary>
/// VRB-104 (gap G37) — refund options shape. A Connect refund sets
/// <c>RefundApplicationFee=true</c>, which makes Stripe reverse the application
/// fee proportionally (full for a full refund, pro-rata for a partial). The
/// actual reversed cents are read back from Stripe + persisted (see
/// <see cref="DestinationChargeMoRTests"/>'s sibling integration test); no
/// misleading <c>application_fee_refund_cents</c> metadata is written anymore.
/// </summary>
[Trait("Category", "Unit")]
public sealed class StripeGatewayRefundTests
{
    private const string Pi = "pi_test_123";

    [Fact]
    public void FullRefundSetsRefundApplicationFeeTrue()
    {
        var opts = StripeGateway.BuildRefundOptions(Pi, amount: null, refundApplicationFee: true);

        opts.RefundApplicationFee.Should().Be(true, "a full refund reverses the whole platform fee.");
        opts.Amount.Should().BeNull("a null amount = full refund.");
    }

    [Fact]
    public void PartialRefundSetsRefundApplicationFeeTrue_ForProportionalReversal()
    {
        var opts = StripeGateway.BuildRefundOptions(Pi, amount: 50m, refundApplicationFee: true);

        opts.RefundApplicationFee.Should().Be(true,
            "RefundApplicationFee=true makes Stripe reverse the fee proportionally on a partial refund.");
        opts.Amount.Should().Be(5000, "50.00 = 5000 cents.");
    }

    [Fact]
    public void NoConnectRefundDoesNotReverseFee()
    {
        var opts = StripeGateway.BuildRefundOptions(Pi, amount: null, refundApplicationFee: false);

        opts.RefundApplicationFee.Should().NotBe(true);
    }

    [Fact]
    public void DoesNotWriteMisleadingFeeReversalMetadata()
    {
        // The old code wrote application_fee_refund_cents to metadata implying an
        // explicit reversal that never happened. It's gone (VRB-104).
        var opts = StripeGateway.BuildRefundOptions(Pi, amount: 50m, refundApplicationFee: true);

        opts.Metadata.Should().BeNull("the reversal is real (RefundApplicationFee), not a metadata note.");
    }
}
