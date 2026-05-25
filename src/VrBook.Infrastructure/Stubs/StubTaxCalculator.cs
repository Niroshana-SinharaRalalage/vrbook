using VrBook.Contracts.Common;
using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Stubs;

/// <summary>
/// A0 stub. Returns zero tax. Replaced by the real Stripe Tax adapter in A5 (Payments).
/// Until then this is what Pricing sees. See proposal §9.2.
/// </summary>
public sealed class StubTaxCalculator : ITaxCalculator
{
    public Task<TaxCalculationResult> CalculateAsync(
        Address address, Money preTaxAmount, CancellationToken ct = default)
    {
        var zero = Money.Zero(preTaxAmount.Currency);
        return Task.FromResult(new TaxCalculationResult(zero, Array.Empty<TaxLine>()));
    }
}
