using MediatR;
using Microsoft.Extensions.Configuration;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Admin.Application.FeatureFlags.Queries;

/// <summary>
/// VRB-203 — list all feature flags with their effective value (PlatformAdmin only):
/// the union of the known config-backed flags and any DB override rows. Effective value
/// = DB override → config → built-in default.
/// </summary>
public sealed record ListFeatureFlagsQuery : IRequest<IReadOnlyList<FeatureToggleDto>>;

internal sealed class ListFeatureFlagsHandler
    : IRequestHandler<ListFeatureFlagsQuery, IReadOnlyList<FeatureToggleDto>>
{
    private readonly IFeatureFlagStore _store;
    private readonly IConfiguration _configuration;

    public ListFeatureFlagsHandler(IFeatureFlagStore store, IConfiguration configuration)
    {
        _store = store;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<FeatureToggleDto>> Handle(
        ListFeatureFlagsQuery request, CancellationToken cancellationToken)
    {
        var overrides = (await _store.ListAsync(cancellationToken))
            .ToDictionary(o => o.Key, o => o.Enabled, StringComparer.Ordinal);

        var keys = new HashSet<string>(FeatureFlagKeys.KnownDefaults.Keys, StringComparer.Ordinal);
        foreach (var key in overrides.Keys)
        {
            keys.Add(key);
        }

        return keys
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(key => new FeatureToggleDto(key, "global", null, Effective(key, overrides)))
            .ToList();
    }

    private bool Effective(string key, Dictionary<string, bool> overrides)
    {
        if (overrides.TryGetValue(key, out var overridden))
        {
            return overridden;
        }
        var configured = _configuration.GetValue<bool?>(key);
        return configured
            ?? (FeatureFlagKeys.KnownDefaults.TryGetValue(key, out var d) && d);
    }
}
