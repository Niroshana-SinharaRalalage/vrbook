using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Events;

public sealed record BookingDraftCreated(
    Guid BookingId,
    string Reference,
    Guid PropertyId,
    Guid GuestUserId,
    DateOnly Checkin,
    DateOnly Checkout) : DomainEvent;

public sealed record BookingPlaced(
    Guid BookingId,
    string Reference,
    Guid PropertyId,
    Guid GuestUserId,
    DateOnly Checkin,
    DateOnly Checkout,
    DateTimeOffset TentativeUntil) : DomainEvent;

public sealed record BookingConfirmed(
    Guid BookingId,
    string Reference,
    Guid PropertyId,
    Guid GuestUserId,
    DateOnly Checkin,
    DateOnly Checkout,
    string Trigger) : DomainEvent; // "owner" | "sla"

public sealed record BookingRejected(
    Guid BookingId,
    string Reference,
    Guid PropertyId,
    Guid GuestUserId,
    string Reason) : DomainEvent;

public sealed record BookingCancelled(
    Guid BookingId,
    string Reference,
    Guid PropertyId,
    Guid GuestUserId,
    string CancelledBy,        // "guest" | "owner" | "system"
    decimal RefundAmount,
    string Currency) : DomainEvent;

public sealed record BookingCheckedIn(Guid BookingId, string Reference) : DomainEvent;

public sealed record BookingCheckedOut(Guid BookingId, string Reference) : DomainEvent;

public sealed record BookingCompleted(
    Guid BookingId,
    string Reference,
    Guid GuestUserId) : DomainEvent;

public sealed record BookingDisputed(
    Guid BookingId,
    string Reference,
    string DisputeReason) : DomainEvent;

public sealed record BookingConflictDetected(
    Guid BookingId,
    Guid ExternalReservationId,
    ChannelKind Channel) : DomainEvent;
