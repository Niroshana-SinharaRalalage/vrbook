using FluentAssertions;
using Xunit;

namespace VrBook.Architecture.Tests.Settings;

/// <summary>
/// VRB-215/216 (design §1b/§5) — RED-then-GREEN guard for retiring the legacy
/// <c>CancellationPolicyCode {Flexible,Moderate,Strict}</c> in favour of
/// <c>CancellationModel {Tiered,RefundableUpgrade}</c>. Phase A adds the new enum +
/// contract; the two legacy consumers below are the documented migration debt that
/// VRB-215 (per-property model) + VRB-102 (refund engine) drive to zero. The test
/// fails if a NEW consumer of the legacy enum appears, and the allowlist shrinks to
/// empty as each migration lands — at which point <c>CancellationPolicyCode</c> is deleted.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CancellationModelMigrationTests
{
    // Files still allowed to reference the legacy enum. Drive this to EMPTY via
    // VRB-215 (Booking domain snapshot) + VRB-102 (DTO), then delete the enum.
    private static readonly string[] KnownLegacyConsumers =
    {
        Path.Combine("src", "Modules", "VrBook.Modules.Booking", "Domain", "Booking.cs"),
        Path.Combine("src", "VrBook.Contracts", "Dtos", "Booking.cs"),
    };

    // Enum-definition files legitimately name the legacy type (defn + the doc comment on
    // its replacement); they are not "consumers". Everything else must be migration debt.
    private static readonly string[] EnumDefinitionFiles =
    {
        "src/VrBook.Contracts/Enums/CancellationPolicyCode.cs",
        "src/VrBook.Contracts/Enums/CancellationModel.cs",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(because: "the test must run from inside the repo.");
        return dir!.FullName;
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void NewCancellationModelEnum_Exists()
    {
        Enum.GetNames<VrBook.Contracts.Enums.CancellationModel>()
            .Should().BeEquivalentTo(new[] { "Tiered", "RefundableUpgrade" },
                because: "the launch model set replaces the legacy Flexible/Moderate/Strict codes.");
    }

    [Fact]
    public void LegacyCancellationPolicyCode_HasNoNewConsumers()
    {
        var root = RepoRoot();
        var exempt = KnownLegacyConsumers
            .Concat(EnumDefinitionFiles.Select(p => p.Replace('/', Path.DirectorySeparatorChar)))
            .Select(p => Path.GetFullPath(Path.Combine(root, p)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var offenders = SourceFiles(root)
            .Where(f => !exempt.Contains(Path.GetFullPath(f)))
            .Where(f => File.ReadAllText(f).Contains("CancellationPolicyCode", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(root, f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            because: "the legacy CancellationPolicyCode is being retired (VRB-215/102) — do not add new " +
                     "consumers; use CancellationModel + the §3 cancellation contract instead. New offenders: " +
                     string.Join(", ", offenders));
    }
}
