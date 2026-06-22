using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Events;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Notifications.Domain;
using VrBook.Modules.Notifications.Infrastructure.Persistence;

namespace VrBook.Modules.Notifications.Application.Handlers;

/// <summary>
/// Slice 4 C4: owner-side counterpart to <see cref="BookingNotificationHandlers"/>.
/// Resolves the property's owner via <see cref="IPropertyOwnerLookup"/> then the
/// owner's email via <see cref="IUserEmailLookup"/>; queues the owner-side
/// templates from <c>SLICE4_PLAN.md</c>:
///
/// <list type="bullet">
///   <item><c>BookingPlaced</c> → <see cref="NotificationKind.OwnerTentativeReceived"/>
///         AND <see cref="NotificationKind.OwnerActionRequiredReminder"/> with
///         <c>NotBeforeUtc = TentativeUntil - 1h</c> (per SLICE4_PLAN §2.3 — the
///         deferred-send path proves out C2's NotBeforeUtc column).</item>
///   <item><c>BookingConfirmed</c> where <c>Trigger == "sla"</c> →
///         <see cref="NotificationKind.OwnerAutoConfirmed"/>. Manual owner confirms
///         do NOT email the owner — they just clicked the button.</item>
///   <item><c>BookingCancelled</c> → <see cref="NotificationKind.OwnerCancellationAlert"/>.</item>
///   <item><c>BookingConflictDetected</c> → <see cref="NotificationKind.OwnerSyncConflict"/>.</item>
/// </list>
/// </summary>
internal sealed class OwnerNotificationHandlers(
    NotificationsDbContext db,
    IPropertyOwnerLookup properties,
    IBookingEmailLookup bookings,
    IUserEmailLookup users,
    IDateTimeProvider clock,
    ILogger<OwnerNotificationHandlers> logger) :
    INotificationHandler<BookingPlaced>,
    INotificationHandler<BookingConfirmed>,
    INotificationHandler<BookingCancelled>,
    INotificationHandler<BookingConflictDetected>
{
    private static readonly TimeSpan ReminderOffsetBeforeDeadline = TimeSpan.FromHours(1);

    public async Task Handle(BookingPlaced n, CancellationToken cancellationToken)
    {
        var ownerEmail = await ResolveOwnerEmail(n.PropertyId, cancellationToken);
        if (ownerEmail is null)
        {
            return;
        }

        await Queue(NotificationKind.OwnerTentativeReceived, n.BookingId, ownerEmail,
            $"Reservation request — {n.Reference}",
            extras: new() { ["TentativeUntil"] = n.TentativeUntil.ToString("o") },
            cancellationToken: cancellationToken);

        var reminderAt = n.TentativeUntil - ReminderOffsetBeforeDeadline;
        if (reminderAt <= clock.UtcNow)
        {
            logger.LogInformation(
                "Reminder for {Reference} would fire in the past ({ReminderAt}); skipping.",
                n.Reference, reminderAt);
            return;
        }
        await Queue(NotificationKind.OwnerActionRequiredReminder, n.BookingId, ownerEmail,
            $"Decision needed soon — {n.Reference}",
            extras: new() { ["TentativeUntil"] = n.TentativeUntil.ToString("o") },
            cancellationToken: cancellationToken,
            notBeforeUtc: reminderAt);
    }

    public async Task Handle(BookingConfirmed n, CancellationToken cancellationToken)
    {
        if (!string.Equals(n.Trigger, "sla", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var ownerEmail = await ResolveOwnerEmail(n.PropertyId, cancellationToken);
        if (ownerEmail is null)
        {
            return;
        }
        await Queue(NotificationKind.OwnerAutoConfirmed, n.BookingId, ownerEmail,
            $"Auto-confirmed — {n.Reference}",
            extras: new() { ["Trigger"] = n.Trigger },
            cancellationToken: cancellationToken);
    }

    public async Task Handle(BookingCancelled n, CancellationToken cancellationToken)
    {
        var ownerEmail = await ResolveOwnerEmail(n.PropertyId, cancellationToken);
        if (ownerEmail is null)
        {
            return;
        }
        await Queue(NotificationKind.OwnerCancellationAlert, n.BookingId, ownerEmail,
            $"Cancelled — {n.Reference}",
            extras: new()
            {
                ["CancelledBy"] = n.CancelledBy,
                ["RefundAmount"] = n.RefundAmount.ToString("F2"),
                ["RefundCurrency"] = n.Currency,
            },
            cancellationToken: cancellationToken);
    }

    public Task Handle(BookingConflictDetected n, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "BookingConflictDetected received for booking {BookingId}; owner.sync_conflict email skipped until a booking->owner lookup ships.",
            n.BookingId);
        return Task.CompletedTask;
    }

    private async Task<string?> ResolveOwnerEmail(Guid propertyId, CancellationToken cancellationToken)
    {
        var prop = await properties.GetAsync(propertyId, cancellationToken);
        if (prop is null)
        {
            logger.LogWarning("Property {PropertyId} not found; owner notification dropped.", propertyId);
            return null;
        }
        var user = await users.GetAsync(prop.OwnerUserId, cancellationToken);
        if (user is null)
        {
            logger.LogWarning(
                "Owner {OwnerId} of property {PropertyId} not found; owner notification dropped.",
                prop.OwnerUserId, propertyId);
            return null;
        }
        return user.Email;
    }

    private async Task Queue(
        NotificationKind kind,
        Guid bookingId,
        string ownerEmail,
        string subject,
        Dictionary<string, object>? extras,
        CancellationToken cancellationToken,
        DateTimeOffset? notBeforeUtc = null)
    {
        var booking = await bookings.GetAsync(bookingId, cancellationToken);
        if (booking is null)
        {
            logger.LogWarning(
                "Booking {BookingId} not found; queueing owner {Kind} without enriched payload.",
                bookingId, kind);
        }

        var payload = NotificationPayload.Build(booking, extras);
        var json = JsonSerializer.Serialize(payload);

        var log = NotificationLog.Queue(
            kind: kind,
            recipientUserId: Guid.Empty,
            recipientEmail: ownerEmail,
            subject: subject,
            payloadJson: json,
            notBeforeUtc: notBeforeUtc);
        db.Logs.Add(log);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Queued owner-side {Kind} -> {OwnerEmail} ({LogId}){Deferred}.",
            kind, ownerEmail, log.Id,
            notBeforeUtc is null ? "" : $" deferred until {notBeforeUtc:o}");
    }
}
