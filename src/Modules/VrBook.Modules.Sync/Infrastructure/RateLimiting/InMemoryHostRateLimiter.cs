using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace VrBook.Modules.Sync.Infrastructure.RateLimiting;

/// <summary>
/// OPS.M.6 §3.2 + §3.3 (D2/D3) — in-memory per-host token-bucket rate limit
/// backed by the BCL <see cref="TokenBucketRateLimiter"/>. Buckets are
/// keyed by the resolved <see cref="HostPolicy.HostSuffix"/> so multiple
/// subdomains share state (e.g. <c>www.airbnb.com</c> + <c>de.airbnb.com</c>).
///
/// <para>Single-instance state lives in a <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// — adequate for the single Container Apps Job that owns the worker today.
/// Multi-instance distributed state ships with Slice OPS.M.10.</para>
/// </summary>
public sealed class InMemoryHostRateLimiter : IRateLimiter, IDisposable
{
    private readonly ChannelPollOptions options;
    private readonly ConcurrentDictionary<string, RateLimiter> buckets = new(StringComparer.Ordinal);

    public InMemoryHostRateLimiter(IOptions<ChannelPollOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options.Value;
    }

    public async ValueTask<bool> TryAcquireAsync(string host, CancellationToken ct = default)
    {
        var policy = HostMatcher.Resolve(options.Hosts, host);
        var limiter = buckets.GetOrAdd(policy.HostSuffix, _ => Build(policy));

        var maxWait = TimeSpan.FromSeconds(options.MaxWaitSeconds);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(maxWait);
        try
        {
            using var lease = await limiter.AcquireAsync(permitCount: 1, cts.Token);
            return lease.IsAcquired;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Soft timeout — caller wasn't cancelled, the maxWait clock was.
            return false;
        }
    }

    private static TokenBucketRateLimiter Build(HostPolicy policy) => new(
        new TokenBucketRateLimiterOptions
        {
            TokenLimit = policy.BurstSize,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = Math.Max(policy.BurstSize, 1) * 4,
            ReplenishmentPeriod = TimeSpan.FromSeconds(
                Math.Max(1, policy.WindowSeconds / Math.Max(1, policy.TokensPerWindow))),
            TokensPerPeriod = 1,
            AutoReplenishment = true,
        });

    public void Dispose()
    {
        foreach (var l in buckets.Values)
        {
            l.Dispose();
        }
        buckets.Clear();
    }
}
