using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Application.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Sync.Application.Common;
using VrBook.Modules.Sync.Infrastructure.Persistence;

namespace VrBook.Modules.Sync.Application.ChannelFeeds.Commands;

/// <summary>
/// VRB-214 — set an inbound channel feed's poll cadence from the availability settings
/// surface. Validated to a sane range (15–1440 min) — the existing domain guard only
/// enforced ≥ 5. Tenant-scoped + audited (<c>settings.availability.set-cadence</c>).
/// </summary>
public sealed record SetFeedCadenceCommand(Guid FeedId, int PollIntervalMinutes, Guid TenantId)
    : IRequest<ChannelFeedDto>, ITenantScoped, IAuditable
{
    public string AuditAction => SettingsAuditActions.For("availability", "set-cadence");
    public string? AuditTargetType => "ChannelFeed";
    public string? AuditTargetId => FeedId.ToString();
}

internal sealed class SetFeedCadenceValidator : AbstractValidator<SetFeedCadenceCommand>
{
    // VRB-214 AC — poll cadence must be a sane inbound-poll range.
    public const int MinMinutes = 15;
    public const int MaxMinutes = 1440;

    public SetFeedCadenceValidator()
    {
        RuleFor(x => x.PollIntervalMinutes)
            .InclusiveBetween(MinMinutes, MaxMinutes)
            .WithMessage($"Poll interval must be between {MinMinutes} and {MaxMinutes} minutes.");
        RuleFor(x => x.FeedId).NotEmpty();
    }
}

internal sealed class SetFeedCadenceHandler(SyncDbContext db, IFeedUrlBuilder urls, ICurrentUser currentUser)
    : IRequestHandler<SetFeedCadenceCommand, ChannelFeedDto>
{
    public async Task<ChannelFeedDto> Handle(SetFeedCadenceCommand cmd, CancellationToken cancellationToken)
    {
        ChannelFeedAuthorization.RequireTenantAdmin(currentUser, cmd.TenantId);

        var feed = await db.ChannelFeeds.FirstOrDefaultAsync(f => f.Id == cmd.FeedId, cancellationToken)
            ?? throw new NotFoundException("ChannelFeed", cmd.FeedId);

        // Reuse the aggregate's config update — cadence only; keep url + enabled as-is.
        feed.UpdateConfig(feed.InboundUrl, cmd.PollIntervalMinutes, feed.IsEnabled);
        await db.SaveChangesAsync(cancellationToken);

        return feed.ToDto(urls.OutboundFeedUrl(feed.OutboundToken), propertyTitle: string.Empty);
    }
}
