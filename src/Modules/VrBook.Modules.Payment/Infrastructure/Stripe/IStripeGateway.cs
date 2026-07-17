using VrBook.Contracts.Enums;

namespace VrBook.Modules.Payment.Infrastructure.Stripe;

public interface IStripeGateway
{
    bool IsConfigured { get; }
    string PublishableKey { get; }

    Task<StripeIntentCreated> CreatePaymentIntentAsync(decimal amount, string currency, string idempotencyKey, IDictionary<string, string>? metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// OPS.M.5 §3.6 + §3.7 — Connect-aware overload that routes the charge to a
    /// destination connected account and takes a platform application fee. The
    /// platform stays merchant-of-record: <c>OnBehalfOf</c> is deliberately NOT
    /// set (VRB-105 / gap G38), so tax/settlement liability sits on the platform
    /// per the marketplace-facilitator posture (VRB-103).
    /// </summary>
    Task<StripeIntentCreated> CreatePaymentIntentAsync(
        decimal amount,
        string currency,
        string idempotencyKey,
        IDictionary<string, string>? metadata,
        string destinationAccountId,
        long applicationFeeAmount,
        CancellationToken cancellationToken = default);

    Task<StripeIntentUpdate> CapturePaymentIntentAsync(string stripePaymentIntentId, CancellationToken cancellationToken = default);
    Task<StripeIntentUpdate> CancelPaymentIntentAsync(string stripePaymentIntentId, CancellationToken cancellationToken = default);
    Task<StripeRefundCreated> RefundAsync(string stripePaymentIntentId, decimal? amount, string idempotencyKey, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// OPS.M.5 §3.6 — Refund with Connect-aware fee reversal.
    /// <paramref name="refundApplicationFee"/> = <c>true</c> for full refunds;
    /// <paramref name="applicationFeeRefundCents"/> non-null for partial refunds
    /// (proportional reversal). Both <c>null</c>/false for the legacy non-Connect path.
    /// </summary>
    Task<StripeRefundCreated> RefundAsync(
        string stripePaymentIntentId,
        decimal? amount,
        string idempotencyKey,
        string? reason,
        bool refundApplicationFee,
        long? applicationFeeRefundCents,
        CancellationToken cancellationToken = default);

    bool VerifyWebhookSignature(string payload, string signatureHeader, out string? eventType, out string? rawEventJson);
}

public sealed record StripeIntentCreated(string Id, string ClientSecret, PaymentStatus Status);
public sealed record StripeIntentUpdate(string Id, PaymentStatus Status, string? ChargeId);
public sealed record StripeRefundCreated(string Id, decimal Amount, RefundStatus Status, long? FeeReversalCents = null);
