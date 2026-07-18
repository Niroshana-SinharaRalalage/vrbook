using FluentAssertions;
using VrBook.Modules.Admin.Application.Settings;
using Xunit;

namespace VrBook.Api.IntegrationTests.Configuration;

/// <summary>
/// VRB-216 — validation rules for the platform-settings commands (tiers monotonic,
/// pct/bps ranges, state-code roster). No Docker.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SettingsCommandValidatorTests
{
    [Fact]
    public void Tiers_Valid_Passes()
    {
        var r = new SetGlobalTiersValidator().Validate(new SetGlobalTiersCommand(7, 2, 50, 48, 8));
        r.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(2, 7, 50, 48, 8)]    // First <= Second
    [InlineData(7, 2, 150, 48, 8)]   // middle pct > 100
    [InlineData(7, 2, 50, 0, 8)]     // final cutoff not > 0
    [InlineData(7, 2, 50, 48, 120)]  // upgrade pct > 100
    [InlineData(7, -1, 50, 48, 8)]   // negative second tier
    public void Tiers_Invalid_Fails(int first, int second, int mid, int cutoff, int upgrade)
    {
        var r = new SetGlobalTiersValidator().Validate(new SetGlobalTiersCommand(first, second, mid, cutoff, upgrade));
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Tax_ValidRoster_Passes()
    {
        var cmd = new SetTaxPostureCommand(true, new Dictionary<string, bool> { ["CA"] = true, ["NY"] = false });
        new SetTaxPostureValidator().Validate(cmd).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("California")] // not a 2-letter code
    [InlineData("ca")]          // lowercase
    [InlineData("C")]           // too short
    public void Tax_BadStateCode_Fails(string key)
    {
        var cmd = new SetTaxPostureCommand(true, new Dictionary<string, bool> { [key] = true });
        new SetTaxPostureValidator().Validate(cmd).IsValid.Should().BeFalse();
    }
}
