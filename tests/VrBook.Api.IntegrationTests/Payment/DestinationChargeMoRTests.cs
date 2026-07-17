using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stripe;
using VrBook.Modules.Payment.Infrastructure.Stripe;
using Xunit;

namespace VrBook.Api.IntegrationTests.Payment;

/// <summary>
/// VRB-105 (gap G38) — live Stripe-test-mode proof that a Connect destination
/// charge settles net of the platform fee with the platform as merchant-of-record
/// (no <c>on_behalf_of</c>). The unit test (<see cref="StripeGatewayChargeTests"/>)
/// proves the request shape in CI; this asserts the real Stripe API accepts it and
/// reports no <c>on_behalf_of</c> on read-back.
///
/// <para><b>Skip-gated:</b> there is no CI Stripe-Connect test account, so this
/// cannot run in the pipeline. Enable it by removing <c>Skip</c> and setting
/// <c>STRIPE_TEST_SECRET_KEY</c> (a <c>sk_test_…</c> key) + <c>STRIPE_TEST_DESTINATION_ACCOUNT</c>
/// (an <c>acct_…</c> test connected account). The equivalent live settlement check
/// runs in the staging place→capture walk against staging's Stripe test mode.</para>
/// </summary>
[Trait("Category", "Integration")]
public sealed class DestinationChargeMoRTests
{
    [Fact(Skip = "Requires a Stripe-Connect test account + STRIPE_TEST_SECRET_KEY; no CI infra. " +
                 "Live settlement is verified in the staging place→capture walk. Un-skip when creds exist.")]
    public async Task FundsSettleNetOfFeeWithoutOnBehalfOf()
    {
        var secretKey = Environment.GetEnvironmentVariable("STRIPE_TEST_SECRET_KEY");
        var destinationAccount = Environment.GetEnvironmentVariable("STRIPE_TEST_DESTINATION_ACCOUNT");
        secretKey.Should().NotBeNullOrEmpty("the test needs a Stripe test secret key when un-skipped.");
        destinationAccount.Should().NotBeNullOrEmpty("the test needs a Stripe test connected account when un-skipped.");

        var opts = Options.Create(new StripeOptions
        {
            SecretKey = secretKey!,
            PublishableKey = "pk_test_placeholder",
        });
        var gateway = new StripeGateway(opts, NullLogger<StripeGateway>.Instance);

        // Create a Connect destination charge: $100 with a $15 platform fee.
        var created = await gateway.CreatePaymentIntentAsync(
            amount: 100m, currency: "USD",
            idempotencyKey: $"vrb105-mor-{Guid.NewGuid():N}", metadata: null,
            destinationAccountId: destinationAccount!, applicationFeeAmount: 1500);

        // Read the intent back from Stripe and assert the merchant-of-record shape.
        var pi = await new PaymentIntentService().GetAsync(created.Id);
        pi.OnBehalfOfId.Should().BeNull("VRB-105/G38 — the platform is merchant-of-record.");
        pi.TransferData.Should().NotBeNull("funds must still route to the connected account.");
        pi.TransferData!.DestinationId.Should().Be(destinationAccount);
        pi.ApplicationFeeAmount.Should().Be(1500, "the platform fee must still be collected.");
    }
}
