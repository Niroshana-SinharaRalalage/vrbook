namespace VrBook.Contracts.Events;

// OPS.M.5 §3.9 (D9) Step 7 — four Payment events gain Guid TenantId as the
// leading positional parameter. Downstream consumers (Notifications, Sync,
// future Reports + OPS.M.9 RLS) can route by tenant without a cross-schema
// lookup. DisputeOpened is NOT bumped — Phase 2 auto-respond is the consumer
// and ships later. Same atomic-deploy constraint as OPS.M.4 §4 events.

public sealed record PaymentAuthorized(
    Guid TenantId,
    Guid PaymentIntentId,
    Guid BookingId,
    string StripePaymentIntentId,
    decimal Amount,
    string Currency) : DomainEvent;

public sealed record PaymentCaptured(
    Guid TenantId,
    Guid PaymentIntentId,
    Guid BookingId,
    string StripePaymentIntentId,
    decimal Amount,
    string Currency) : DomainEvent;

public sealed record PaymentFailed(
    Guid TenantId,
    Guid PaymentIntentId,
    Guid BookingId,
    string Reason) : DomainEvent;

public sealed record RefundIssued(
    Guid TenantId,
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
