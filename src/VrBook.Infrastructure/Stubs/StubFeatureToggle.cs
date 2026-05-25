using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Stubs;

/// <summary>
/// A0 stub. Returns the default value. Replaced by the Redis-backed resolver in A9
/// (Notifications module, which owns the feature_toggles table — see proposal §11.4).
/// </summary>
public sealed class StubFeatureToggle : IFeatureToggle
{
    public Task<bool> IsEnabledAsync(
        string key, Guid? propertyId = null, Guid? userId = null,
        bool defaultValue = false, CancellationToken ct = default)
        => Task.FromResult(defaultValue);
}
