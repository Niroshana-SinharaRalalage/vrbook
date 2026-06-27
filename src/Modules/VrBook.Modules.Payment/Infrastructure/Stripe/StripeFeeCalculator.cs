namespace VrBook.Modules.Payment.Infrastructure.Stripe;

/// <summary>
/// OPS.M.5 §3.6 + §10 best-practice #4 — application-fee math. Pure functions
/// so the formula is unit-testable without Stripe HTTP. <c>decimal</c> with
/// banker's rounding (<see cref="MidpointRounding.ToEven"/>) per §10.
///
/// <para>Step 3 RED: methods throw so <c>StripeFeeCalculatorTests</c> fails
/// at assertion. GREEN replaces with the formulas from §3.6.</para>
/// </summary>
public static class StripeFeeCalculator
{
    /// <summary>
    /// <c>applicationFeeAmount = round(capturedAmount × PlatformFeeBps / 10_000, 2)</c>
    /// converted to cents at the Stripe boundary.
    /// </summary>
    public static long ApplicationFeeCents(decimal capturedAmount, int platformFeeBps) =>
        throw new NotImplementedException("Wired by OPS.M.5 §3.6 Step 3 GREEN.");

    /// <summary>
    /// <c>feeReversal = round(refundAmount × PlatformFeeBps / 10_000, 2)</c> for
    /// partial refunds. Returns <c>null</c> for the full-refund case (the
    /// caller passes <c>refund_application_fee=true</c> alone instead).
    /// </summary>
    public static long? ProportionalFeeReversalCents(
        decimal refundAmount, decimal capturedAmount, int platformFeeBps) =>
        throw new NotImplementedException("Wired by OPS.M.5 §3.6 Step 3 GREEN.");
}
