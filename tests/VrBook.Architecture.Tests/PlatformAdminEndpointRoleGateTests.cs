using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using VrBook.Api.Controllers;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.8 §7 + §8 Step 12 — load-bearing arch test that enforces the
/// PlatformAdmin role gate on the cross-tenant operator surface.
///
/// <para>The bypass introduced by §3.3 (D3) means a PlatformAdmin can write
/// to any tenant. The role gate is the ONLY thing standing between a regular
/// authenticated user and that bypass. A future PR that adds a new
/// PlatformAdmin endpoint without the role attribute would silently expose
/// cross-tenant writes; this test fails loudly when that happens.</para>
/// </summary>
public sealed class PlatformAdminEndpointRoleGateTests
{
    private const string PlatformControllerRoute = "api/v1/admin/platform/tenants";
    private const string PlatformAdminRole = "PlatformAdmin";

    [Fact]
    public void TenantsPlatformController_carries_class_level_PlatformAdmin_role_attribute()
    {
        var attr = typeof(TenantsPlatformController)
            .GetCustomAttribute<AuthorizeAttribute>(inherit: false);
        attr.Should().NotBeNull(
            "OPS.M.8 §7 — [Authorize] on the controller is the primary gate.");
        attr!.Roles.Should().Be(
            PlatformAdminRole,
            because: "Roles must be exactly 'PlatformAdmin' — adding 'Owner,Admin' would re-expose the bypass.");
    }

    [Fact]
    public void TenantsPlatformController_route_is_api_v1_admin_platform_tenants()
    {
        var attr = typeof(TenantsPlatformController)
            .GetCustomAttribute<RouteAttribute>(inherit: false);
        attr.Should().NotBeNull();
        attr!.Template.Should().Be(PlatformControllerRoute,
            "the route prefix is the contract surface OPS.M.10's isolation test pack sweeps.");
    }

    [Fact]
    public void Every_PlatformController_action_is_HTTP_verb_attributed()
    {
        var verbed = typeof(TenantsPlatformController).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();
        verbed.Should().NotBeEmpty();
        foreach (var m in verbed)
        {
            var hasVerb = m.GetCustomAttributes()
                .Any(a => a is HttpMethodAttribute);
            hasVerb.Should().BeTrue(
                $"{m.Name} on TenantsPlatformController must be HTTP-verb attributed.");
        }
    }

    [Fact]
    public void No_PlatformController_action_overrides_with_a_more_permissive_role()
    {
        // A future contributor may attach a method-level [Authorize(Roles="Owner")]
        // and accidentally widen the gate. Pin the absence of method-level overrides.
        var actions = typeof(TenantsPlatformController).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);
        foreach (var m in actions)
        {
            var attrs = m.GetCustomAttributes<AuthorizeAttribute>(inherit: false).ToList();
            attrs.Should().BeEmpty(
                $"{m.Name}: method-level [Authorize] would override the controller-level " +
                "PlatformAdmin gate — keep the gate at the class level.");
        }
    }

    [Fact]
    public void No_AllowAnonymous_override_on_PlatformController_actions()
    {
        var actions = typeof(TenantsPlatformController).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);
        foreach (var m in actions)
        {
            m.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false)
                .Should().BeNull($"{m.Name} must NOT have [AllowAnonymous].");
        }
    }
}
