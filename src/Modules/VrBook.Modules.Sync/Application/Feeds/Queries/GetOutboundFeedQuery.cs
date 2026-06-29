using MediatR;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Infrastructure.Persistence;
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
    IGuestTenantResolver guestTenant,
    IEnumerable<IExternalChannel> channels) : IRequestHandler<GetOutboundFeedQuery, string>
{
    public async Task<string> Handle(GetOutboundFeedQuery request, CancellationToken cancellationToken)
    {
        // Slice OPS.M.9.1 F6e — closes audit #7. [AllowAnonymous] endpoint;
        // the outbound token IS the credential. M.9 RLS denied every read
        // of sync.channel_feeds AND booking.bookings, so every iCal
        // subscriber (Airbnb, VRBO, Google Calendar) got empty feeds.
        // CarveOutAppLayerTests.Outbound_iCal_feed_with_unknown_token_returns_404
        // false-passed because all tokens 404'd — that test now needs a
        // valid-token-200 partner (added in this commit's test pack).
        //
        // Resolve tenant from the token first (the resolver opens its own
        // scoped bypass against sync.channel_feeds), then open a
        // BackgroundTenantScope so the feed metadata reload + booking
        // lookup inside the handler run under the right tenant.
        var tenantId = await guestTenant.ResolveFromOutboundTokenAsync(request.OutboundToken, cancellationToken)
            ?? throw new NotFoundException("OutboundFeed", request.OutboundToken);
        using var tenantScope = BackgroundTenantScope.Enter(tenantId);

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
