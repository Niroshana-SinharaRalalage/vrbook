using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VrBook.Contracts.Common;
using VrBook.Modules.Payment.Infrastructure.Stripe;
using VrBook.Modules.Payment.Infrastructure.Tax;
using Xunit;

namespace VrBook.Api.IntegrationTests.Payment;

/// <summary>
/// VRB-103 — live Stripe-test-mode proof that <see cref="StripeTaxClient"/> returns real,
/// non-zero, per-jurisdiction tax for a US address (the AC that replaces the zero-tax stub).
///
/// <para><b>Skip-gated:</b> needs a Stripe test key with <b>Stripe Tax enabled</b> on the
/// platform account (an operator toggle, Q2) — no CI infra. Enable by removing <c>Skip</c>
/// and setting <c>STRIPE_TEST_SECRET_KEY</c>. The equivalent live check runs in the staging
/// guest-quote walk once the account toggle is flipped.</para>
/// </summary>
[Trait("Category", "Integration")]
public sealed class StripeTaxCalculationLiveTests
{
    [Fact(Skip = "Requires STRIPE_TEST_SECRET_KEY + Stripe Tax enabled on the platform account (operator); " +
                 "no CI infra. Live proof runs in the staging guest-quote walk. Un-skip when creds exist.")]
    public async Task CreateCalculation_returns_non_zero_tax_for_us_address()
    {
        var secretKey = Environment.GetEnvironmentVariable("STRIPE_TEST_SECRET_KEY");
        secretKey.Should().NotBeNullOrEmpty();

        var opts = Options.Create(new StripeOptions { SecretKey = secretKey!, PublishableKey = "pk_test_placeholder" });
        var client = new StripeTaxClient(opts, NullLogger<StripeTaxClient>.Instance);
        var address = new Address(
            Street: "1355 Market St", City: "San Francisco", State: "CA",
            PostalCode: "94103", CountryCode: "US", Latitude: 37.777m, Longitude: -122.416m);

        var calc = await client.CreateCalculationAsync(
            address, new Money(200.00m, "USD"), applyToFees: true, CancellationToken.None);

        calc.TotalTaxCents.Should().BeGreaterThan(0, "a CA address must yield real sales tax, not the zero stub.");
        calc.Lines.Should().NotBeEmpty("tax must be itemized per jurisdiction for the receipt breakdown.");
        calc.Lines.Should().OnlyContain(l => l.AmountCents > 0);
    }
}
