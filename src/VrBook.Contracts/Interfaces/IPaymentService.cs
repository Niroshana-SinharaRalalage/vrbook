using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;

namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Booking → Payment boundary. The Booking module orchestrates and only depends on this
/// interface; the Stripe-specific implementation lives in the Payment module.
/// </summary>
public interface IPaymentService
{
    Task<PaymentAuthorizationResult> AuthorizeAsync(
        Guid bookingId,
        Money amount,
        Guid guestUserId,
        string idempotencyKey,
        CancellationToken ct = default);

    Task CaptureAsync(Guid paymentIntentId, CancellationToken ct = default);

    Task<RefundDto> RefundAsync(
        Guid paymentIntentId,
        Money? amount,
        string reason,
        string idempotencyKey,
        CancellationToken ct = default);

    Task CancelAsync(Guid paymentIntentId, string reason, CancellationToken ct = default);
}

public sealed record PaymentAuthorizationResult(
    Guid PaymentIntentId,
    string StripePaymentIntentId,
    string ClientSecret,
    string PublishableKey);
