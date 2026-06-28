namespace VrBook.Modules.Sync.Infrastructure.RateLimiting;

/// <summary>
/// OPS.M.6 §3.2 — longest-suffix host policy resolver. Pure function so it
/// can be unit-tested without the limiter.
/// </summary>
public static class HostMatcher
{
    /// <summary>
    /// Returns the <see cref="HostPolicy"/> whose <see cref="HostPolicy.HostSuffix"/>
    /// is the longest match for <paramref name="host"/>. Falls back to the
    /// wildcard <c>"*"</c> policy if no specific suffix matches; throws if
    /// no wildcard policy is configured.
    /// </summary>
    public static HostPolicy Resolve(IReadOnlyList<HostPolicy> policies, string host)
    {
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var lower = host.ToLowerInvariant();
        HostPolicy? best = null;
        var bestLength = -1;
        HostPolicy? wildcard = null;
        foreach (var p in policies)
        {
            if (p.HostSuffix == "*")
            {
                wildcard = p;
                continue;
            }
            var suffix = p.HostSuffix.ToLowerInvariant();
            var matches = lower == suffix || lower.EndsWith("." + suffix, StringComparison.Ordinal);
            if (matches && suffix.Length > bestLength)
            {
                best = p;
                bestLength = suffix.Length;
            }
        }
        return best ?? wildcard
            ?? throw new InvalidOperationException(
                $"No HostPolicy matches '{host}' and no wildcard policy is configured.");
    }
}
