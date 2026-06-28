using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Events;

// OPS.M.4 Step 1 — SyncConflictDetected feeds Booking transition logic + future
// Notifications. OPS.M.6 Step 5 — the other three records gain leading
// `Guid TenantId` so downstream cross-module consumers can deserialize a
// routing key without joining back to ChannelFeed/Property.
//
// SyncConflictDetected keeps TenantId at position 5 (legacy); rebumping it
// would silently break the OPS.M.4 Booking-side consumer.

public sealed record ExternalReservationImported(
    Guid TenantId,
    Guid ExternalReservationId,
    Guid PropertyId,
    ChannelKind Channel,
    string ICalUid,
    DateOnly Checkin,
    DateOnly Checkout) : DomainEvent;

public sealed record ExternalReservationCancelled(
    Guid TenantId,
    Guid ExternalReservationId,
    Guid PropertyId,
    ChannelKind Channel,
    string ICalUid) : DomainEvent;

public sealed record SyncConflictDetected(
    Guid ConflictId,
    Guid PropertyId,
    Guid BookingId,
    Guid ExternalReservationId,
    ChannelKind Channel,
    Guid TenantId) : DomainEvent;

public sealed record SyncRunFailed(
    Guid TenantId,
    Guid ChannelFeedId,
    Guid PropertyId,
    ChannelKind Channel,
    int ConsecutiveFailures,
    string Error) : DomainEvent;
