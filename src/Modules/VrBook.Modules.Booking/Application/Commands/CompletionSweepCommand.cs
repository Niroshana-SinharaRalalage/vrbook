using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Booking.Infrastructure.Persistence;

namespace VrBook.Modules.Booking.Application.Commands;

/// <summary>
/// Slice 5 — daily sweep over <see cref="BookingStatus.CheckedOut"/> bookings.
/// Slice OPS.M.16 — predicate now reads the snapshotted
/// <see cref="VrBook.Modules.Booking.Domain.Booking.CompletionDueAt"/> instead
/// of the hardcoded 24h delay. Effective turnover window is per-property (with
/// per-booking override), stamped at CheckOut time. Calls
/// <c>Booking.Complete()</c> which emits <c>BookingCompleted(Trigger="sweep")</c>;
/// the in-process MediatR pipeline then runs Loyalty's
/// <c>OnBookingCompletedHandler</c> (increment stay count, raise
/// <c>TierPromoted</c>) and Notifications' <c>BookingNotificationHandlers</c>
/// (queue the "thanks for staying" email and the deferred review request).
///
/// Runs as the booking worker's <c>--mode=completion</c> Container App Job
/// (cron <c>0 6 * * *</c>); see <c>SLICE5_PLAN.md §2.1</c>.
/// </summary>
public sealed record CompletionSweepCommand : IRequest<CompletionSweepResult>;

public sealed record CompletionSweepResult(int Scanned, int Completed, int Skipped);

internal sealed class CompletionSweepHandler(
    BookingDbContext db,
    IDateTimeProvider clock,
    ILogger<CompletionSweepHandler> logger) : IRequestHandler<CompletionSweepCommand, CompletionSweepResult>
{
    public async Task<CompletionSweepResult> Handle(CompletionSweepCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // Slice OPS.M.16 — snapshot approach: read CompletionDueAt directly.
        // Rows where CompletionDueAt IS NULL (shouldn't happen post-M.16.2
        // backfill) are skipped by predicate. Diagnostic log below surfaces
        // any residual NULLs so a data-heal regression doesn't hide.
        var dueBookings = await db.Bookings
            .Where(b => b.Status == BookingStatus.CheckedOut
                        && b.CompletionDueAt != null
                        && b.CompletionDueAt <= now)
            .ToListAsync(cancellationToken);

        var orphanedCount = await db.Bookings
            .CountAsync(
                b => b.Status == BookingStatus.CheckedOut && b.CompletionDueAt == null,
                cancellationToken);
        if (orphanedCount > 0)
        {
            logger.LogWarning(
                "Completion sweep found {Count} CheckedOut booking(s) with NULL CompletionDueAt. Post-M.16.2 backfill should have stamped these; investigate.",
                orphanedCount);
        }

        var completed = 0;
        var skipped = 0;
        foreach (var booking in dueBookings)
        {
            try
            {
                booking.Complete();
                completed++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Skipping completion for booking {BookingId}: {Reason}.",
                    booking.Id, ex.Message);
                skipped++;
            }
        }
        if (completed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Completion sweep: scanned={Scanned} completed={Completed} skipped={Skipped}.",
            dueBookings.Count, completed, skipped);

        return new CompletionSweepResult(dueBookings.Count, completed, skipped);
    }
}
