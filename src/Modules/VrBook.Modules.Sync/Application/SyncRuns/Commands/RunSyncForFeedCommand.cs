using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Sync.Domain;
using VrBook.Modules.Sync.Infrastructure.Persistence;

namespace VrBook.Modules.Sync.Application.SyncRuns.Commands;

/// <summary>
/// Worker entry point. For one channel feed: dispatch to the matching
/// <see cref="IExternalChannel"/>, upsert the returned reservations, mark
/// reservations that disappeared from the feed as cancelled, and record a
/// <see cref="SyncRun"/> audit row.
///
/// <para>OPS.M.6 §3.1 + Step 2 — the worker stamps <see cref="TenantId"/>
/// from <c>ChannelFeed.TenantId</c>; <c>BackgroundCommandTenantScopeBehavior</c>
/// asserts non-empty and pushes the value into the logging scope.</para>
/// </summary>
public sealed record RunSyncForFeedCommand(Guid ChannelFeedId, Guid TenantId)
    : IRequest<RunSyncForFeedResult>, IBackgroundCommand, ITenantScoped;

public sealed record RunSyncForFeedResult(
    Guid ChannelFeedId,
    SyncRunStatus Status,
    int EventsSeen,
    int EventsNew,
    int EventsUpdated,
    int EventsCancelled,
    string? Error);

internal sealed class RunSyncForFeedHandler(
    SyncDbContext db,
    IEnumerable<IExternalChannel> channels,
    ILogger<RunSyncForFeedHandler> logger)
    : IRequestHandler<RunSyncForFeedCommand, RunSyncForFeedResult>
{
    public async Task<RunSyncForFeedResult> Handle(RunSyncForFeedCommand cmd, CancellationToken cancellationToken)
    {
        var feed = await db.ChannelFeeds.FirstOrDefaultAsync(f => f.Id == cmd.ChannelFeedId, cancellationToken)
            ?? throw new NotFoundException("ChannelFeed", cmd.ChannelFeedId);

        var channel = channels.FirstOrDefault(c => c.Kind == feed.Channel)
            ?? throw new BusinessRuleViolationException(
                "sync.channel.unsupported",
                $"No IExternalChannel registered for {feed.Channel}.");

        // OPS.M.3c — run + reservations inherit tenant from the feed. Wave B
        // backfilled every pre-existing row, so no fallback is needed.
        var run = SyncRun.Start(feed.TenantId, feed.Id, feed.PropertyId, feed.Channel);
        feed.RecordAttemptStarted(DateTimeOffset.UtcNow);
        db.SyncRuns.Add(run);

        var config = new ChannelFeedConfig(feed.Id, feed.PropertyId, feed.InboundUrl, feed.ETag, feed.LastModifiedAt);

        try
        {
            var pulled = await channel.PullAsync(config, cancellationToken);
            var (newCount, updatedCount, cancelledCount) = await ApplyAsync(feed, pulled, cancellationToken);

            run.Complete(pulled.Count, newCount, updatedCount, cancelledCount);
            // We can't read the ETag/Last-Modified back from the IExternalChannel right
            // now (Phase-1 channel returns only the parsed payload). A future revision
            // can return a response envelope; for now we just bump LastSuccessAt.
            feed.RecordSuccess(DateTimeOffset.UtcNow, etag: null, lastModified: null);

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Sync OK for feed {FeedId}: seen={Seen} new={New} updated={Updated} cancelled={Cancelled}",
                feed.Id, pulled.Count, newCount, updatedCount, cancelledCount);

            return new RunSyncForFeedResult(feed.Id, SyncRunStatus.Success, pulled.Count, newCount, updatedCount, cancelledCount, null);
        }
        catch (Exception ex)
        {
            feed.RecordFailure(DateTimeOffset.UtcNow, ex.Message);
            run.Fail(ex.Message, feed.ConsecutiveFailures);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogError(ex,
                "Sync FAILED for feed {FeedId} (consecutive={Failures})",
                feed.Id, feed.ConsecutiveFailures);
            return new RunSyncForFeedResult(feed.Id, SyncRunStatus.Failed, 0, 0, 0, 0, ex.Message);
        }
    }

    /// <summary>
    /// Upserts the pulled reservations and marks anything not in the pull-set as cancelled.
    /// </summary>
    private async Task<(int newCount, int updatedCount, int cancelledCount)> ApplyAsync(
        ChannelFeed feed,
        IReadOnlyList<ExternalReservationDto> pulled,
        CancellationToken ct)
    {
        var existing = await db.ExternalReservations
            .Where(r => r.ChannelFeedId == feed.Id && r.CancelledAt == null)
            .ToListAsync(ct);
        var existingByUid = existing.ToDictionary(r => r.ICalUid);
        var pulledUids = new HashSet<string>(pulled.Select(p => p.ICalUid));

        var newCount = 0;
        var updatedCount = 0;
        var cancelledCount = 0;

        foreach (var dto in pulled)
        {
            if (existingByUid.TryGetValue(dto.ICalUid, out var existingRow))
            {
                // Only update if any field actually changed.
                if (existingRow.Checkin != dto.Checkin
                    || existingRow.Checkout != dto.Checkout
                    || existingRow.Summary != dto.Summary)
                {
                    existingRow.Update(dto.Checkin, dto.Checkout, dto.Summary, dto.RawPayload);
                    updatedCount++;
                }
            }
            else
            {
                var er = ExternalReservation.Import(
                    feed.TenantId,
                    feed.Id, feed.PropertyId, feed.Channel,
                    dto.ICalUid, dto.Checkin, dto.Checkout, dto.Summary, dto.RawPayload);
                db.ExternalReservations.Add(er);
                newCount++;
            }
        }

        // Anything we previously imported that's no longer in the feed → cancelled.
        foreach (var orphan in existing.Where(r => !pulledUids.Contains(r.ICalUid)))
        {
            orphan.MarkCancelled();
            cancelledCount++;
        }

        return (newCount, updatedCount, cancelledCount);
    }
}
