using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using VrBook.Api.Common;
using VrBook.Api.Controllers;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.15.1 — bans <see cref="AuthorizeAttribute.Roles"/> values
/// that reference <c>Owner</c> or <c>Admin</c> on any controller in the
/// API assembly. The only allowed role literal is <c>PlatformAdmin</c>
/// (the ADR-0014 shape).
///
/// <para>Facts 1 + 2 flip GREEN in M.15.3 (controller migration).
/// Fact 3 is a positive assertion pinning the current
/// <see cref="TenantsPlatformController"/> shape (mirrors
/// <c>PlatformAdminEndpointRoleGateTests</c> fact 1); passes today.</para>
///
/// <para>Adversarial case: a future PR copy-pastes
/// <c>[Authorize(Roles = "Owner,Admin")]</c> from a pre-M.15 sibling.
/// The class-walker in fact 1 catches it and fails with a specific
/// controller + action name.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class OpsM15_NoOwnerAdminRoleAttributeTests
{
    private const string PlatformAdminRole = "PlatformAdmin";

    private static readonly HashSet<string> AllowedRoleLiterals = new(StringComparer.Ordinal)
    {
        PlatformAdminRole,
    };

    private static IEnumerable<Type> ControllerTypes()
    {
        // Reach the API assembly via a well-known type that lives inside it.
        var apiAssembly = typeof(ExemptFromCrossTenantMatrixAttribute).Assembly;
        return apiAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);
    }

    private static bool HasHttpVerb(MethodInfo m) =>
        m.GetCustomAttributes<HttpMethodAttribute>(inherit: false).Any();

    private static IEnumerable<(Type Controller, MethodInfo? Action, AuthorizeAttribute Attr)> WalkAuthorize()
    {
        foreach (var c in ControllerTypes())
        {
            foreach (var classAttr in c.GetCustomAttributes<AuthorizeAttribute>(inherit: false))
            {
                yield return (c, null, classAttr);
            }
            foreach (var m in c.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName || !HasHttpVerb(m))
                {
                    continue;
                }
                foreach (var methodAttr in m.GetCustomAttributes<AuthorizeAttribute>(inherit: false))
                {
                    yield return (c, m, methodAttr);
                }
            }
        }
    }

    [Fact]
    public void No_controller_attribute_references_Owner_or_Admin_role_literal()
    {
        var offenders = new List<string>();
        foreach (var (controller, action, attr) in WalkAuthorize())
        {
            var roles = attr.Roles;
            if (string.IsNullOrEmpty(roles))
            {
                continue;
            }
            var tokens = roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var t in tokens)
            {
                if (!AllowedRoleLiterals.Contains(t))
                {
                    var target = action is null
                        ? $"{controller.Name} (class-level)"
                        : $"{controller.Name}.{action.Name}";
                    offenders.Add($"{target} carries [Authorize(Roles=\"{roles}\")] — '{t}' is not allowed. " +
                                  "Migrate to [Authorize] + handler-level HasTenantRole per docs/OPS_M_15_APP_ROLES_CLEANUP_PLAN.md §2.3.");
                }
            }
        }
        offenders.Should().BeEmpty();
    }

    [Fact]
    public void Every_controller_action_carries_an_explicit_access_decision_post_M15()
    {
        // Post-M.15.3: every action is EITHER
        //   [Authorize] (no Roles) — the common shape,
        //   [Authorize(Roles="PlatformAdmin")] — cross-tenant operator surface,
        //   [AllowAnonymous] — genuinely public,
        // OR inherits the equivalent from the class level.
        // No action carries an [Authorize(Roles="Owner")] or "Owner,Admin".
        var offenders = new List<string>();

        foreach (var c in ControllerTypes())
        {
            var classHasAnyAccess = c.GetCustomAttribute<AuthorizeAttribute>(inherit: false) is not null
                || c.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false) is not null;
            foreach (var m in c.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName || !HasHttpVerb(m))
                {
                    continue;
                }
                var methodHasAccess = m.GetCustomAttribute<AuthorizeAttribute>(inherit: false) is not null
                    || m.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false) is not null;
                if (!classHasAnyAccess && !methodHasAccess)
                {
                    offenders.Add($"{c.Name}.{m.Name} carries no access decision — add [Authorize] or [AllowAnonymous].");
                }
            }
        }
        offenders.Should().BeEmpty();
    }

    [Fact]
    public void TenantsPlatformController_still_carries_PlatformAdmin_class_level_gate()
    {
        // Mirrors PlatformAdminEndpointRoleGateTests fact 1 — pins the ONE
        // controller in the codebase whose role gate SURVIVES M.15.
        var attr = typeof(TenantsPlatformController)
            .GetCustomAttribute<AuthorizeAttribute>(inherit: false);
        attr.Should().NotBeNull();
        attr!.Roles.Should().Be(PlatformAdminRole,
            because: "TenantsPlatformController is the cross-tenant operator surface; PlatformAdmin is the ONLY role literal that survives M.15.");
    }
}
