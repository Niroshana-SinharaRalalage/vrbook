using VrBook.Contracts.Common;

namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Pricing → Tax boundary. Pricing depends on this interface; the Stripe Tax adapter
/// (proposal §9.2) implements it. Until Payment module ships, infrastructure provides
/// a zero-tax stub.
/// </summary>
public interface ITaxCalculator
{
    Task<TaxCalculationResult> CalculateAsync(
        Address address,
        Money preTaxAmount,
        CancellationToken ct = default);
}

public sealed record TaxCalculationResult(
    Money TotalTax,
    IReadOnlyList<TaxLine> Lines);

public sealed record TaxLine(
    string Label,
    Money Amount,
    string? JurisdictionCode);
