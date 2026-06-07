using VrBook.Contracts.Enums;

namespace VrBook.Modules.Payment.Infrastructure.Stripe;

public interface IStripeGateway
{
    bool IsConfigured { get; }
    string PublishableKey { get; }

    Task<StripeIntentCreated> CreatePaymentIntentAsync(decimal amount, string currency, string idempotencyKey, IDictionary<string, string>? metadata, CancellationToken cancellationToken = default);
    Task<StripeIntentUpdate> CapturePaymentIntentAsync(string stripePaymentIntentId, CancellationToken cancellationToken = default);
    Task<StripeIntentUpdate> CancelPaymentIntentAsync(string stripePaymentIntentId, CancellationToken cancellationToken = default);
    Task<StripeRefundCreated> RefundAsync(string stripePaymentIntentId, decimal? amount, string idempotencyKey, string? reason, CancellationToken cancellationToken = default);

    bool VerifyWebhookSignature(string payload, string signatureHeader, out string? eventType, out string? rawEventJson);
}

public sealed record StripeIntentCreated(string Id, string ClientSecret, PaymentStatus Status);
public sealed record StripeIntentUpdate(string Id, PaymentStatus Status, string? ChargeId);
public sealed record StripeRefundCreated(string Id, decimal Amount, RefundStatus Status);
