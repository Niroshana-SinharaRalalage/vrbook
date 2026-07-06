using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.15.4 — locks the load-bearing handler-level role check
/// that replaces the M.15.3-dropped controller-level
/// <c>[Authorize(Roles = "Owner,Admin")]</c> on booking transitions.
///
/// <para>Pre-M.15.3, the controller gate rejected any authenticated
/// caller lacking the Owner or Admin App Role. Post-M.15.3 the gate is
/// plain <c>[Authorize]</c>; without this handler-level check, any
/// authenticated same-tenant user (guest with a booking there,
/// tenant_member if the role ever ships) would silently reach the
/// transition path. This arch test pins the check in source so a future
/// refactor can't quietly drop it.</para>
///
/// <para>See <c>docs/OPS_M_15_APP_ROLES_CLEANUP_PLAN.md</c> §2.3
/// three-layer defence.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class OpsM15_OwnerActionHandlerRoleGateTests
{
    private const string HandlerRelativePath =
        "src/Modules/VrBook.Modules.Booking/Application/Commands/TransitionHandlers.cs";

    private static string ReadHandlerSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, HandlerRelativePath)))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(
            because: "the test must run from inside the repo so it can read the handler source.");
        return File.ReadAllText(Path.Combine(dir!.FullName, HandlerRelativePath));
    }

    [Fact]
    public void OwnerActionHandler_TransitionAsync_checks_HasTenantRole()
    {
        var text = ReadHandlerSource();
        text.Should().Contain("HasTenantRole(booking.TenantId, \"tenant_admin\")",
            because: "post-M.15.3 the controller-level Owner/Admin role gate is gone; " +
                     "the handler MUST fence off same-tenant non-admin callers via HasTenantRole. " +
                     "A regressor that drops this check silently exposes booking transitions " +
                     "to any authenticated same-tenant user.");
    }

    [Fact]
    public void OwnerActionHandler_TransitionAsync_throws_ForbiddenException_on_role_miss()
    {
        var text = ReadHandlerSource();
        var re = new Regex(
            @"HasTenantRole\(booking\.TenantId,\s*""tenant_admin""\).*?ForbiddenException",
            RegexOptions.Singleline);
        re.IsMatch(text).Should().BeTrue(
            because: "the role-miss must throw ForbiddenException so the API maps to 403 " +
                     "via the standard RFC 7807 pipeline. Any other exception type would leak " +
                     "as 500 and break the SPA's admin/error.tsx branch.");
    }

    [Fact]
    public void OwnerActionHandler_role_string_is_the_tenant_admin_literal()
    {
        // Owner-locked §5-Q2 (docs/OPS_M_15_APP_ROLES_CLEANUP_PLAN.md §7):
        // the role string on the handler check is "tenant_admin" matching
        // identity.tenant_memberships. Not "Owner", not "Admin", not any
        // future distinct-owner role token.
        var text = ReadHandlerSource();
        text.Should().Contain("\"tenant_admin\"",
            because: "the role literal must match the M.13.6 MembershipRoles shape " +
                     "(identity.tenant_memberships.role). Introducing a new role token " +
                     "here requires an ADR-0014 amendment.");
    }
}
