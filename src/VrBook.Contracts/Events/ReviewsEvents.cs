namespace VrBook.Contracts.Events;

// OPS.M.4 Step 1 — ReviewSubmitted feeds Notifications + Reports; ReviewApproved
// feeds Catalog rating recompute (same-tenant) but the carrying field future-proofs
// cross-tenant reporting. ReviewRejected + ReviewResponded stay same-module.

public sealed record ReviewSubmitted(
    Guid ReviewId,
    Guid BookingId,
    Guid PropertyId,
    Guid GuestUserId,
    int Rating,
    Guid TenantId) : DomainEvent;

public sealed record ReviewApproved(
    Guid ReviewId,
    Guid PropertyId,
    int Rating,
    Guid TenantId) : DomainEvent;

public sealed record ReviewRejected(
    Guid ReviewId,
    Guid PropertyId,
    string? Reason) : DomainEvent;

public sealed record ReviewResponded(
    Guid ReviewId,
    Guid ResponseId,
    Guid OwnerUserId) : DomainEvent;
