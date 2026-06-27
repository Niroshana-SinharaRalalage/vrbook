using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Events;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Messaging.Domain;
using VrBook.Modules.Messaging.Infrastructure.Persistence;

namespace VrBook.Modules.Messaging.Application.Threads.Handlers;

/// <summary>
/// A7.4 — first real cross-module event consumer. When Booking emits
/// <c>BookingConfirmed</c>, create the host↔guest thread for that booking if it
/// doesn't already exist. Idempotent: re-firing the event (e.g. from the A11
/// outbox relay) finds the existing thread and no-ops.
///
/// This is the wire that proves A0.3 actually delivers events end-to-end.
/// </summary>
internal sealed class OnBookingConfirmedHandler(
    MessagingDbContext db,
    IBookingMessagingContext bookings,
    ILogger<OnBookingConfirmedHandler> logger) : INotificationHandler<BookingConfirmed>
{
    public async Task Handle(BookingConfirmed notification, CancellationToken cancellationToken)
    {
        var exists = await db.Threads.AnyAsync(t => t.BookingId == notification.BookingId, cancellationToken);
        if (exists)
        {
            return; // idempotent — already wired up
        }

        var snapshot = await bookings.GetAsync(notification.BookingId, cancellationToken);
        if (snapshot is null)
        {
            logger.LogWarning(
                "Could not resolve messaging context for booking {BookingId} — skipping thread creation.",
                notification.BookingId);
            return;
        }

        // OPS.M.3 — pipe TenantId from the booking's property. Fall back to the
        // default tenant when Catalog 3b hasn't backfilled yet.
        var tenantId = snapshot.TenantId ?? new Guid("00000000-0000-0000-0000-000000000001");
        var thread = MessageThread.CreateForBooking(
            tenantId,
            snapshot.BookingId,
            snapshot.Reference,
            snapshot.GuestUserId,
            snapshot.GuestDisplayName,
            snapshot.OwnerUserId,
            snapshot.OwnerDisplayName);
        db.Threads.Add(thread);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Auto-created messaging thread {ThreadId} for booking {BookingId} (guest={GuestId} owner={OwnerId}).",
            thread.Id, snapshot.BookingId, snapshot.GuestUserId, snapshot.OwnerUserId);
    }
}
