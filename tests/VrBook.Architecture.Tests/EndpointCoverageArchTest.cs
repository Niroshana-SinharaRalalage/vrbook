using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using VrBook.Api.Common;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.10 §4.10 (D10) Step 9 — endpoint-coverage drift guard.
///
/// <para>The full M.10 invariant is: every authenticated controller action
/// either appears in <c>RouteMatrix.GetAll()</c> (the cross-tenant test
/// matrix) or carries
/// <see cref="ExemptFromCrossTenantMatrixAttribute"/>. This test ships the
/// load-bearing half of that invariant: every authenticated action is
/// covered by one of (a) <see cref="AuthorizeAttribute"/>,
/// (b) <see cref="AllowAnonymousAttribute"/>, or
/// (c) <see cref="ExemptFromCrossTenantMatrixAttribute"/> — i.e. the
/// engineer made a deliberate decision about its access shape.</para>
///
/// <para>The second half (matrix-row enumeration) lights up when
/// <c>RouteMatrix</c> ships in Step 2; the attribute already exists so
/// future code can carry it ahead of the matrix.</para>
/// </summary>
public sealed class EndpointCoverageArchTest
{
    [Fact]
    public void Every_controller_action_carries_an_explicit_access_decision()
    {
        // Reach the API assembly via the ExemptFromCrossTenantMatrixAttribute
        // (which lives in VrBook.Api.Common); Program is top-level so it
        // can't be referenced directly.
        var apiAssembly = typeof(ExemptFromCrossTenantMatrixAttribute).Assembly;
        var controllerTypes = apiAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        controllerTypes.Should().NotBeEmpty("the API project must export some controllers.");

        var offenders = new List<string>();

        foreach (var c in controllerTypes)
        {
            // Controller-level exempt covers every action.
            if (c.GetCustomAttribute<ExemptFromCrossTenantMatrixAttribute>(inherit: false) is not null)
            {
                continue;
            }

            var controllerLevelAuth = c.GetCustomAttribute<AuthorizeAttribute>(inherit: false) is not null
                || c.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false) is not null;

            foreach (var m in c.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName)
                {
                    continue;
                }

                var hasHttpVerb = m.GetCustomAttributes<HttpMethodAttribute>(inherit: false).Any();
                if (!hasHttpVerb)
                {
                    continue;
                }

                var methodLevelExempt = m.GetCustomAttribute<ExemptFromCrossTenantMatrixAttribute>(inherit: false) is not null;
                var methodLevelAuth = m.GetCustomAttribute<AuthorizeAttribute>(inherit: false) is not null
                    || m.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false) is not null;

                if (methodLevelExempt)
                {
                    continue;
                }

                if (controllerLevelAuth || methodLevelAuth)
                {
                    continue;
                }

                offenders.Add($"{c.FullName}.{m.Name}");
            }
        }

        offenders.Should().BeEmpty(
            because: "OPS.M.10 §4.10 (D10) — every controller action must declare an explicit access decision: " +
                     "[Authorize] (gated), [AllowAnonymous] (public by design), or [ExemptFromCrossTenantMatrix] " +
                     "(out-of-matrix with a documented reason). A bare action is a silent cross-tenant risk.");
    }

    [Fact]
    public void ExemptFromCrossTenantMatrix_requires_a_non_empty_reason()
    {
        var act = () => new ExemptFromCrossTenantMatrixAttribute("");
        act.Should().Throw<ArgumentException>(
            because: "OPS.M.10 §4.10 — the reason string is the documentation; empty strings would defeat audit.");
    }

    [Fact]
    public void ExemptFromCrossTenantMatrix_applies_to_class_or_method()
    {
        var t = typeof(ExemptFromCrossTenantMatrixAttribute);
        var usage = t.GetCustomAttribute<AttributeUsageAttribute>()!;
        usage.ValidOn.Should().HaveFlag(AttributeTargets.Class);
        usage.ValidOn.Should().HaveFlag(AttributeTargets.Method);
        usage.AllowMultiple.Should().BeFalse(
            because: "one exemption per target is sufficient; multiples would imply contradictory reasons.");
    }
}
