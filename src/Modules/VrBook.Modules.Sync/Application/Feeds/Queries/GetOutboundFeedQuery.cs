using MediatR;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Sync.Infrastructure.Persistence;

namespace VrBook.Modules.Sync.Application.Feeds.Queries;

/// <summary>
/// Renders the outbound iCal feed for one property given its OutboundToken.
/// Token is opaque — no auth required (the token IS the secret). Returns the
/// rendered ICS body as a string; caller wraps in a text/calendar response.
///
/// Redis 10-min caching deferred to a polish round — current implementation
/// computes fresh on every request, which is fine at Phase 1 volume.
/// </summary>
public sealed record GetOutboundFeedQuery(string OutboundToken) : IRequest<string>;

internal sealed class GetOutboundFeedHandler(
    IChannelFeedRepository feeds,
    IConfirmedBookingLookup bookings,
    IEnumerable<IExternalChannel> channels) : IRequestHandler<GetOutboundFeedQuery, string>
{
    public async Task<string> Handle(GetOutboundFeedQuery request, CancellationToken cancellationToken)
    {
        var feed = await feeds.GetByOutboundTokenAsync(request.OutboundToken, cancellationToken)
            ?? throw new NotFoundException("OutboundFeed", request.OutboundToken);

        // Pull all active direct bookings for the property from today onward.
        var bookingsList = await bookings.ListForOutboundFeedAsync(
            feed.PropertyId,
            from: DateOnly.FromDateTime(DateTime.UtcNow.Date),
            cancellationToken);

        // Convert into the channel-agnostic OutboundReservation shape.
        var reservations = bookingsList
            .Select(b => new OutboundReservation(
                Uid: $"vrbook-{b.BookingId:N}@vrbook",
                Checkin: b.Checkin,
                Checkout: b.Checkout,
                Summary: b.IsTentative ? $"VrBook tentative {b.Reference}" : $"VrBook booking {b.Reference}",
                IsTentative: b.IsTentative,
                LastModified: b.LastModified))
            .ToArray();

        // Pick the channel matching this feed's kind — it knows how to render
        // its outbound format. AirBnBICalChannel renders generic iCal which is
        // also what subscribers like Google Calendar consume.
        var channel = channels.FirstOrDefault(c => c.Kind == feed.Channel)
            ?? throw new BusinessRuleViolationException(
                "sync.channel.unsupported",
                $"No IExternalChannel registered for {feed.Channel}.");

        return await channel.RenderOutboundFeedAsync(feed.PropertyId, reservations, cancellationToken);
    }
}
