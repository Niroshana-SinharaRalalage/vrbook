using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VrBook.Contracts.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;

namespace VrBook.Modules.Payment.Infrastructure.Tax;

/// <summary>
/// VRB-103 — the real <see cref="ITaxCalculator"/>, replacing the zero-tax stub when
/// <c>Features:StripeTaxEnabled</c> is on (see <c>PaymentModule</c>). Computes US
/// lodging/sales tax via Stripe Tax as the marketplace facilitator (tax lands on the
/// platform account — coherent since VRB-105 dropped <c>OnBehalfOf=supplier</c>).
///
/// <para>Fail-closed (<c>Tax:FailClosed</c>, default true): a Stripe-Tax error throws
/// <see cref="TaxUnavailableException"/> so the quote never silently shows $0 tax at launch.</para>
///
/// <para>The Stripe SDK is isolated behind <see cref="IStripeTaxClient"/>; this type + its
/// <see cref="MapCalculation"/> seam are provider-neutral and fully unit-tested.</para>
/// </summary>
internal sealed class StripeTaxCalculator : ITaxCalculator
{
    private readonly IStripeTaxClient client;
    private readonly TaxOptions options;
    private readonly ILogger<StripeTaxCalculator> logger;

    public StripeTaxCalculator(
        IStripeTaxClient client, IOptions<TaxOptions> options, ILogger<StripeTaxCalculator> logger)
    {
        this.client = client;
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<TaxCalculationResult> CalculateAsync(
        Address address, Money preTaxAmount, CancellationToken ct = default)
    {
        StripeTaxCalculation calc;
        try
        {
            calc = await client.CreateCalculationAsync(address, preTaxAmount, options.ApplyToFees, ct);
        }
        catch (Exception ex) when (ex is not DomainException)
        {
            // Observability (story §Observability): every failed calc is logged with the
            // jurisdiction + amount so the >1% error-rate alert has a signal to page on.
            logger.LogError(
                ex,
                "tax.calc.error country={Country} state={State} amount={Amount} {Currency} failClosed={FailClosed}",
                address.CountryCode, address.State, preTaxAmount.Amount, preTaxAmount.Currency, options.FailClosed);

            if (options.FailClosed)
            {
                throw new TaxUnavailableException();
            }

            // Fail-open is a dev-only escape hatch (Tax:FailClosed=false); prod stays closed.
            return new TaxCalculationResult(Money.Zero(preTaxAmount.Currency), Array.Empty<TaxLine>());
        }

        logger.LogInformation(
            "tax.calc country={Country} state={State} tax_cents={TaxCents} lines={LineCount}",
            address.CountryCode, address.State, calc.TotalTaxCents, calc.Lines.Count);

        return MapCalculation(calc, preTaxAmount.Currency);
    }

    /// <summary>
    /// SDK-free mapping seam: a Stripe calculation (minor units) → the domain
    /// <see cref="TaxCalculationResult"/>. Kept internal-static + pure so unit tests pin the
    /// itemized, per-jurisdiction shape without any Stripe dependency.
    /// </summary>
    internal static TaxCalculationResult MapCalculation(StripeTaxCalculation calc, string currency)
    {
        var lines = calc.Lines
            .Select(l => new TaxLine(l.Label, new Money(l.AmountCents / 100m, currency), l.JurisdictionCode))
            .ToList();
        return new TaxCalculationResult(new Money(calc.TotalTaxCents / 100m, currency), lines);
    }
}
