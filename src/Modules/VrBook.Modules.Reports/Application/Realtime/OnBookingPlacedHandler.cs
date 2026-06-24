using MediatR;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Events;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Reports.Application.Realtime;

/// <summary>
/// Slice 7 — pushes "tentativeBookingAdded" to the property owner when a
/// guest places a new booking. Fire-and-forget per SLICE7_PLAN §2.5: the
/// MediatR handler returns immediately, the SignalR REST call runs in the
/// background, and any failure is logged without affecting the booking
/// transaction. Payload is lean ({ bookingId, reference, dates,
/// tentativeUntil }) so the dashboard refetches the canonical list on push.
/// </summary>
internal sealed class OnBookingPlacedHandler(
    IPropertyOwnerLookup ownerLookup,
    IRealtimeNotifier notifier,
    ILogger<OnBookingPlacedHandler> logger) : INotificationHandler<BookingPlaced>
{
    public Task Handle(BookingPlaced notification, CancellationToken cancellationToken)
    {
        // Fire-and-forget so booking-POST latency isn't gated on SignalR REST.
        // cancellationToken intentionally NOT passed - the host scope ends as
        // soon as Handle returns; CancellationToken.None lets the background
        // push run to completion.
        _ = Task.Run(async () =>
        {
            try
            {
                var owner = await ownerLookup.GetAsync(notification.PropertyId, CancellationToken.None);
                if (owner is null)
                {
                    logger.LogWarning(
                        "BookingPlaced for unknown property {PropertyId}; skipping realtime push.",
                        notification.PropertyId);
                    return;
                }

                var payload = new
                {
                    bookingId = notification.BookingId,
                    reference = notification.Reference,
                    checkinDate = notification.Checkin.ToString("yyyy-MM-dd"),
                    checkoutDate = notification.Checkout.ToString("yyyy-MM-dd"),
                    tentativeUntil = notification.TentativeUntil,
                };

                await notifier.NotifyUserAsync(
                    owner.OwnerUserId,
                    "tentativeBookingAdded",
                    payload,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "tentativeBookingAdded push failed for booking {BookingId}",
                    notification.BookingId);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }
}
