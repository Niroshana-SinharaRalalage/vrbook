using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Sync.Application.ChannelFeeds.Commands;
using VrBook.Modules.Sync.Application.Common;
using VrBook.Modules.Sync.Infrastructure.Persistence;

namespace VrBook.Modules.Sync.Application.ChannelFeeds.Queries;

public sealed record ListChannelFeedsQuery : IRequest<IReadOnlyList<ChannelFeedDto>>;

public sealed record GetChannelFeedQuery(Guid Id) : IRequest<ChannelFeedDto>;

internal sealed class ListChannelFeedsHandler(
    IChannelFeedRepository feeds,
    IFeedUrlBuilder urls) : IRequestHandler<ListChannelFeedsQuery, IReadOnlyList<ChannelFeedDto>>
{
    public async Task<IReadOnlyList<ChannelFeedDto>> Handle(ListChannelFeedsQuery request, CancellationToken cancellationToken)
    {
        var all = await feeds.ListAsync(cancellationToken);
        return all.Select(f => f.ToDto(urls.OutboundFeedUrl(f.OutboundToken), propertyTitle: string.Empty)).ToArray();
    }
}

internal sealed class GetChannelFeedHandler(
    IChannelFeedRepository feeds,
    IFeedUrlBuilder urls) : IRequestHandler<GetChannelFeedQuery, ChannelFeedDto>
{
    public async Task<ChannelFeedDto> Handle(GetChannelFeedQuery request, CancellationToken cancellationToken)
    {
        var feed = await feeds.GetAsync(request.Id, cancellationToken)
            ?? throw new VrBook.Domain.Common.NotFoundException("ChannelFeed", request.Id);
        return feed.ToDto(urls.OutboundFeedUrl(feed.OutboundToken), propertyTitle: string.Empty);
    }
}
