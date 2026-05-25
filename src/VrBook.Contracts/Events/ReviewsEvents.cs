namespace VrBook.Contracts.Events;

public sealed record ReviewSubmitted(
    Guid ReviewId,
    Guid BookingId,
    Guid PropertyId,
    Guid GuestUserId,
    int Rating) : DomainEvent;

public sealed record ReviewApproved(
    Guid ReviewId,
    Guid PropertyId,
    int Rating) : DomainEvent;

public sealed record ReviewRejected(
    Guid ReviewId,
    Guid PropertyId,
    string? Reason) : DomainEvent;

public sealed record ReviewResponded(
    Guid ReviewId,
    Guid ResponseId,
    Guid OwnerUserId) : DomainEvent;
