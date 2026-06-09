using VrBook.Contracts.Dtos;
using VrBook.Modules.Sync.Domain;

namespace VrBook.Modules.Sync.Application.Common;

internal static class Mapping
{
    public static ChannelFeedDto ToDto(this ChannelFeed feed, string outboundFeedUrl, string propertyTitle) =>
        new(
            feed.Id,
            feed.PropertyId,
            propertyTitle,
            feed.Channel,
            feed.InboundUrl,
            outboundFeedUrl,
            feed.PollIntervalMinutes,
            feed.IsEnabled,
            feed.LastSuccessAt,
            feed.LastAttemptAt,
            feed.LastError);

    public static SyncConflictDto ToDto(
        this SyncConflict c,
        string propertyTitle,
        ExternalReservation er,
        string bookingReference,
        DateOnly bookingCheckin,
        DateOnly bookingCheckout) =>
        new(
            c.Id,
            c.PropertyId,
            propertyTitle,
            c.ExternalReservationId,
            er.Summary ?? "Reserved",
            er.Checkin,
            er.Checkout,
            c.BookingId,
            bookingReference,
            bookingCheckin,
            bookingCheckout,
            c.Resolution,
            c.ResolutionNotes,
            c.DetectedAt,
            c.ResolvedAt);
}
