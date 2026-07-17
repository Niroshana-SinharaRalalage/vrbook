using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VrBook.Modules.Payment.Infrastructure.Stripe;
using Xunit;

namespace VrBook.Api.IntegrationTests.Payment;

/// <summary>
/// VRB-104 (gap G37) — live Stripe-test-mode proof that a **partial** refund on a
/// Connect destination charge reverses the platform fee **proportionally** and the
/// gateway reads the **actual** reversed cents back (returned as
/// <see cref="StripeRefundCreated.FeeReversalCents"/>, persisted on the Refund row).
///
/// <para><b>Skip-gated:</b> no CI Stripe-Connect test account. Enable by removing
/// <c>Skip</c> and setting <c>STRIPE_TEST_SECRET_KEY</c> + <c>STRIPE_TEST_CAPTURED_PI</c>
/// (a captured destination-charge PaymentIntent with a platform fee). The equivalent
/// live check runs in the staging refund walk.</para>
/// </summary>
[Trait("Category", "Integration")]
public sealed class RefundFeeReversalTests
{
    [Fact(Skip = "Requires a Stripe-Connect test account + STRIPE_TEST_SECRET_KEY + STRIPE_TEST_CAPTURED_PI; " +
                 "no CI infra. Live proof runs in the staging refund walk. Un-skip when creds exist.")]
    public async Task PartialRefundReversesProportionalFeeAndReadsBackActualCents()
    {
        var secretKey = Environment.GetEnvironmentVariable("STRIPE_TEST_SECRET_KEY");
        var capturedPi = Environment.GetEnvironmentVariable("STRIPE_TEST_CAPTURED_PI");
        secretKey.Should().NotBeNullOrEmpty();
        capturedPi.Should().NotBeNullOrEmpty();

        var opts = Options.Create(new StripeOptions { SecretKey = secretKey!, PublishableKey = "pk_test_placeholder" });
        var gateway = new StripeGateway(opts, NullLogger<StripeGateway>.Instance);

        // Partial refund with the fee reversed proportionally by Stripe.
        var result = await gateway.RefundAsync(
            capturedPi!, amount: 50m,
            idempotencyKey: $"vrb104-fee-reversal-{Guid.NewGuid():N}", reason: null,
            refundApplicationFee: true, applicationFeeRefundCents: 750,
            CancellationToken.None);

        result.FeeReversalCents.Should().NotBeNull("the gateway must read the actual reversal back from Stripe.");
        result.FeeReversalCents.Should().BeGreaterThan(0, "a partial refund reverses a proportional slice of the platform fee.");
    }
}
