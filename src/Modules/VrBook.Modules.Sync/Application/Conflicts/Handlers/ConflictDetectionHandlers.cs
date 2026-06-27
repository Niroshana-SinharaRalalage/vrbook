using MediatR;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Sync.Domain;
using VrBook.Modules.Sync.Infrastructure.Persistence;

namespace VrBook.Modules.Sync.Application.Conflicts.Handlers;

/// <summary>
/// Symmetric pair of in-process notification handlers that record
/// <see cref="SyncConflict"/> rows when overlap is detected. Both paths share
/// the static <c>ConflictDetectionHelpers.RecordIfMissingAsync</c> helper so the
/// dedupe + create logic lives in one place.
///
/// Architectural decision (system-architect 2026-06-09): conflicts are
/// RECORDED, not BLOCKED. The booking confirm flow still succeeds; the conflict
/// row surfaces in the admin /admin/sync UI for owner resolution.
///
/// Both handlers commit through <see cref="SyncDbContext"/> which fires the
/// SyncConflictDetected event via the A0.3 outbox interceptor.
/// </summary>
internal sealed class OnExternalReservationImported(
    SyncDbContext db,
    ISyncConflictRepository conflicts,
    IConfirmedBookingLookup bookings,
    IPropertyOwnerLookup properties,
    ILogger<OnExternalReservationImported> logger)
    : INotificationHandler<ExternalReservationImported>
{
    public async Task Handle(ExternalReservationImported notification, CancellationToken cancellationToken)
    {
        var bookingOverlaps = await bookings.FindOverlappingAsync(
            notification.PropertyId, notification.Checkin, notification.Checkout, cancellationToken);

        var owner = await properties.GetAsync(notification.PropertyId, cancellationToken);
        var tenantId = owner!.TenantId;

        if (bookingOverlaps.Count == 0)
        {
            return;
        }

        var created = 0;
        foreach (var b in bookingOverlaps)
        {
            if (await ConflictDetectionHelpers.RecordIfMissingAsync(
                db, conflicts, tenantId, notification.PropertyId, b.BookingId,
                notification.ExternalReservationId, notification.Channel, cancellationToken))
            {
                created++;
            }
        }
        if (created > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "External reservation {ExternalId} for property {PropertyId} overlapped {Overlaps} direct booking(s); created {Created} SyncConflict row(s).",
                notification.ExternalReservationId, notification.PropertyId, bookingOverlaps.Count, created);
        }
    }
}

internal sealed class OnBookingConfirmed(
    SyncDbContext db,
    ISyncConflictRepository conflicts,
    IExternalChannelConflictChecker externalChannel,
    IPropertyOwnerLookup properties,
    ILogger<OnBookingConfirmed> logger)
    : INotificationHandler<BookingConfirmed>
{
    public async Task Handle(BookingConfirmed notification, CancellationToken cancellationToken)
    {
        var externalOverlaps = await externalChannel.FindOverlappingAsync(
            notification.PropertyId, notification.Checkin, notification.Checkout, cancellationToken);

        var owner = await properties.GetAsync(notification.PropertyId, cancellationToken);
        var tenantId = owner!.TenantId;

        if (externalOverlaps.Count == 0)
        {
            return;
        }

        var created = 0;
        foreach (var er in externalOverlaps)
        {
            if (await ConflictDetectionHelpers.RecordIfMissingAsync(
                db, conflicts, tenantId, notification.PropertyId, notification.BookingId,
                er.ExternalReservationId, er.Channel, cancellationToken))
            {
                created++;
            }
        }
        if (created > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Confirmed booking {BookingId} for property {PropertyId} overlapped {Overlaps} external reservation(s); created {Created} SyncConflict row(s).",
                notification.BookingId, notification.PropertyId, externalOverlaps.Count, created);
        }
    }
}

internal static class ConflictDetectionHelpers
{
    /// <summary>
    /// Idempotency check + Detect. Returns true if a new conflict was added to
    /// the context (caller must SaveChanges). The partial unique index
    /// <c>ux_sync_conflicts_booking_external_pending</c> defends against the
    /// race when two handlers fire concurrently.
    /// </summary>
    public static async Task<bool> RecordIfMissingAsync(
        SyncDbContext db,
        ISyncConflictRepository conflicts,
        Guid tenantId,
        Guid propertyId,
        Guid bookingId,
        Guid externalReservationId,
        ChannelKind channel,
        CancellationToken ct)
    {
        var existing = await conflicts.FindByPairAsync(bookingId, externalReservationId, ct);
        if (existing is not null && !existing.IsResolved)
        {
            return false; // already recorded and still pending
        }
        var conflict = SyncConflict.Detect(tenantId, propertyId, bookingId, externalReservationId, channel);
        db.SyncConflicts.Add(conflict);
        return true;
    }
}
