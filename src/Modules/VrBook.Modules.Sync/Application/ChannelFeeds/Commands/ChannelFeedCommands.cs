using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;

namespace VrBook.Modules.Sync.Application.ChannelFeeds.Commands;

public sealed record CreateChannelFeedCommand(
    Guid PropertyId,
    ChannelKind Channel,
    string InboundUrl,
    int PollIntervalMinutes) : IRequest<ChannelFeedDto>;

public sealed record UpdateChannelFeedCommand(
    Guid Id,
    string InboundUrl,
    int PollIntervalMinutes,
    bool IsEnabled) : IRequest<ChannelFeedDto>;

public sealed record DeleteChannelFeedCommand(Guid Id) : IRequest;
