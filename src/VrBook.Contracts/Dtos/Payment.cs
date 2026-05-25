using VrBook.Contracts.Common;
using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Dtos;

public sealed record PaymentIntentDto(
    Guid Id,
    Guid BookingId,
    string StripePaymentIntentId,
    Money Amount,
    PaymentStatus Status,
    string CaptureMethod,
    DateTimeOffset CreatedAt);

public sealed record CreatePaymentIntentRequest(
    Guid BookingId,
    string? PaymentMethodId);

public sealed record CreatePaymentIntentResponse(
    PaymentIntentDto PaymentIntent,
    string ClientSecret,
    string PublishableKey);

public sealed record RefundDto(
    Guid Id,
    Guid PaymentIntentId,
    string StripeRefundId,
    Money Amount,
    RefundStatus Status,
    string? Reason,
    DateTimeOffset CreatedAt);

public sealed record IssueRefundRequest(
    Guid BookingId,
    Money? Amount, // null = full refund (or policy-driven)
    string Reason);
