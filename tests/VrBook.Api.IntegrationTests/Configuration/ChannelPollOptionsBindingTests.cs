using FluentAssertions;
using Microsoft.Extensions.Configuration;
using VrBook.Modules.Sync.Infrastructure.RateLimiting;
using Xunit;

namespace VrBook.Api.IntegrationTests.Configuration;

/// <summary>
/// VRB-214 (G28) — the outbound rate-limit config is bound from the <c>ChannelPoll</c>
/// section (now explicit in appsettings, per-env-overridable) + fail-fast validated.
/// No Docker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ChannelPollOptionsBindingTests
{
    [Fact]
    public void Binds_from_ChannelPoll_section()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ChannelPoll:MaxWaitSeconds"] = "45",
                ["ChannelPoll:Hosts:0:HostSuffix"] = "example.com",
                ["ChannelPoll:Hosts:0:TokensPerWindow"] = "12",
                ["ChannelPoll:Hosts:0:WindowSeconds"] = "60",
                ["ChannelPoll:Hosts:0:BurstSize"] = "4",
            })
            .Build();

        var options = new ChannelPollOptions { Hosts = new() };
        config.GetSection(ChannelPollOptions.SectionName).Bind(options);

        options.MaxWaitSeconds.Should().Be(45);
        options.Hosts.Should().ContainSingle(h =>
            h.HostSuffix == "example.com" && h.TokensPerWindow == 12 && h.BurstSize == 4);
    }

    [Fact]
    public void Defaults_pass_validation()
    {
        new ChannelPollOptionsValidator()
            .Validate(null, new ChannelPollOptions())
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void NonPositive_MaxWait_fails_validation()
    {
        var result = new ChannelPollOptionsValidator()
            .Validate(null, new ChannelPollOptions { MaxWaitSeconds = 0 });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("MaxWaitSeconds");
    }

    [Fact]
    public void EmptyHosts_fails_validation()
    {
        var result = new ChannelPollOptionsValidator()
            .Validate(null, new ChannelPollOptions { Hosts = new() });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Hosts");
    }

    [Fact]
    public void InvalidHostPolicy_fails_validation()
    {
        var result = new ChannelPollOptionsValidator().Validate(null, new ChannelPollOptions
        {
            Hosts = new() { new HostPolicy { HostSuffix = "bad.com", TokensPerWindow = 0, WindowSeconds = 60, BurstSize = 1 } },
        });

        result.Failed.Should().BeTrue();
    }
}
