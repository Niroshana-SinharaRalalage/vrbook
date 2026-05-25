namespace VrBook.Contracts.Events;

public sealed record PaymentAuthorized(
    Guid PaymentIntentId,
    Guid BookingId,
    string StripePaymentIntentId,
    decimal Amount,
    string Currency) : DomainEvent;

public sealed record PaymentCaptured(
    Guid PaymentIntentId,
    Guid BookingId,
    string StripePaymentIntentId,
    decimal Amount,
    string Currency) : DomainEvent;

public sealed record PaymentFailed(
    Guid PaymentIntentId,
    Guid BookingId,
    string Reason) : DomainEvent;

public sealed record RefundIssued(
    Guid RefundId,
    Guid PaymentIntentId,
    Guid BookingId,
    decimal Amount,
    string Currency,
    string Reason) : DomainEvent;

public sealed record DisputeOpened(
    Guid PaymentIntentId,
    Guid BookingId,
    string StripeDisputeId,
    string Reason,
    DateTimeOffset EvidenceDueBy) : DomainEvent;
