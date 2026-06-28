using FluentAssertions;
using VrBook.Modules.Sync.Infrastructure.RateLimiting;
using Xunit;

namespace VrBook.Api.IntegrationTests.Sync;

/// <summary>
/// Slice OPS.M.6 §3.3 (D3) — pins the per-host default rate limits against
/// the §4 table. A future operator override via appsettings is fine; a
/// regression on the default ceiling is not. Each value documented inline
/// so a reviewer sees the source-of-evidence without leaving the test.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ChannelPollOptionsDefaultsTests
{
    [Fact]
    public void Defaults_match_OPS_M_6_4_table()
    {
        var opts = new ChannelPollOptions();
        Bucket(opts, "airbnb.com")
            .Should().BeEquivalentTo(new { TokensPerWindow = 60, WindowSeconds = 60, BurstSize = 5 });
        Bucket(opts, "booking.com")
            .Should().BeEquivalentTo(new { TokensPerWindow = 30, WindowSeconds = 60, BurstSize = 3 });
        Bucket(opts, "vrbo.com")
            .Should().BeEquivalentTo(new { TokensPerWindow = 30, WindowSeconds = 60, BurstSize = 3 });
        Bucket(opts, "*")
            .Should().BeEquivalentTo(new { TokensPerWindow = 20, WindowSeconds = 60, BurstSize = 2 });
        opts.MaxWaitSeconds.Should().Be(30,
            because: "OPS.M.6 §3.3 — 30s ceiling so a stalled bucket doesn't hold the worker indefinitely.");
    }

    private static HostPolicy Bucket(ChannelPollOptions opts, string suffix) =>
        opts.Hosts.Single(p => p.HostSuffix == suffix);
}
