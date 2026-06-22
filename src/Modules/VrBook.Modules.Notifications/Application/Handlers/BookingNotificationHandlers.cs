using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Configuration;
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
    IBookingEmailLookup bookings,
    IConfiguration configuration,
    IDateTimeProvider clock,
    ILogger<BookingNotificationHandlers> logger) :
    INotificationHandler<BookingPlaced>,
    INotificationHandler<BookingConfirmed>,
    INotificationHandler<BookingRejected>,
    INotificationHandler<BookingCancelled>,
    INotificationHandler<BookingCompleted>
{
    public Task Handle(BookingPlaced n, CancellationToken cancellationToken) =>
        Queue(NotificationKind.BookingPlaced, n.BookingId, n.GuestUserId,
            $"Booking received — {n.Reference}",
            extras: new() { ["TentativeUntil"] = n.TentativeUntil.ToString("o") },
            cancellationToken: cancellationToken);

    public Task Handle(BookingConfirmed n, CancellationToken cancellationToken) =>
        Queue(NotificationKind.BookingConfirmed, n.BookingId, n.GuestUserId,
            $"Your booking is confirmed — {n.Reference}",
            extras: new() { ["Trigger"] = n.Trigger },
            cancellationToken: cancellationToken);

    public Task Handle(BookingRejected n, CancellationToken cancellationToken) =>
        Queue(NotificationKind.BookingRejected, n.BookingId, n.GuestUserId,
            $"Booking declined — {n.Reference}",
            extras: new() { ["Reason"] = n.Reason },
            cancellationToken: cancellationToken);

    public Task Handle(BookingCancelled n, CancellationToken cancellationToken) =>
        Queue(NotificationKind.BookingCancelled, n.BookingId, n.GuestUserId,
            $"Booking cancelled — {n.Reference}",
            extras: new()
            {
                ["CancelledBy"] = n.CancelledBy,
                ["RefundAmount"] = n.RefundAmount.ToString("F2"),
                ["RefundCurrency"] = n.Currency,
            },
            cancellationToken: cancellationToken);

    public async Task Handle(BookingCompleted n, CancellationToken cancellationToken)
    {
        // (1) Immediate "thanks for staying" row.
        await Queue(NotificationKind.BookingCompleted, n.BookingId, n.GuestUserId,
            $"Thanks for staying — {n.Reference}",
            extras: null,
            cancellationToken: cancellationToken);

        // (2) Deferred T+1 review request. C2's NotBeforeUtc gating means the
        //     dispatcher only picks this up 24h later — matches SLICE5_PLAN §2.3.
        var deepLink = BuildReviewDeepLink(n.BookingId);
        await Queue(NotificationKind.ReviewRequest, n.BookingId, n.GuestUserId,
            $"How was your stay? — {n.Reference}",
            extras: new() { ["DeepLink"] = deepLink },
            cancellationToken: cancellationToken,
            notBeforeUtc: clock.UtcNow + TimeSpan.FromHours(24));
    }

    private string BuildReviewDeepLink(Guid bookingId)
    {
        // DevAuth:WebBaseUrl is the existing config key (used by the persona-
        // switch redirect handoff). Phase 2 renames to App:WebBaseUrl.
        var webBase = configuration["DevAuth:WebBaseUrl"]?.TrimEnd('/')
            ?? "https://example.com";
        return $"{webBase}/account/bookings/{bookingId}/review";
    }

    private async Task Queue(
        NotificationKind kind,
        Guid bookingId,
        Guid recipient,
        string subject,
        Dictionary<string, object>? extras,
        CancellationToken cancellationToken,
        DateTimeOffset? notBeforeUtc = null)
    {
        var user = await users.GetAsync(recipient, cancellationToken);
        if (user is null)
        {
            logger.LogWarning(
                "User {RecipientId} not found in identity.users; skipping notification {Kind}.",
                recipient, kind);
            return;
        }

        var booking = await bookings.GetAsync(bookingId, cancellationToken);
        if (booking is null)
        {
            logger.LogWarning(
                "Booking {BookingId} not found; queueing {Kind} without enriched payload.",
                bookingId, kind);
        }

        var payload = NotificationPayload.Build(booking, extras);
        var json = JsonSerializer.Serialize(payload);

        var log = NotificationLog.Queue(
            kind,
            recipientUserId: recipient,
            recipientEmail: user.Email,
            subject: subject,
            payloadJson: json,
            notBeforeUtc: notBeforeUtc);
        db.Logs.Add(log);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Queued notification {Kind} for user {RecipientId} ({LogId}) -> {RecipientEmail}{Deferred}.",
            kind, recipient, log.Id, user.Email,
            notBeforeUtc is null ? "" : $" deferred until {notBeforeUtc:o}");
    }
}
