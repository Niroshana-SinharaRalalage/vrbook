using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.10.2 F11.7.6.5 — locks in the shape of the
/// <c>User.ClaimOidForExistingProfile</c> domain method + the
/// <c>UserOidRebound</c> event. Preserves the contract the
/// <see cref="ProvisionUserHandlerShapeTests"/> relies on.
///
/// <para>Source-text scans (no Roslyn):</para>
///
/// <list type="number">
///   <item><c>User.cs</c> declares the <c>ClaimOidForExistingProfile</c>
///     method.</item>
///   <item>Method raises <c>UserOidRebound</c>.</item>
///   <item>Method is idempotent on same oid — an early-return path
///     exists for <c>string.Equals(B2CObjectId, newOid, ...)</c>.</item>
///   <item>Method refuses on soft-deleted state (throws
///     <c>InvalidOperationException</c> on <c>IsDeleted</c>).</item>
///   <item><c>IdentityEvents.cs</c> declares
///     <c>UserOidRebound(Guid UserId, string OldOid, string NewOid)</c>.</item>
/// </list>
/// </summary>
public sealed class UserAggregateRebindShapeTests
{
    private const string UserRelativePath =
        "src/Modules/VrBook.Modules.Identity/Domain/User.cs";

    private const string EventsRelativePath =
        "src/VrBook.Contracts/Events/IdentityEvents.cs";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, UserRelativePath)))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(
            because: "the test must run from inside the repo so it can read source files.");
        return dir!.FullName;
    }

    private static string ReadUserSource() =>
        File.ReadAllText(Path.Combine(RepoRoot(), UserRelativePath));

    private static string ReadEventsSource() =>
        File.ReadAllText(Path.Combine(RepoRoot(), EventsRelativePath));

    [Fact]
    public void User_declares_ClaimOidForExistingProfile_method()
    {
        var src = ReadUserSource();
        var m = Regex.Match(src, @"public\s+void\s+ClaimOidForExistingProfile\s*\(");
        m.Success.Should().BeTrue(
            because: "the rebind path in ProvisionUserHandler depends on this domain method; if the method signature drifts (e.g. renamed, made internal, or a different arity), the handler build breaks and this test fires first with a clearer failure.");
    }

    [Fact]
    public void ClaimOidForExistingProfile_raises_UserOidRebound()
    {
        var src = ReadUserSource();
        src.Should().Contain("Raise(new UserOidRebound",
            because: "the audit trail lives on this event; removing the Raise() call silently drops the audit record for oid rebinds.");
    }

    [Fact]
    public void ClaimOidForExistingProfile_is_idempotent_on_same_oid()
    {
        var src = ReadUserSource();
        var m = Regex.Match(
            src,
            @"if\s*\(\s*string\s*\.\s*Equals\s*\(\s*B2CObjectId\s*,\s*newOid",
            RegexOptions.Singleline);
        m.Success.Should().BeTrue(
            because: "re-provisioning under the same oid must be a no-op to avoid spamming UserOidRebound events (see F11.7.6 doc §F11.7.6.2). A regressor removing this early-return would emit false-positive rebind audits on every login.");
    }

    [Fact]
    public void ClaimOidForExistingProfile_refuses_soft_deleted_state()
    {
        var src = ReadUserSource();
        var m = Regex.Match(
            src,
            @"if\s*\(\s*IsDeleted\s*\)",
            RegexOptions.Singleline);
        m.Success.Should().BeTrue(
            because: "rebinding onto a soft-deleted user row would resurrect an intentionally-retired identity; the handler-side survivor picker already excludes soft-deleted rows via GetActiveByEmailAsync, but this domain-level guard is defense-in-depth.");
    }

    [Fact]
    public void IdentityEvents_declares_UserOidRebound_record()
    {
        var src = ReadEventsSource();
        var m = Regex.Match(
            src,
            @"public\s+sealed\s+record\s+UserOidRebound\s*\(\s*Guid\s+UserId\s*,\s*string\s+OldOid\s*,\s*string\s+NewOid\s*\)",
            RegexOptions.Singleline);
        m.Success.Should().BeTrue(
            because: "the event shape is the contract external audit + downstream consumers rely on; renaming fields silently breaks binding.");
    }
}
