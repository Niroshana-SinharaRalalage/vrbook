using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure;
using VrBook.Modules.Admin;
using Xunit;

namespace VrBook.Architecture.Tests.Admin;

/// <summary>
/// VRB-203 (gap G13) — guards that the production composition resolves the real
/// DB-backed feature-flag runtime, never the A0 no-op <c>StubFeatureToggle</c>. If a
/// refactor drops <c>AddAdminModule</c>'s <c>Replace</c> (or re-registers the stub),
/// this fails the build.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FeatureToggleCompositionTests
{
    [Fact]
    public void ProdComposition_ResolvesDbFeatureToggle_NotTheStub()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        // The API composition: infra core registers the stub; the Admin module replaces it.
        services.AddInfrastructureCore(configuration);
        services.AddAdminModule(configuration);

        var descriptor = services.Single(d => d.ServiceType == typeof(IFeatureToggle));

        descriptor.ImplementationType?.Name.Should().Be(
            "DbFeatureToggle",
            because: "AddAdminModule must replace StubFeatureToggle with the real DB-backed resolver (G13).");
        descriptor.ImplementationType?.Name.Should().NotBe(
            "StubFeatureToggle",
            because: "the no-op stub must never be the active IFeatureToggle in production.");
    }
}
