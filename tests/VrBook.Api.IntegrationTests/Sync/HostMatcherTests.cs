using FluentAssertions;
using VrBook.Modules.Sync.Infrastructure.RateLimiting;
using Xunit;

namespace VrBook.Api.IntegrationTests.Sync;

/// <summary>
/// Slice OPS.M.6 §3.2 (D2) — pins the host-policy resolver. Pure function;
/// safe to assert behaviorally without time involvement.
/// </summary>
[Trait("Category", "Unit")]
public sealed class HostMatcherTests
{
    private static readonly List<HostPolicy> Policies = new()
    {
        new HostPolicy { HostSuffix = "airbnb.com",  TokensPerWindow = 60, WindowSeconds = 60, BurstSize = 5 },
        new HostPolicy { HostSuffix = "booking.com", TokensPerWindow = 30, WindowSeconds = 60, BurstSize = 3 },
        new HostPolicy { HostSuffix = "*",           TokensPerWindow = 20, WindowSeconds = 60, BurstSize = 2 },
    };

    [Theory]
    [InlineData("airbnb.com", "airbnb.com")]
    [InlineData("www.airbnb.com", "airbnb.com")]
    [InlineData("de.airbnb.com", "airbnb.com")]
    [InlineData("booking.com", "booking.com")]
    [InlineData("admin.booking.com", "booking.com")]
    public void Routes_subdomains_to_their_parent_policy(string host, string expectedSuffix)
    {
        HostMatcher.Resolve(Policies, host).HostSuffix.Should().Be(expectedSuffix);
    }

    [Fact]
    public void Unknown_host_falls_to_catch_all_wildcard()
    {
        HostMatcher.Resolve(Policies, "random-personal-server.tld").HostSuffix.Should().Be("*");
    }

    [Fact]
    public void Empty_host_throws()
    {
        var act = () => HostMatcher.Resolve(Policies, string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void No_wildcard_no_match_throws()
    {
        var noWildcard = Policies.Where(p => p.HostSuffix != "*").ToList();
        var act = () => HostMatcher.Resolve(noWildcard, "unknown.tld");
        act.Should().Throw<InvalidOperationException>();
    }
}
