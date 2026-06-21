using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Events;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Notifications.Domain;
using VrBook.Modules.Notifications.Infrastructure.Persistence;

namespace VrBook.Modules.Notifications.Application.Handlers;

/// <summary>
/// Slice 4 C1: every guest-side domain event the proposal §13 mentions for email
/// gets a <see cref="NotificationLog"/> row written here. The Slice 4 dispatch
/// worker (C2) drains <c>Queued</c> rows and calls ACS.
///
/// <para>
/// Recipient email is resolved via <see cref="IUserEmailLookup"/> (implemented
/// in Identity). When the lookup returns null — an internal consistency error
/// since the booking would not have been placed by a missing user — we log a
/// warning and skip queueing. We do not roll back the originating event because
/// the booking itself is real and persisted.
/// </para>
/// </summary>
internal sealed class BookingNotificationHandlers(
    NotificationsDbContext db,
    IUserEmailLookup users,
    ILogger<BookingNotificationHandlers> logger) :
    INotificationHandler<BookingPlaced>,
    INotificationHandler<BookingConfirmed>,
    INotificationHandler<BookingRejected>,
    INotificationHandler<BookingCancelled>,
    INotificationHandler<BookingCompleted>
{
    public Task Handle(BookingPlaced notification, CancellationToken cancellationToken) =>
        Queue(NotificationKind.BookingPlaced, notification.GuestUserId,
            $"Booking received — {notification.Reference}", notification, cancellationToken);

    public Task Handle(BookingConfirmed notification, CancellationToken cancellationToken) =>
        Queue(NotificationKind.BookingConfirmed, notification.GuestUserId,
            $"Your booking is confirmed — {notification.Reference}", notification, cancellationToken);

    public Task Handle(BookingRejected notification, CancellationToken cancellationToken) =>
        Queue(NotificationKind.BookingRejected, notification.GuestUserId,
            $"Booking declined — {notification.Reference}", notification, cancellationToken);

    public Task Handle(BookingCancelled notification, CancellationToken cancellationToken) =>
        Queue(NotificationKind.BookingCancelled, notification.GuestUserId,
            $"Booking cancelled — {notification.Reference}", notification, cancellationToken);

    public Task Handle(BookingCompleted notification, CancellationToken cancellationToken) =>
        Queue(NotificationKind.BookingCompleted, notification.GuestUserId,
            $"Thanks for staying — {notification.Reference}", notification, cancellationToken);

    private async Task Queue(NotificationKind kind, Guid recipient, string subject, object payload, CancellationToken ct)
    {
        var snapshot = await users.GetAsync(recipient, ct);
        if (snapshot is null)
        {
            logger.LogWarning(
                "User {RecipientId} not found in identity.users; cannot queue notification {Kind}. " +
                "Booking funnel continues without an email for this event.",
                recipient, kind);
            return;
        }

        var json = JsonSerializer.Serialize(payload, payload.GetType());
        var log = NotificationLog.Queue(
            kind,
            recipientUserId: recipient,
            recipientEmail: snapshot.Email,
            subject: subject,
            payloadJson: json);
        db.Logs.Add(log);
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Queued notification {Kind} for user {RecipientId} ({LogId}) -> {RecipientEmail}.",
            kind, recipient, log.Id, snapshot.Email);
    }
}
