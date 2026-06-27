using VrBook.Contracts.Enums;
using VrBook.Domain.Common;

namespace VrBook.Modules.Sync.Domain;

/// <summary>
/// One inbound iCal subscription per property + channel. Created when an owner
/// pastes their AirBnB calendar URL into <c>/admin/sync</c>. Owns the polling
/// state (ETag, ConsecutiveFailures) and the outbound token used to render
/// <c>/feeds/{token}.ics</c>.
/// </summary>
public sealed class ChannelFeed : AggregateRoot
{
    /// <summary>
    /// Tenant the feed belongs to (inherits from the property's tenant).
    /// Per OPS_M_3_PLAN §3.1 — `Guid?` during 3a/3b; flips to `Guid` in 3c.
    /// </summary>
    public Guid? TenantId { get; private set; }

    public Guid PropertyId { get; private set; }
    public ChannelKind Channel { get; private set; }
    public string InboundUrl { get; private set; } = default!;

    /// <summary>Random opaque token embedded in the outbound feed URL — gives owners a
    /// shareable iCal subscription URL without exposing the property id.</summary>
    public string OutboundToken { get; private set; } = default!;

    public int PollIntervalMinutes { get; private set; }
    public bool IsEnabled { get; private set; }

    // Polling state
    public DateTimeOffset? LastSuccessAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public string? LastError { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public string? ETag { get; private set; }
    public DateTimeOffset? LastModifiedAt { get; private set; }

    private ChannelFeed() { } // EF

    public static ChannelFeed Create(
        Guid tenantId,
        Guid propertyId,
        ChannelKind channel,
        string inboundUrl,
        int pollIntervalMinutes = 30)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId required.", nameof(tenantId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(inboundUrl);
        if (pollIntervalMinutes < 5)
        {
            throw new BusinessRuleViolationException(
                "sync.feed.poll_interval",
                "Poll interval must be at least 5 minutes.");
        }
        if (!Uri.TryCreate(inboundUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new BusinessRuleViolationException(
                "sync.feed.url",
                "Inbound URL must be an absolute http(s) URL.");
        }

        return new ChannelFeed
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PropertyId = propertyId,
            Channel = channel,
            InboundUrl = inboundUrl.Trim(),
            OutboundToken = Guid.NewGuid().ToString("N"),
            PollIntervalMinutes = pollIntervalMinutes,
            IsEnabled = true,
        };
    }

    public void UpdateConfig(string inboundUrl, int pollIntervalMinutes, bool isEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inboundUrl);
        if (pollIntervalMinutes < 5)
        {
            throw new BusinessRuleViolationException(
                "sync.feed.poll_interval",
                "Poll interval must be at least 5 minutes.");
        }
        InboundUrl = inboundUrl.Trim();
        PollIntervalMinutes = pollIntervalMinutes;
        IsEnabled = isEnabled;
    }

    public void Pause() => IsEnabled = false;
    public void Resume() => IsEnabled = true;

    public void RecordAttemptStarted(DateTimeOffset now)
    {
        LastAttemptAt = now;
    }

    public void RecordSuccess(DateTimeOffset now, string? etag, DateTimeOffset? lastModified)
    {
        LastSuccessAt = now;
        LastAttemptAt = now;
        ConsecutiveFailures = 0;
        LastError = null;
        ETag = string.IsNullOrWhiteSpace(etag) ? null : etag.Trim();
        LastModifiedAt = lastModified;
    }

    public void RecordFailure(DateTimeOffset now, string error)
    {
        LastAttemptAt = now;
        ConsecutiveFailures++;
        LastError = error;
    }

    /// <summary>
    /// Worker calls this to decide whether a feed should be polled right now. True if
    /// the feed is enabled AND either has never been attempted OR enough time has
    /// elapsed since the last attempt.
    /// </summary>
    public bool IsDueForPoll(DateTimeOffset now)
    {
        if (!IsEnabled)
        {
            return false;
        }
        if (LastAttemptAt is null)
        {
            return true;
        }
        return now >= LastAttemptAt.Value.AddMinutes(PollIntervalMinutes);
    }
}
