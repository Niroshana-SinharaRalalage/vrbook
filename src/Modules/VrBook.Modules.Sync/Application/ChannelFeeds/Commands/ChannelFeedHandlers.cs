using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Sync.Application.Common;
using VrBook.Modules.Sync.Domain;
using VrBook.Modules.Sync.Infrastructure.Persistence;

namespace VrBook.Modules.Sync.Application.ChannelFeeds.Commands;

internal sealed class CreateChannelFeedHandler(SyncDbContext db, IFeedUrlBuilder urls, IPropertyOwnerLookup properties)
    : IRequestHandler<CreateChannelFeedCommand, ChannelFeedDto>
{
    public async Task<ChannelFeedDto> Handle(CreateChannelFeedCommand cmd, CancellationToken cancellationToken)
    {
        // One feed per (property, channel) — enforce here to avoid race conditions on the
        // unique index (which we don't have on (property_id, channel)).
        var existing = await db.ChannelFeeds
            .AnyAsync(f => f.PropertyId == cmd.PropertyId && f.Channel == cmd.Channel, cancellationToken);
        if (existing)
        {
            throw new ConflictException(
                $"A {cmd.Channel} feed already exists for property {cmd.PropertyId}. Update the existing feed instead.");
        }

        // OPS.M.3 — inherit tenant from the property; fall back to default tenant
        // for pre-backfill Catalog rows.
        var owner = await properties.GetAsync(cmd.PropertyId, cancellationToken);
        var tenantId = owner!.TenantId;

        var feed = ChannelFeed.Create(tenantId, cmd.PropertyId, cmd.Channel, cmd.InboundUrl, cmd.PollIntervalMinutes);
        db.ChannelFeeds.Add(feed);
        await db.SaveChangesAsync(cancellationToken);
        return feed.ToDto(urls.OutboundFeedUrl(feed.OutboundToken), propertyTitle: string.Empty);
    }
}

internal sealed class UpdateChannelFeedHandler(SyncDbContext db, IFeedUrlBuilder urls)
    : IRequestHandler<UpdateChannelFeedCommand, ChannelFeedDto>
{
    public async Task<ChannelFeedDto> Handle(UpdateChannelFeedCommand cmd, CancellationToken cancellationToken)
    {
        var feed = await db.ChannelFeeds.FirstOrDefaultAsync(f => f.Id == cmd.Id, cancellationToken)
            ?? throw new NotFoundException("ChannelFeed", cmd.Id);
        // Slice OPS.M.10.2 F7 (audit #19) — defense-in-depth row-level
        // tenant equality. M.4 already gated cmd.TenantId == caller via
        // the ITenantScoped pipeline; this check binds the loaded row
        // back to the command. If RLS regressed, an Admin in tenant A
        // could mutate tenant B's feed by id; this is the safety net.
        if (feed.TenantId != cmd.TenantId)
        {
            throw new NotFoundException("ChannelFeed", cmd.Id);
        }
        feed.UpdateConfig(cmd.InboundUrl, cmd.PollIntervalMinutes, cmd.IsEnabled);
        await db.SaveChangesAsync(cancellationToken);
        return feed.ToDto(urls.OutboundFeedUrl(feed.OutboundToken), propertyTitle: string.Empty);
    }
}

internal sealed class DeleteChannelFeedHandler(SyncDbContext db) : IRequestHandler<DeleteChannelFeedCommand>
{
    public async Task Handle(DeleteChannelFeedCommand cmd, CancellationToken cancellationToken)
    {
        var feed = await db.ChannelFeeds.FirstOrDefaultAsync(f => f.Id == cmd.Id, cancellationToken)
            ?? throw new NotFoundException("ChannelFeed", cmd.Id);
        // Slice OPS.M.10.2 F7 (audit #19) — same defense-in-depth as
        // UpdateChannelFeedHandler above.
        if (feed.TenantId != cmd.TenantId)
        {
            throw new NotFoundException("ChannelFeed", cmd.Id);
        }
        // Soft delete via the BaseDbContext interceptor (DeletedAt is set on Remove).
        db.ChannelFeeds.Remove(feed);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Computes the absolute URL for the outbound iCal feed (<c>/feeds/{token}.ics</c>).
/// The Api host wires up the implementation with the request's host base. Sync worker
/// uses a static fallback (env var <c>VrBook__PublicBaseUrl</c>).
/// </summary>
public interface IFeedUrlBuilder
{
    string OutboundFeedUrl(string outboundToken);
}
