using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Notifications.Domain;
using VrBook.Modules.Notifications.Infrastructure.Persistence;

namespace VrBook.Modules.Notifications.Application.Dispatch;

/// <summary>
/// Slice 4 C2: entry point for the dispatch worker (cron <c>*/2 * * * *</c>).
/// Each invocation:
///   1. Resets stale <see cref="NotificationStatus.Sending"/> rows whose lease
///      has expired (5-minute timeout) back to <see cref="NotificationStatus.Queued"/>.
///   2. Picks up to <see cref="BatchSize"/> Queued rows where
///      <c>NotBeforeUtc IS NULL OR NotBeforeUtc &lt;= NOW()</c>.
///   3. Leases each row (Queued → Sending), dispatches via <see cref="IEmailSender"/>,
///      then marks <see cref="NotificationStatus.Sent"/> or
///      <see cref="NotificationStatus.Failed"/> / DeadLetter on failure.
///
/// <para>
/// Returns a small summary so the worker can log and exit with a meaningful code.
/// </para>
/// </summary>
public sealed record DrainQueuedNotificationsCommand(int BatchSize = 50) : IRequest<DrainResult>;

public sealed record DrainResult(int Released, int Picked, int Sent, int Failed, int DeadLettered);

internal sealed class DrainQueuedNotificationsHandler(
    NotificationsDbContext db,
    IEmailSender sender,
    IDateTimeProvider clock,
    ILogger<DrainQueuedNotificationsHandler> logger)
    : IRequestHandler<DrainQueuedNotificationsCommand, DrainResult>
{
    private static readonly TimeSpan LeaseTimeout = TimeSpan.FromMinutes(5);

    public async Task<DrainResult> Handle(DrainQueuedNotificationsCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var cutoff = now - LeaseTimeout;

        // (1) Release expired leases. Crashed-worker recovery.
        var stale = await db.Logs
            .Where(x => x.Status == NotificationStatus.Sending &&
                        x.DispatchStartedAt != null &&
                        x.DispatchStartedAt <= cutoff)
            .ToListAsync(cancellationToken);
        foreach (var s in stale)
        {
            s.ReleaseLease(cutoff);
        }
        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Released {Count} stale Sending leases (>{Timeout}m).", stale.Count, LeaseTimeout.TotalMinutes);
        }

        // (2) Pick due Queued rows.
        var due = await db.Logs
            .Where(x => x.Status == NotificationStatus.Queued &&
                        (x.NotBeforeUtc == null || x.NotBeforeUtc <= now))
            .OrderBy(x => x.CreatedAt)
            .Take(request.BatchSize)
            .ToListAsync(cancellationToken);

        var sent = 0;
        var failed = 0;
        var deadLettered = 0;

        foreach (var row in due)
        {
            // (3a) Lease.
            try
            {
                row.Lease(clock.UtcNow);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogInformation(ex,
                    "Concurrent lease on {LogId}; another replica claimed it. Skipping.",
                    row.Id);
                continue;
            }

            // (3b) Dispatch.
            EmailDispatchResult outcome;
            try
            {
                outcome = await sender.SendAsync(
                    new EmailDispatchRequest(
                        ToEmail: row.RecipientEmail,
                        ToDisplayName: row.RecipientEmail,
                        Subject: row.Subject,
                        // C2 sends raw payload; C3 swaps in Mustache-rendered HTML/text.
                        HtmlBody: $"<pre>{System.Net.WebUtility.HtmlEncode(row.PayloadJson)}</pre>",
                        PlainTextBody: row.PayloadJson),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sender threw for {LogId}.", row.Id);
                outcome = EmailDispatchResult.Failure(ex.Message);
            }

            // (3c) Record outcome.
            if (outcome.IsSuccess)
            {
                row.MarkSent(clock.UtcNow);
                sent++;
            }
            else
            {
                row.RecordFailure(outcome.Error ?? "unknown");
                if (row.Status == NotificationStatus.DeadLetter)
                {
                    deadLettered++;
                }
                else
                {
                    failed++;
                }
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        return new DrainResult(stale.Count, due.Count, sent, failed, deadLettered);
    }
}
