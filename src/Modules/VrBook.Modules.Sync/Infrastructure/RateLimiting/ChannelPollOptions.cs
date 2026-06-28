namespace VrBook.Modules.Sync.Infrastructure.RateLimiting;

/// <summary>
/// OPS.M.6 §3.3 (D3) — outbound poll rate-limit configuration. Binds from
/// the <c>ChannelPoll</c> section in appsettings. Defaults track the
/// documented rate ceilings of major iCal providers (see §4 table).
/// </summary>
public sealed class ChannelPollOptions
{
    public const string SectionName = "ChannelPoll";

    /// <summary>
    /// Soft cap on how long <see cref="IRateLimiter.TryAcquireAsync"/> will
    /// wait before returning <c>false</c>. Default = 30 seconds so a
    /// long queue doesn't tie up the Container Apps Job indefinitely.
    /// </summary>
    public int MaxWaitSeconds { get; set; } = 30;

    /// <summary>
    /// Per-host policies. The matcher uses longest suffix match; the wildcard
    /// <c>"*"</c> entry is the catch-all default for hosts not explicitly listed.
    /// </summary>
    public List<HostPolicy> Hosts { get; set; } = new()
    {
        new HostPolicy { HostSuffix = "airbnb.com",      TokensPerWindow = 60, WindowSeconds = 60, BurstSize = 5 },
        new HostPolicy { HostSuffix = "booking.com",     TokensPerWindow = 30, WindowSeconds = 60, BurstSize = 3 },
        new HostPolicy { HostSuffix = "vrbo.com",        TokensPerWindow = 30, WindowSeconds = 60, BurstSize = 3 },
        new HostPolicy { HostSuffix = "homeaway.com",    TokensPerWindow = 30, WindowSeconds = 60, BurstSize = 3 },
        new HostPolicy { HostSuffix = "*",               TokensPerWindow = 20, WindowSeconds = 60, BurstSize = 2 },
    };
}

/// <summary>
/// OPS.M.6 §3.3 (D3) — a single rate-limit policy.
/// </summary>
public sealed class HostPolicy
{
    /// <summary>Host suffix (e.g. <c>"airbnb.com"</c>) or <c>"*"</c> for catch-all.</summary>
    public string HostSuffix { get; set; } = string.Empty;

    /// <summary>Tokens issued per <see cref="WindowSeconds"/> window. = the rate.</summary>
    public int TokensPerWindow { get; set; }

    /// <summary>Window length in seconds. 60 = "per minute".</summary>
    public int WindowSeconds { get; set; }

    /// <summary>Maximum burst capacity (bucket size).</summary>
    public int BurstSize { get; set; }
}
