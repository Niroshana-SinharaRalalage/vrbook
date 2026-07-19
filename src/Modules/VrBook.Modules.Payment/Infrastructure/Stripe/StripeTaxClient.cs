using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Tax;
using VrBook.Contracts.Common;
using VrBook.Domain.Common;
using VrBook.Modules.Payment.Infrastructure.Stripe;
using StripeAddress = VrBook.Contracts.Common.Address;

namespace VrBook.Modules.Payment.Infrastructure.Tax;

/// <summary>
/// VRB-103 — the ONLY Stripe-Tax-touching type. Calls the Stripe Tax <c>Calculations</c> API
/// (destination = the property jurisdiction) and projects the result into the provider-neutral
/// <see cref="StripeTaxCalculation"/> that <see cref="StripeTaxCalculator"/> consumes. The
/// calculation is created on the <b>platform</b> account (no <c>OnBehalfOf</c>) so tax is
/// collected by VrBook as the marketplace facilitator (VRB-105 dropped the supplier MoR).
/// Exercised by a Skip-gated live integration test (needs a Stripe test key + Stripe Tax enabled).
/// </summary>
internal sealed class StripeTaxClient : IStripeTaxClient
{
    private readonly StripeOptions options;
    private readonly ILogger<StripeTaxClient> logger;

    public StripeTaxClient(IOptions<StripeOptions> options, ILogger<StripeTaxClient> logger)
    {
        this.options = options.Value;
        this.logger = logger;
        if (this.options.IsConfigured)
        {
            StripeConfiguration.ApiKey = this.options.SecretKey;
        }
    }

    public async Task<StripeTaxCalculation> CreateCalculationAsync(
        StripeAddress address, Money preTaxAmount, bool applyToFees, CancellationToken ct = default)
    {
        if (!options.IsConfigured)
        {
            throw new BusinessRuleViolationException(
                "payment.not_configured", "Payment provider is not configured for this environment.");
        }

        var amountCents = (long)Math.Round(preTaxAmount.Amount * 100m, MidpointRounding.AwayFromZero);
        var createOptions = new CalculationCreateOptions
        {
            Currency = preTaxAmount.Currency.ToLowerInvariant(),
            LineItems = new List<CalculationLineItemOptions>
            {
                new()
                {
                    Amount = amountCents,
                    Reference = "booking-subtotal",
                    TaxBehavior = "exclusive",
                },
            },
            CustomerDetails = new CalculationCustomerDetailsOptions
            {
                Address = new AddressOptions
                {
                    Line1 = address.Street,
                    City = address.City,
                    State = address.State,
                    PostalCode = address.PostalCode,
                    Country = address.CountryCode,
                },
                AddressSource = "shipping",
            },
        };

        var service = new CalculationService();
        var calc = await StripeRetryPipeline.Build().ExecuteAsync(
            async token => await service.CreateAsync(createOptions, requestOptions: null, token),
            ct);

        var lines = (calc.TaxBreakdown ?? new List<CalculationTaxBreakdown>())
            .Where(b => b.Amount > 0)
            .Select(ToLine)
            .ToList();

        logger.LogInformation(
            "tax.calc.stripe calculation_id={CalculationId} state={State} tax_cents={TaxCents} lines={LineCount}",
            calc.Id, address.State, calc.TaxAmountExclusive, lines.Count);

        return new StripeTaxCalculation(calc.TaxAmountExclusive, lines);
    }

    private static StripeTaxLine ToLine(CalculationTaxBreakdown b)
    {
        var d = b.TaxRateDetails;
        var state = d?.State;
        var country = d?.Country;
        var taxType = Humanize(d?.TaxType);
        var label = string.IsNullOrWhiteSpace(state) ? taxType : $"{state} {taxType}";
        var jurisdiction = string.IsNullOrWhiteSpace(state)
            ? country
            : string.Join('-', new[] { country, state }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new StripeTaxLine(label, b.Amount, jurisdiction);
    }

    private static string Humanize(string? taxType) =>
        string.IsNullOrWhiteSpace(taxType)
            ? "Tax"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(taxType.Replace('_', ' '));
}
