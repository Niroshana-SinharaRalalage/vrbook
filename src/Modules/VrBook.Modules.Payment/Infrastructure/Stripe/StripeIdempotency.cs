namespace VrBook.Modules.Payment.Infrastructure.Stripe;

/// <summary>
/// OPS.M.5 §10 best-practices contract — every Stripe write passes an explicit
/// <see cref="global::Stripe.RequestOptions.IdempotencyKey"/>. Per-call format
/// is fixed here so callers cannot drift and arch test
/// <c>NoDirectStripeSdkUsageOutsideGatewayTests</c> can pin the format.
/// </summary>
public static class StripeIdempotency
{
    /// <summary>Connect Account.create — one Connect account per tenant ever.</summary>
    public static string ForOnboarding(Guid tenantId) =>
        $"tenant-onboarding:{tenantId:D}";

    /// <summary>PaymentIntent.create — one PI per booking (pre-existing format from `StripeGateway.cs`).</summary>
    public static string ForPaymentIntent(Guid bookingId) =>
        $"booking:{bookingId:N}:pi";

    /// <summary>Refund.create — keyed on the domain Refund.Id.</summary>
    public static string ForRefund(Guid refundId) =>
        $"refund:{refundId:N}";
}
