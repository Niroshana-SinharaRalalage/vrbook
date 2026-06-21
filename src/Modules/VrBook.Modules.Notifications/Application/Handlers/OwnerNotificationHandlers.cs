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
    IUserEmailLookup users,
    IDateTimeProvider clock,
    ILogger<OwnerNotificationHandlers> logger) :
    INotificationHandler<BookingPlaced>,
    INotificationHandler<BookingConfirmed>,
    INotificationHandler<BookingCancelled>,
    INotificationHandler<BookingConflictDetected>
{
    private static readonly TimeSpan ReminderOffsetBeforeDeadline = TimeSpan.FromHours(1);

    public async Task Handle(BookingPlaced notification, CancellationToken cancellationToken)
    {
        var ownerEmail = await ResolveOwnerEmail(notification.PropertyId, cancellationToken);
        if (ownerEmail is null)
        {
            return;
        }

        // (1) Immediate "you have a new request" email.
        await Queue(
            NotificationKind.OwnerTentativeReceived,
            ownerEmail,
            $"Reservation request — {notification.Reference}",
            notification, cancellationToken);

        // (2) Deferred 1h-before-deadline reminder. C2's NotBeforeUtc column
        //     means the dispatcher only picks this up when the time arrives.
        var reminderAt = notification.TentativeUntil - ReminderOffsetBeforeDeadline;
        if (reminderAt <= clock.UtcNow)
        {
            logger.LogInformation(
                "Reminder for {Reference} would fire in the past ({ReminderAt}); skipping.",
                notification.Reference, reminderAt);
            return;
        }
        await Queue(
            NotificationKind.OwnerActionRequiredReminder,
            ownerEmail,
            $"Decision needed soon — {notification.Reference}",
            notification, cancellationToken,
            notBeforeUtc: reminderAt);
    }

    public async Task Handle(BookingConfirmed notification, CancellationToken cancellationToken)
    {
        // Manual owner-driven confirm doesn't need a courtesy email back to the
        // owner who just clicked the button.
        if (!string.Equals(notification.Trigger, "sla", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var ownerEmail = await ResolveOwnerEmail(notification.PropertyId, cancellationToken);
        if (ownerEmail is null)
        {
            return;
        }
        await Queue(
            NotificationKind.OwnerAutoConfirmed,
            ownerEmail,
            $"Auto-confirmed — {notification.Reference}",
            notification, cancellationToken);
    }

    public async Task Handle(BookingCancelled notification, CancellationToken cancellationToken)
    {
        var ownerEmail = await ResolveOwnerEmail(notification.PropertyId, cancellationToken);
        if (ownerEmail is null)
        {
            return;
        }
        await Queue(
            NotificationKind.OwnerCancellationAlert,
            ownerEmail,
            $"Cancelled — {notification.Reference}",
            notification, cancellationToken);
    }

    public Task Handle(BookingConflictDetected notification, CancellationToken cancellationToken)
    {
        // BookingConflictDetected carries BookingId (not PropertyId). To resolve
        // the owner we would need the booking → property hop, which is a cross-
        // module read we have not wired yet. For Slice 4 we log the event and
        // defer the sync_conflict email until OPS / Slice 6 add the booking
        // lookup. This keeps C4 acceptance criteria 1+3 unblocked.
        logger.LogInformation(
            "BookingConflictDetected received for booking {BookingId}; owner.sync_conflict email skipped until a booking->owner lookup ships.",
            notification.BookingId);
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
        string ownerEmail,
        string subject,
        object payload,
        CancellationToken cancellationToken,
        DateTimeOffset? notBeforeUtc = null)
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType());
        var log = NotificationLog.Queue(
            kind: kind,
            recipientUserId: Guid.Empty, // owner-side rows carry the email only.
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
