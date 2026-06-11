using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Payment.Application.Commands;

namespace VrBook.Modules.Booking.Application.Commands;

/// <summary>
/// Slice 0.4 — SLA expiry sweep. The booking expiry worker invokes this once
/// per cron tick. Scans Tentative bookings whose <c>TentativeUntil</c> has
/// passed and, per booking:
///   * Auto-confirms if no overlapping external reservation exists (captures the PI).
///   * Auto-cancels if a conflict exists OR the PI auth has lapsed (releases the
///     uncaptured hold via RefundForBookingCommand which calls Stripe Cancel
///     on uncaptured PIs).
///
/// Idempotent: re-running the sweep on the same set is safe; only Tentative
/// bookings are touched, and a booking already transitioned out of Tentative
/// is skipped silently.
/// </summary>
public sealed record ExpirySweepCommand : IRequest<ExpirySweepResult>;

public sealed record ExpirySweepResult(int Scanned, int AutoConfirmed, int AutoExpired, int Skipped);

internal sealed class ExpirySweepHandler(
    BookingDbContext db,
    IMediator mediator,
    IExternalChannelConflictChecker conflictChecker,
    ILogger<ExpirySweepHandler> logger) : IRequestHandler<ExpirySweepCommand, ExpirySweepResult>
{
    public async Task<ExpirySweepResult> Handle(ExpirySweepCommand request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        // Pull all Tentative bookings whose window has expired. Bounded by status
        // filter + index on TentativeUntil; sweep size is small in practice.
        var expired = await db.Bookings
            .Where(b => b.Status == BookingStatus.Tentative
                     && b.TentativeUntil != null
                     && b.TentativeUntil <= now)
            .ToListAsync(cancellationToken);

        var autoConfirmed = 0;
        var autoExpired = 0;
        var skipped = 0;

        foreach (var booking in expired)
        {
            try
            {
                var hasOverlap = await conflictChecker.HasOverlapAsync(
                    booking.PropertyId,
                    booking.Stay.CheckinDate,
                    booking.Stay.CheckoutDate,
                    cancellationToken);

                if (hasOverlap)
                {
                    booking.AutoExpire("External reservation overlaps these dates; tentative window elapsed.");
                    await db.SaveChangesAsync(cancellationToken);
                    // Release the uncaptured PaymentIntent so the guest sees no charge.
                    await mediator.Send(
                        new RefundForBookingCommand(booking.Id, null, "sla_expired_with_conflict"),
                        cancellationToken);
                    autoExpired++;
                    logger.LogInformation(
                        "Auto-expired booking {BookingId} (property {PropertyId}) due to iCal conflict.",
                        booking.Id, booking.PropertyId);
                }
                else
                {
                    booking.AutoConfirm();
                    await db.SaveChangesAsync(cancellationToken);
                    // Capture the held funds.
                    await mediator.Send(
                        new CapturePaymentIntentForBookingCommand(booking.Id),
                        cancellationToken);
                    autoConfirmed++;
                    logger.LogInformation(
                        "Auto-confirmed booking {BookingId} (property {PropertyId}) — SLA window elapsed without owner action and no conflicts.",
                        booking.Id, booking.PropertyId);
                }
            }
            catch (Exception ex)
            {
                // Per-booking failure shouldn't stop the sweep. Will be picked up
                // on the next tick.
                skipped++;
                logger.LogWarning(ex,
                    "Failed to auto-transition booking {BookingId}; will retry on next sweep.",
                    booking.Id);
            }
        }

        return new ExpirySweepResult(expired.Count, autoConfirmed, autoExpired, skipped);
    }
}
