using VrBook.Contracts.Common;

namespace VrBook.Modules.Payment.Infrastructure.Tax;

/// <summary>
/// VRB-103 — seam over the Stripe Tax <c>Calculations</c> API. Keeps the raw Stripe SDK
/// types out of <see cref="StripeTaxCalculator"/> so the calculator + its mapping are fully
/// unit-testable; the real client (<c>StripeTaxClient</c>) is the ONLY Stripe-Tax-touching
/// type and is exercised by a Skip-gated live integration test (like the charge/refund seams).
/// </summary>
internal interface IStripeTaxClient
{
    /// <summary>
    /// Create a Stripe Tax calculation for <paramref name="preTaxAmount"/> at
    /// <paramref name="address"/>. <paramref name="applyToFees"/> reflects
    /// <c>Tax:ApplyToFees</c>. Throws on any Stripe/transport error — the caller
    /// decides fail-closed vs fail-open.
    /// </summary>
    Task<StripeTaxCalculation> CreateCalculationAsync(
        Address address, Money preTaxAmount, bool applyToFees, CancellationToken ct = default);
}

/// <summary>Provider-neutral projection of a Stripe Tax Calculation, in minor units (cents).</summary>
internal sealed record StripeTaxCalculation(long TotalTaxCents, IReadOnlyList<StripeTaxLine> Lines);

/// <summary>One jurisdiction's tax line, in minor units (cents).</summary>
internal sealed record StripeTaxLine(string Label, long AmountCents, string? JurisdictionCode);
