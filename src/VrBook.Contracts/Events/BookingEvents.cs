using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Events;

// OPS.M.4 Step 1 — every booking event that has a downstream cross-module consumer
// (Notifications, Messaging, Sync, future Reports) gains Guid TenantId so the
// consumer doesn't have to round-trip through a cross-schema property lookup.
// BookingCheckedIn / BookingCheckedOut / BookingDisputed are NOT bumped per
// OPS_M_4_PLAN §4 — no demonstrated cross-module consumer needs the field yet.

public sealed record BookingDraftCreated(
    Guid BookingId,
    string Reference,
    Guid PropertyId,
    Guid GuestUserId,
    DateOnly Checkin,
    DateOnly Checkout,
    Guid TenantId) : DomainEvent;

public sealed record BookingPlaced(
    Guid BookingId,
    string Reference,
    Guid PropertyId,
    Guid GuestUserId,
    DateOnly Checkin,
    DateOnly Checkout,
    DateTimeOffset TentativeUntil,
    Guid TenantId) : DomainEvent;

public sealed record BookingConfirmed(
    Guid BookingId,
    string Reference,
    Guid PropertyId,
    Guid GuestUserId,
    DateOnly Checkin,
    DateOnly Checkout,
    string Trigger,
    Guid TenantId) : DomainEvent; // Trigger: "owner" | "sla"

public sealed record BookingRejected(
    Guid BookingId,
    string Reference,
    Guid PropertyId,
    Guid GuestUserId,
    string Reason,
    Guid TenantId) : DomainEvent;

public sealed record BookingCancelled(
    Guid BookingId,
    string Reference,
    Guid PropertyId,
    Guid GuestUserId,
    string CancelledBy,        // "guest" | "owner" | "system"
    decimal RefundAmount,
    string Currency,
    Guid TenantId) : DomainEvent;

public sealed record BookingCheckedIn(Guid BookingId, string Reference) : DomainEvent;

public sealed record BookingCheckedOut(Guid BookingId, string Reference) : DomainEvent;

public sealed record BookingCompleted(
    Guid BookingId,
    string Reference,
    Guid GuestUserId,
    Guid TenantId,
    string Trigger = "sweep") : DomainEvent; // Trigger: "sweep" | "manual" (OPS.M.16 discriminator; legacy outbox rows default to "sweep")

/// <summary>
/// Slice OPS.M.16 — raised when an admin reschedules a CheckedOut booking's
/// auto-completion window (POST /api/v1/bookings/{id}/schedule-completion).
/// Audit-trail only today; the housekeeping module (future slice) is the
/// intended downstream consumer.
/// </summary>
public sealed record BookingCompletionRescheduled(
    Guid BookingId,
    DateTimeOffset DueAt,
    int HoursFromCheckedOutAt,
    Guid TenantId) : DomainEvent;

public sealed record BookingDisputed(
    Guid BookingId,
    string Reference,
    string DisputeReason) : DomainEvent;

public sealed record BookingConflictDetected(
    Guid BookingId,
    Guid ExternalReservationId,
    ChannelKind Channel,
    Guid TenantId) : DomainEvent;
