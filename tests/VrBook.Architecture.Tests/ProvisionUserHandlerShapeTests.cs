using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.10.2 F11.7.6.5 — locks in the upsert-by-(oid ∪ email)
/// shape of <c>ProvisionUserHandler</c> so a future regressor can't
/// silently return to the oid-only shape that caused the F11.7 walk-3
/// `Cross-tenant write rejected. actual=&lt;null&gt;` panel.
///
/// <para>Source-text scans (no Roslyn dep, matches the pattern in
/// <see cref="OwnerActionTenantResolutionTests"/>):</para>
///
/// <list type="number">
///   <item>Handler references <c>GetActiveByEmailAsync</c> — email
///     fallback branch exists.</item>
///   <item>Handler references <c>ClaimOidForExistingProfile</c> —
///     rebind path exists.</item>
///   <item>Handler contains the string literal <c>"email_already_claimed"</c> —
///     guardrail exists.</item>
///   <item>Handler contains a <c>Guid.TryParse</c> call — Real-Entra oid
///     shape check is the specific mechanism (rejecting the fragile
///     <c>dev-</c> prefix heuristic per F11.7.6 §3).</item>
///   <item><c>IUserRepository</c> declares <c>GetActiveByEmailAsync</c> —
///     signature check on the interface, not just the impl.</item>
/// </list>
/// </summary>
public sealed class ProvisionUserHandlerShapeTests
{
    private const string HandlerRelativePath =
        "src/Modules/VrBook.Modules.Identity/Application/Users/Commands/ProvisionUserHandler.cs";

    private const string RepoInterfaceRelativePath =
        "src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/IUserRepository.cs";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, HandlerRelativePath)))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(
            because: "the test must run from inside the repo so it can read source files.");
        return dir!.FullName;
    }

    private static string ReadHandlerSource() =>
        File.ReadAllText(Path.Combine(RepoRoot(), HandlerRelativePath));

    private static string ReadRepoInterfaceSource() =>
        File.ReadAllText(Path.Combine(RepoRoot(), RepoInterfaceRelativePath));

    [Fact]
    public void Handler_invokes_GetActiveByEmailAsync()
    {
        var src = ReadHandlerSource();
        src.Should().Contain("GetActiveByEmailAsync",
            because: "the email-fallback branch is the load-bearing fix; a regressor removing it returns us to the walk-3 multi-row-per-email bug.");
    }

    [Fact]
    public void Handler_invokes_ClaimOidForExistingProfile()
    {
        var src = ReadHandlerSource();
        src.Should().Contain("ClaimOidForExistingProfile",
            because: "the rebind path is what heals a divergent oid onto the survivor row; removing it forces every fresh oid to provision a new row.");
    }

    [Fact]
    public void Handler_declares_email_already_claimed_guardrail()
    {
        var src = ReadHandlerSource();
        src.Should().Contain("email_already_claimed",
            because: "the guardrail (BusinessRuleViolationException with rule email_already_claimed) is the closure of the role-address collision case; removing it would silently permit privilege inheritance across two humans sharing an email.");
    }

    [Fact]
    public void Handler_uses_Guid_TryParse_for_real_entra_oid_shape_check()
    {
        var src = ReadHandlerSource();
        var m = Regex.Match(src, @"Guid\s*\.\s*TryParse");
        m.Success.Should().BeTrue(
            because: "F11.7.6 §3 explicitly rejected the 'dev-' prefix heuristic (leaks because 'dev-' is not reserved by Entra) in favor of Guid.TryParse. If a regressor swaps back to the prefix check, this test fails and the F11.7.6 doc is the source of truth for why.");
    }

    [Fact]
    public void UserRepository_interface_declares_GetActiveByEmailAsync()
    {
        var src = ReadRepoInterfaceSource();
        src.Should().Contain("GetActiveByEmailAsync",
            because: "the handler-side reference is only useful if the interface exposes the method; a regressor removing only the interface would leave the handler-side call broken at compile time - this arch test catches shape drift before that.");
    }
}
