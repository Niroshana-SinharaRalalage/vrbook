using FluentAssertions;
using Xunit;

namespace VrBook.Architecture.Tests.Infra;

/// <summary>
/// VRB-208 (gaps G3/G4) — dead/mismatched config keys were removed because they had
/// zero code consumers (verified by grep at close-out): the sync poll cadence is a
/// per-feed DB value (CONFIG-INVENTORY §7), not a global app setting, and the Bicep
/// var was even misnamed (<c>Sync__DefaultPollIntervalMin</c> ≠ appsettings
/// <c>Sync:DefaultPollIntervalMinutes</c>, so it could never bind). This guards
/// against the keys creeping back in without a real consumer — if a future story
/// wants sync tuning, it must add a bound + consumed option, not a dead literal.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DeadConfigTests
{
    // Tokens that must not reappear in appsettings.json or main.bicep. Each removed
    // key is listed under both its ':' (appsettings) and '__' (Bicep env) spellings.
    private static readonly string[] RemovedTokens =
    {
        "DefaultPollIntervalMinutes",   // Sync:  appsettings
        "DefaultPollIntervalMin",       // Sync__ Bicep (also covers the G4 misspelling)
        "StaleAlertHours",              // Sync:  / Sync__
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(because: "the test must run from inside the repo to read config files.");
        return dir!.FullName;
    }

    [Fact]
    public void RemovedDeadKeys_AbsentFromAppsettings()
    {
        var appsettings = File.ReadAllText(Path.Combine(RepoRoot(), "src", "VrBook.Api", "appsettings.json"));

        foreach (var token in RemovedTokens)
        {
            appsettings.Should().NotContain(token,
                because: $"'{token}' is dead config removed in VRB-208 — it must not return to appsettings.json without a consumer.");
        }
    }

    [Fact]
    public void RemovedDeadKeys_AbsentFromBicep()
    {
        var bicep = File.ReadAllText(Path.Combine(RepoRoot(), "infra", "main.bicep"));

        foreach (var token in RemovedTokens)
        {
            bicep.Should().NotContain(token,
                because: $"'{token}' is dead config removed in VRB-208 — it must not return to main.bicep without a consumer.");
        }
    }
}
