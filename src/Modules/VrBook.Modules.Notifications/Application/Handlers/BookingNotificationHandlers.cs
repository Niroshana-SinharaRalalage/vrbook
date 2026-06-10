using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Events;
using VrBook.Modules.Notifications.Domain;
using VrBook.Modules.Notifications.Infrastructure.Persistence;

namespace VrBook.Modules.Notifications.Application.Handlers;

/// <summary>
/// A9 v1: every domain event the proposal §13 mentions for email gets a
/// <see cref="NotificationLog"/> row written here. The A9 worker (deferred —
/// needs Azure Communication Services resource provisioning) drains
/// <c>Queued</c> rows and calls ACS. Until then the rows are the audit
/// trail + replay log.
///
/// Recipient email is stubbed as a sentinel — the real lookup needs an
/// IUserEmailLookup interface across modules. Rows still go in with the
/// placeholder so the wiring is provable end-to-end on staging.
/// </summary>
internal sealed class BookingNotificationHandlers(
    NotificationsDbContext db,
    ILogger<BookingNotificationHandlers> logger) :
    INotificationHandler<BookingPlaced>,
    INotificationHandler<BookingConfirmed>,
    INotificationHandler<BookingCancelled>,
    INotificationHandler<BookingCompleted>
{
    public Task Handle(BookingPlaced notification, CancellationToken cancellationToken) =>
        Queue(NotificationKind.BookingPlaced, notification.GuestUserId,
            $"Booking received — {notification.Reference}", notification, cancellationToken);

    public Task Handle(BookingConfirmed notification, CancellationToken cancellationToken) =>
        Queue(NotificationKind.BookingConfirmed, notification.GuestUserId,
            $"Your booking is confirmed — {notification.Reference}", notification, cancellationToken);

    public Task Handle(BookingCancelled notification, CancellationToken cancellationToken) =>
        Queue(NotificationKind.BookingCancelled, notification.GuestUserId,
            $"Booking cancelled — {notification.Reference}", notification, cancellationToken);

    public Task Handle(BookingCompleted notification, CancellationToken cancellationToken) =>
        Queue(NotificationKind.BookingCompleted, notification.GuestUserId,
            $"Thanks for staying — {notification.Reference}", notification, cancellationToken);

    private async Task Queue(NotificationKind kind, Guid recipient, string subject, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType());
        var log = NotificationLog.Queue(
            kind,
            recipientUserId: recipient,
            recipientEmail: $"user-{recipient:N}@stub.vrbook",
            subject: subject,
            payloadJson: json);
        db.Logs.Add(log);
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Queued notification {Kind} for user {RecipientId} ({LogId}).",
            kind, recipient, log.Id);
    }
}
