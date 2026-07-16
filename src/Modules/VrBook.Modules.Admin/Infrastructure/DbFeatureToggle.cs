using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Admin.Application;

namespace VrBook.Modules.Admin.Infrastructure;

/// <summary>
/// VRB-203 (gap G13) — the real <see cref="IFeatureToggle"/> runtime, replacing the
/// no-op <c>StubFeatureToggle</c>. Resolves a flag in priority order:
/// <list type="number">
///   <item>DB override row in <c>admin.feature_flags</c> (set live via the admin
///   toggle API, no redeploy),</item>
///   <item>config value under the <c>Features:&lt;Area&gt;.&lt;Capability&gt;</c> key,</item>
///   <item>the caller's <c>defaultValue</c> (safe default).</item>
/// </list>
///
/// <para>Results are cached in <see cref="IMemoryCache"/> for a short TTL to keep
/// per-request lookups cheap; the admin toggle command invalidates the key on write
/// (see <see cref="FeatureFlagKeys.CacheKey"/>). The cache is <b>per-replica</b>: the
/// <see cref="IFeatureToggle"/> contract's Redis + Service Bus distributed cache-bust
/// is <b>deferred, not dropped</b> (Redis is not deployed — CURRENT-STATE §10); swap
/// the store for distributed invalidation when Redis lands.</para>
///
/// <para>The <c>propertyId</c>/<c>userId</c> arguments are accepted for interface
/// compatibility but not yet used — VRB-203 ships global/platform flags;
/// per-property / per-user overrides are a future extension.</para>
/// </summary>
internal sealed class DbFeatureToggle : IFeatureToggle
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IFeatureFlagStore _store;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;

    public DbFeatureToggle(IFeatureFlagStore store, IConfiguration configuration, IMemoryCache cache)
    {
        _store = store;
        _configuration = configuration;
        _cache = cache;
    }

    public async Task<bool> IsEnabledAsync(
        string key,
        Guid? propertyId = null,
        Guid? userId = null,
        bool defaultValue = false,
        CancellationToken ct = default)
    {
        if (_cache.TryGetValue(FeatureFlagKeys.CacheKey(key), out bool? cached))
        {
            return cached ?? defaultValue;
        }

        // Override wins over config; config wins over the caller default. A null here
        // means "not set anywhere" → fall back to the per-call default.
        var resolved = await _store.GetOverrideAsync(key, ct)
                       ?? _configuration.GetValue<bool?>(key);

        _cache.Set(FeatureFlagKeys.CacheKey(key), resolved, CacheTtl);
        return resolved ?? defaultValue;
    }
}
