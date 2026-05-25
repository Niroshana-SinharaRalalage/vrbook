using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Events;

public sealed record ExternalReservationImported(
    Guid ExternalReservationId,
    Guid PropertyId,
    ChannelKind Channel,
    string ICalUid,
    DateOnly Checkin,
    DateOnly Checkout) : DomainEvent;

public sealed record ExternalReservationCancelled(
    Guid ExternalReservationId,
    Guid PropertyId,
    ChannelKind Channel,
    string ICalUid) : DomainEvent;

public sealed record SyncConflictDetected(
    Guid ConflictId,
    Guid PropertyId,
    Guid BookingId,
    Guid ExternalReservationId,
    ChannelKind Channel) : DomainEvent;

public sealed record SyncRunFailed(
    Guid ChannelFeedId,
    Guid PropertyId,
    ChannelKind Channel,
    int ConsecutiveFailures,
    string Error) : DomainEvent;
