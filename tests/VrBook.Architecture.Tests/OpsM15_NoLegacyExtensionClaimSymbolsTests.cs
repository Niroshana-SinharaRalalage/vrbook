using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using VrBook.Modules.Identity.Infrastructure.Auth;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.15.1 — bans the pre-ADR-0014 legacy extension-claim
/// symbols from <see cref="HttpCurrentUser"/> and its readers.
///
/// <para>These facts are INTENTIONALLY RED at M.15.1's commit. Each is
/// flipped GREEN by a subsequent sub-commit:
///   fact 1 + fact 2 + fact 4 → M.15.2 (drop the constants + ReadBoolClaim).
///   fact 3 → M.15.5 (last symbol reference is TestAuthHandler's emission).
///   fact 5 → passes today (positive assertion; guards that
///            UserProvisioningMiddleware only synthesizes PlatformAdmin).
/// </para>
///
/// <para>See <c>docs/OPS_M_15_APP_ROLES_CLEANUP_PLAN.md</c> §1.M.15.1 for
/// the RED-count expectation on this commit.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class OpsM15_NoLegacyExtensionClaimSymbolsTests
{
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

    private static string HttpCurrentUserSourcePath() => Path.Combine(
        RepoRoot(),
        "src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/HttpCurrentUser.cs");

    [Fact]
    public void HttpCurrentUser_source_contains_no_extension_isOwner_or_extension_isAdmin_literal()
    {
        var text = File.ReadAllText(HttpCurrentUserSourcePath());
        text.Should().NotContain("\"extension_isOwner\"",
            because: "M.15.2 drops the OwnerClaim constant — the string literal must not survive.");
        text.Should().NotContain("\"extension_isAdmin\"",
            because: "M.15.2 drops the AdminClaim constant — the string literal must not survive.");
    }

    [Fact]
    public void HttpCurrentUser_type_has_no_OwnerClaim_or_AdminClaim_member()
    {
        var t = typeof(HttpCurrentUser);
        var flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance;
        t.GetField("OwnerClaim", flags).Should().BeNull(
            because: "M.15.2 removes the legacy constant; downstream code should reference roles via ClaimTypes.Role.");
        t.GetField("AdminClaim", flags).Should().BeNull(
            because: "M.15.2 removes the legacy constant.");
    }

    [Fact]
    public void No_src_file_references_OwnerClaim_or_AdminClaim_identifier()
    {
        var srcDir = Path.Combine(RepoRoot(), "src");
        var files = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
                !f.Contains(Path.Combine("Migrations", ""), StringComparison.OrdinalIgnoreCase) ||
                !(f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
                  f.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Match the identifiers as standalone tokens; avoid matching e.g.
        // "PlatformAdminClaim" or a substring inside another word.
        var ownerRe = new Regex(@"\bOwnerClaim\b");
        var adminRe = new Regex(@"\bAdminClaim\b");

        var offenders = new List<string>();
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            if (ownerRe.IsMatch(text) || adminRe.IsMatch(text))
            {
                offenders.Add(f);
            }
        }

        offenders.Should().BeEmpty(
            because: "M.15.2 removes the two constants; no src/**/*.cs file should reference them by name.");
    }

    [Fact]
    public void HttpCurrentUser_source_contains_no_ReadBoolClaim_method()
    {
        var text = File.ReadAllText(HttpCurrentUserSourcePath());
        text.Should().NotContain("ReadBoolClaim",
            because: "M.15.2 drops the private helper along with the constants. Any remaining reader belongs to the App Roles path (HasRole).");
    }

    [Fact]
    public void UserProvisioningMiddleware_synthesizes_only_the_PlatformAdmin_role_claim()
    {
        var path = Path.Combine(RepoRoot(),
            "src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/UserProvisioningMiddleware.cs");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);

        // Count occurrences of any pattern that constructs a Role Claim.
        // The middleware should have EXACTLY ONE, referencing PlatformAdminRole.
        var addRoleClaimRe = new Regex(@"new\s+Claim\s*\(\s*ClaimTypes\.Role");
        var matches = addRoleClaimRe.Matches(text);
        matches.Count.Should().Be(1,
            because: "the middleware should synthesize ONE role claim (PlatformAdmin). Adding Owner/Admin synthesis here would re-introduce the legacy shape ADR-0014 explicitly rejected.");

        text.Should().Contain("HttpCurrentUser.PlatformAdminRole",
            because: "the single role claim must reference the PlatformAdminRole constant — single source of truth.");
    }
}
