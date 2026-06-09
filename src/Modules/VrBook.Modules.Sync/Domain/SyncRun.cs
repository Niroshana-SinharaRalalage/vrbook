using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Domain.Common;

namespace VrBook.Modules.Sync.Domain;

/// <summary>
/// Audit row for one execution of the sync worker against one feed. Started before
/// the HTTP call, completed (or failed) when the parse/upsert is done. Three or more
/// consecutive failures triggers <see cref="SyncRunFailed"/> which the on-call alert
/// picks up.
/// </summary>
public sealed class SyncRun : AggregateRoot
{
    public Guid ChannelFeedId { get; private set; }
    public Guid PropertyId { get; private set; }
    public ChannelKind Channel { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public SyncRunStatus Status { get; private set; }
    public int EventsSeen { get; private set; }
    public int EventsNew { get; private set; }
    public int EventsUpdated { get; private set; }
    public int EventsCancelled { get; private set; }
    public string? Error { get; private set; }

    public bool IsTerminal => EndedAt is not null;
    public TimeSpan? Duration => EndedAt is null ? null : EndedAt.Value - StartedAt;

    private SyncRun() { } // EF

    public static SyncRun Start(Guid channelFeedId, Guid propertyId, ChannelKind channel) => new()
    {
        Id = Guid.NewGuid(),
        ChannelFeedId = channelFeedId,
        PropertyId = propertyId,
        Channel = channel,
        StartedAt = DateTimeOffset.UtcNow,
        Status = SyncRunStatus.Partial, // updated to Success / Failed on completion
    };

    public void Complete(int seen, int @new, int updated, int cancelled)
    {
        if (IsTerminal)
        {
            throw new BusinessRuleViolationException(
                "sync.run.already_terminal",
                "SyncRun has already been completed or failed.");
        }
        EventsSeen = seen;
        EventsNew = @new;
        EventsUpdated = updated;
        EventsCancelled = cancelled;
        EndedAt = DateTimeOffset.UtcNow;
        Status = SyncRunStatus.Success;
    }

    public void Fail(string error, int consecutiveFailures)
    {
        if (IsTerminal)
        {
            throw new BusinessRuleViolationException(
                "sync.run.already_terminal",
                "SyncRun has already been completed or failed.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        EndedAt = DateTimeOffset.UtcNow;
        Status = SyncRunStatus.Failed;
        Error = error;
        Raise(new SyncRunFailed(ChannelFeedId, PropertyId, Channel, consecutiveFailures, error));
    }
}
