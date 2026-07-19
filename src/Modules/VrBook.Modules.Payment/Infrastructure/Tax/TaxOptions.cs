namespace VrBook.Modules.Payment.Infrastructure.Tax;

/// <summary>
/// VRB-103 — bound from the <c>Tax</c> config section. Governs the Stripe-Tax
/// calculator; the <c>Features:StripeTaxEnabled</c> flag gates whether it is used
/// at all (off ⇒ the zero-tax stub, current behavior).
/// </summary>
public sealed class TaxOptions
{
    public const string SectionName = "Tax";

    /// <summary>Provider selector (only <c>StripeTax</c> today).</summary>
    public string Provider { get; set; } = "StripeTax";

    /// <summary>Q25 — whether platform fees are part of the taxable base. Default true.</summary>
    public bool ApplyToFees { get; set; } = true;

    /// <summary>
    /// When true (default), a Stripe-Tax error throws so the quote fails closed
    /// (never silently shows $0 tax). Dev may set false for offline work.
    /// </summary>
    public bool FailClosed { get; set; } = true;
}
