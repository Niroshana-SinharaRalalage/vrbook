using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Booking.Infrastructure.Persistence;

namespace VrBook.Modules.Booking.Application.Commands;

/// <summary>
/// Slice 5 — daily sweep over <see cref="BookingStatus.CheckedOut"/> bookings
/// whose <c>CheckedOutAt</c> is at least 24h old. Calls
/// <c>Booking.Complete()</c> on each, which raises <c>BookingCompleted</c>;
/// the in-process MediatR pipeline then runs Loyalty's
/// <c>OnBookingCompletedHandler</c> (increment stay count, raise
/// <c>TierPromoted</c>) and Notifications'
/// <c>BookingNotificationHandlers</c> (queue the "thanks for staying" email
/// and the deferred review request).
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
    private static readonly TimeSpan CompletionDelay = TimeSpan.FromHours(24);

    public async Task<CompletionSweepResult> Handle(CompletionSweepCommand request, CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow - CompletionDelay;
        var dueBookings = await db.Bookings
            .Where(b => b.Status == BookingStatus.CheckedOut
                        && b.CheckedOutAt != null
                        && b.CheckedOutAt <= cutoff)
            .ToListAsync(cancellationToken);

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
