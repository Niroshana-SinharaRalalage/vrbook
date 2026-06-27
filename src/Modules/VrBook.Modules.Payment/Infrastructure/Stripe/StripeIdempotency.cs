namespace VrBook.Modules.Payment.Infrastructure.Stripe;

/// <summary>
/// OPS.M.5 §10 best-practices contract — every Stripe write passes an explicit
/// <see cref="global::Stripe.RequestOptions.IdempotencyKey"/>. Per-call format
/// is fixed here so callers cannot drift and arch test
/// <c>NoDirectStripeSdkUsageOutsideGatewayTests</c> can pin the format.
///
/// <para>Step 3 RED: methods throw <see cref="NotImplementedException"/> so the
/// idempotency-format tests in <c>StripeIdempotencyTests</c> fail at the
/// assertion checks. Step 3 GREEN replaces the bodies with the per-call
/// formats from OPS.M.5 §10.</para>
/// </summary>
public static class StripeIdempotency
{
    public static string ForOnboarding(Guid tenantId) =>
        throw new NotImplementedException("Wired by OPS.M.5 §10 Step 3 GREEN.");

    public static string ForPaymentIntent(Guid bookingId) =>
        throw new NotImplementedException("Wired by OPS.M.5 §10 Step 3 GREEN.");

    public static string ForRefund(Guid refundId) =>
        throw new NotImplementedException("Wired by OPS.M.5 §10 Step 3 GREEN.");
}
