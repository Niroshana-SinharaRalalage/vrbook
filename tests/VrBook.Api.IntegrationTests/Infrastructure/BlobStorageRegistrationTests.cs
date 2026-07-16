using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure;
using Xunit;

namespace VrBook.Api.IntegrationTests.Infrastructure;

/// <summary>
/// VRB-101 regression guard: the image handlers always depend on
/// <see cref="IBlobStorage"/>, so it MUST resolve even when no blob backend is
/// configured — otherwise the DI container's build-time validation fails and
/// takes down every app-host-building integration fixture. This runs without
/// Docker (Category=Unit) so the regression is caught before CI.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BlobStorageRegistrationTests
{
    [Fact]
    public void AddInfrastructureCore_registers_IBlobStorage_with_no_blob_config()
    {
        var services = new ServiceCollection();
        services.AddInfrastructureCore(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        provider.GetService<IBlobStorage>().Should().NotBeNull(
            "image handlers depend on IBlobStorage, so a fallback must be registered even with no backend configured");
    }
}
