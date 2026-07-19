using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VrBook.Contracts.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Payment.Infrastructure.Tax;
using Xunit;

namespace VrBook.Api.IntegrationTests.Payment;

/// <summary>
/// VRB-103 Phase A — pins the provider-neutral <see cref="StripeTaxCalculator"/>:
/// the Stripe calculation → <c>TaxCalculationResult</c> mapping, and the fail-closed /
/// fail-open posture. The Stripe SDK is faked via <see cref="IStripeTaxClient"/>, so these
/// run as fast unit tests (no network); the real client has a Skip-gated live test.
/// </summary>
[Trait("Category", "Unit")]
public sealed class StripeTaxCalculatorTests
{
    private static readonly Address UsAddress = new(
        Street: "1 Ocean Dr", City: "Miami Beach", State: "FL",
        PostalCode: "33139", CountryCode: "US", Latitude: 25.79m, Longitude: -80.13m);

    private static readonly Money PreTax = new(200.00m, "USD");

    [Fact]
    public void MapCalculation_maps_stripe_calculation_to_itemized_tax_lines()
    {
        var calc = new StripeTaxCalculation(
            TotalTaxCents: 1300,
            Lines: new[]
            {
                new StripeTaxLine("Florida state tax", 1200, "US-FL"),
                new StripeTaxLine("Miami-Dade county tax", 100, "US-FL-DADE"),
            });

        var result = StripeTaxCalculator.MapCalculation(calc, "USD");

        result.TotalTax.Should().Be(new Money(13.00m, "USD"));
        result.Lines.Should().HaveCount(2);
        result.Lines[0].Should().Be(new TaxLine("Florida state tax", new Money(12.00m, "USD"), "US-FL"));
        result.Lines[1].Should().Be(new TaxLine("Miami-Dade county tax", new Money(1.00m, "USD"), "US-FL-DADE"));
    }

    [Fact]
    public async Task CalculateAsync_maps_stripe_result_and_passes_ApplyToFees_through()
    {
        var fake = new FakeStripeTaxClient(
            new StripeTaxCalculation(800, new[] { new StripeTaxLine("Sales tax", 800, "US-CA") }));
        var calc = NewCalculator(fake, applyToFees: true);

        var result = await calc.CalculateAsync(UsAddress, PreTax, CancellationToken.None);

        result.TotalTax.Should().Be(new Money(8.00m, "USD"));
        result.Lines.Should().ContainSingle().Which.JurisdictionCode.Should().Be("US-CA");
        fake.LastApplyToFees.Should().BeTrue("Tax:ApplyToFees flows to the Stripe calculation request");
    }

    [Fact]
    public async Task CalculateAsync_throws_TaxUnavailable_when_stripe_errors_and_fail_closed()
    {
        var fake = new FakeStripeTaxClient(throwOnCall: new InvalidOperationException("stripe down"));
        var calc = NewCalculator(fake, failClosed: true);

        Func<Task> act = () => calc.CalculateAsync(UsAddress, PreTax, CancellationToken.None);

        (await act.Should().ThrowAsync<TaxUnavailableException>())
            .Which.Rule.Should().Be("tax.unavailable");
    }

    [Fact]
    public async Task CalculateAsync_returns_zero_when_stripe_errors_and_fail_open()
    {
        var fake = new FakeStripeTaxClient(throwOnCall: new InvalidOperationException("stripe down"));
        var calc = NewCalculator(fake, failClosed: false);

        var result = await calc.CalculateAsync(UsAddress, PreTax, CancellationToken.None);

        result.TotalTax.Should().Be(Money.Zero("USD"));
        result.Lines.Should().BeEmpty();
    }

    private static StripeTaxCalculator NewCalculator(
        IStripeTaxClient client, bool applyToFees = true, bool failClosed = true)
    {
        var opts = Options.Create(new TaxOptions { ApplyToFees = applyToFees, FailClosed = failClosed });
        return new StripeTaxCalculator(client, opts, NullLogger<StripeTaxCalculator>.Instance);
    }

    private sealed class FakeStripeTaxClient : IStripeTaxClient
    {
        private readonly StripeTaxCalculation? result;
        private readonly Exception? throwOnCall;

        public FakeStripeTaxClient(StripeTaxCalculation result) => this.result = result;
        public FakeStripeTaxClient(Exception throwOnCall) => this.throwOnCall = throwOnCall;

        public bool? LastApplyToFees { get; private set; }

        public Task<StripeTaxCalculation> CreateCalculationAsync(
            Address address, Money preTaxAmount, bool applyToFees, CancellationToken ct = default)
        {
            LastApplyToFees = applyToFees;
            if (throwOnCall is not null)
            {
                throw throwOnCall;
            }

            return Task.FromResult(result!);
        }
    }
}
