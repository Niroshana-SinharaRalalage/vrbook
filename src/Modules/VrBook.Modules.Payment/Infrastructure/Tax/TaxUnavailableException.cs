using VrBook.Domain.Common;

namespace VrBook.Modules.Payment.Infrastructure.Tax;

/// <summary>
/// VRB-103 — thrown when Stripe Tax is unreachable and <c>Tax:FailClosed</c> is true
/// (the launch default). The quote fails closed: guests never silently see $0 tax.
/// Reuses the <see cref="BusinessRuleViolationException"/> rule surface so the existing
/// RFC 7807 pipeline maps it (rule <c>tax.unavailable</c>). The underlying Stripe error
/// is logged by <see cref="StripeTaxCalculator"/> before this is raised.
/// </summary>
public sealed class TaxUnavailableException : BusinessRuleViolationException
{
    /// <summary>The <see cref="BusinessRuleViolationException.Rule"/> value this always carries.</summary>
    public const string RuleName = "tax.unavailable";

    public TaxUnavailableException()
        : base(RuleName, "Tax is temporarily unavailable. Please try again shortly.")
    {
    }
}
