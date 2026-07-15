using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using VrBook.Modules.Admin.Application;
using VrBook.Modules.Admin.Infrastructure;
using Xunit;

namespace VrBook.Api.IntegrationTests.Admin;

/// <summary>
/// VRB-203 — unit coverage for the <see cref="DbFeatureToggle"/> resolution order:
/// DB override → config → caller default. No Docker (fake store, in-memory cache).
/// </summary>
[Trait("Category", "Unit")]
public sealed class FeatureToggleResolutionTests
{
    private sealed class FakeStore : IFeatureFlagStore
    {
        private readonly bool? _override;
        public FakeStore(bool? overrideValue) => _override = overrideValue;
        public Task<bool?> GetOverrideAsync(string key, CancellationToken ct = default) => Task.FromResult(_override);
        public Task<IReadOnlyList<FeatureFlagOverride>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult((IReadOnlyList<FeatureFlagOverride>)Array.Empty<FeatureFlagOverride>());
    }

    private static DbFeatureToggle Toggle(bool? overrideValue, params (string Key, string Value)[] config)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config.ToDictionary(c => c.Key, c => (string?)c.Value))
            .Build();
        return new DbFeatureToggle(new FakeStore(overrideValue), configuration, new MemoryCache(new MemoryCacheOptions()));
    }

    [Fact]
    public async Task ConfigFlag_ResolvesFromConfig_WhenNoOverride()
    {
        var toggle = Toggle(overrideValue: null, ("Features:Loyalty.Enabled", "true"));

        var enabled = await toggle.IsEnabledAsync("Features:Loyalty.Enabled", defaultValue: false);

        enabled.Should().BeTrue();
    }

    [Fact]
    public async Task DbOverride_WinsOverConfig()
    {
        var toggle = Toggle(overrideValue: false, ("Features:Loyalty.Enabled", "true"));

        var enabled = await toggle.IsEnabledAsync("Features:Loyalty.Enabled", defaultValue: true);

        enabled.Should().BeFalse(because: "the admin.feature_flags override must win over the config value.");
    }

    [Fact]
    public async Task UnknownFlag_ReturnsCallerDefault_SafeWhenUnset()
    {
        var toggle = Toggle(overrideValue: null); // no override, no config

        (await toggle.IsEnabledAsync("Features:Booking.InstantBook", defaultValue: false)).Should().BeFalse();
        (await toggle.IsEnabledAsync("Features:Loyalty.Enabled", defaultValue: true)).Should().BeTrue();
    }
}
