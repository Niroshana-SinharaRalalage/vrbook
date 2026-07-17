using FluentAssertions;
using VrBook.Application.Common;
using Xunit;

namespace VrBook.Api.IntegrationTests.Configuration;

/// <summary>
/// VRB-211 — the <c>settings.&lt;section&gt;.&lt;verb&gt;</c> audit-action convention that
/// every settings command emits and the "Recent changes" query filters on. No Docker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SettingsAuditActionsTests
{
    [Theory]
    [InlineData("cancellation", "set-model", "settings.cancellation.set-model")]
    [InlineData("platform", "set-tiers", "settings.platform.set-tiers")]
    [InlineData("platform", "set-fee", "settings.platform.set-fee")]
    public void For_BuildsConventionalAction(string section, string verb, string expected)
    {
        SettingsAuditActions.For(section, verb).Should().Be(expected);
        SettingsAuditActions.For(section, verb).Should().StartWith(SettingsAuditActions.Prefix);
    }

    [Fact]
    public void SectionPrefix_SelectsTheSection()
    {
        SettingsAuditActions.SectionPrefix("cancellation").Should().Be("settings.cancellation.");
        SettingsAuditActions.For("cancellation", "set-model")
            .Should().StartWith(SettingsAuditActions.SectionPrefix("cancellation"));
    }

    [Theory]
    [InlineData("", "set")]
    [InlineData("cancellation", "")]
    [InlineData("Cancellation", "set")]      // uppercase
    [InlineData("cancellation", "Set_Model")] // underscore + caps
    [InlineData("has space", "set")]
    [InlineData("trailing-", "set")]
    public void For_RejectsNonKebabTokens(string section, string verb)
    {
        var act = () => SettingsAuditActions.For(section, verb);
        act.Should().Throw<ArgumentException>();
    }
}
