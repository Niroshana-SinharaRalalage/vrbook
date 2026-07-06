using System.Reflection;
using FluentAssertions;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Identity.Infrastructure.Auth;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.15.1 — pins the post-M.15.5 shape of
/// <see cref="ICurrentUser"/>: the legacy <c>IsOwner</c> / <c>IsAdmin</c>
/// accessors are REMOVED, and <see cref="HttpCurrentUser.HasRole"/> is
/// the sole role reader.
///
/// <para>Fact 1 is Skip-marked at M.15.1 landing so the RED count stays
/// bounded to the 4 arch failures the plan documents. It is flipped to
/// active in M.15.5's commit. See
/// <c>docs/OPS_M_15_APP_ROLES_CLEANUP_PLAN.md</c> §1.M.15.5.</para>
///
/// <para>Fact 2 is a positive assertion — the shape callers migrate onto.
/// Passes today.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class OpsM15_ICurrentUserRoleShapeTests
{
    [Fact]
    public void ICurrentUser_exposes_no_IsOwner_or_IsAdmin_property()
    {
        var t = typeof(ICurrentUser);
        var flags = BindingFlags.Public | BindingFlags.Instance;
        t.GetProperty("IsOwner", flags).Should().BeNull(
            because: "M.15.5 removes IsOwner from ICurrentUser; callers migrate to HasTenantRole for tenant-scoped checks.");
        t.GetProperty("IsAdmin", flags).Should().BeNull(
            because: "M.15.5 removes IsAdmin from ICurrentUser; the App Roles token drives the [Authorize] gate + HasTenantRole handles tenant-scoped checks.");
    }

    [Fact]
    public void HttpCurrentUser_HasRole_string_returns_bool()
    {
        var m = typeof(HttpCurrentUser).GetMethod("HasRole",
            BindingFlags.Public | BindingFlags.Instance,
            new[] { typeof(string) });
        m.Should().NotBeNull(
            because: "HasRole(string) is the sole role reader post-M.15 — it must stay.");
        m!.ReturnType.Should().Be(typeof(bool));
    }
}
