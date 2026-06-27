using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Sync.Application.ChannelFeeds.Commands;

// OPS.M.4 — channel feeds are owner-driven (per-tenant per-property). Controller
// stamps TenantId from currentUser.TenantId; the behavior validates equality.

public sealed record CreateChannelFeedCommand(
    Guid PropertyId,
    ChannelKind Channel,
    string InboundUrl,
    int PollIntervalMinutes,
    Guid TenantId) : IRequest<ChannelFeedDto>, ITenantScoped;

public sealed record UpdateChannelFeedCommand(
    Guid Id,
    string InboundUrl,
    int PollIntervalMinutes,
    bool IsEnabled,
    Guid TenantId) : IRequest<ChannelFeedDto>, ITenantScoped;

public sealed record DeleteChannelFeedCommand(Guid Id, Guid TenantId) : IRequest, ITenantScoped;
