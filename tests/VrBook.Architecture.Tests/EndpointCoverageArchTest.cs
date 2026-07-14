using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using VrBook.Api.Common;
using VrBook.Api.IntegrationTests.Multitenancy;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.10 §4.10 (D10) + VRB-300 — the endpoint-coverage gate.
///
/// <para>The full invariant is two halves:</para>
/// <list type="number">
///   <item><b>Access-decision half (OPS.M.10):</b> every controller action
///   carries an explicit access decision —
///   <see cref="AuthorizeAttribute"/> (gated),
///   <see cref="AllowAnonymousAttribute"/> (public by design), or
///   <see cref="ExemptFromCrossTenantMatrixAttribute"/> (out-of-matrix with a
///   documented reason). Enforced by
///   <see cref="Every_controller_action_carries_an_explicit_access_decision"/>.</item>
///   <item><b>Matrix-membership half (VRB-300):</b> every <em>authenticated</em>
///   controller action <b>either appears in <c>RouteMatrix.GetAll()</c></b>
///   (the cross-tenant test matrix, matched on verb + route template) <b>or
///   carries <see cref="ExemptFromCrossTenantMatrixAttribute"/></b>. Enforced by
///   <see cref="Every_authenticated_action_appears_in_the_route_matrix_or_is_exempt"/>.
///   The build fails naming any action that is neither — a new endpoint without
///   a matrix row (and, per ENGINEERING-RULES §3, its contract tests) cannot go
///   green.</item>
/// </list>
///
/// <para>An action is <em>authenticated</em> — and therefore in scope for the
/// matrix half — when it is behind <c>[Authorize]</c> (at controller or method
/// level) and NOT <c>[AllowAnonymous]</c>. Public anonymous endpoints are out of
/// scope for the cross-tenant matrix (they have no tenant to isolate); the
/// access-decision half already guarantees they carry their explicit
/// <c>[AllowAnonymous]</c>.</para>
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

    /// <summary>
    /// VRB-300 — the real endpoint-coverage gate: every authenticated
    /// controller action must be accounted for by the cross-tenant matrix.
    /// Matches an action to a <see cref="RouteMatrix.Cell"/> on
    /// (HTTP verb, route template with route-parameter names/constraints
    /// normalised away). An action that is neither in the matrix nor exempt
    /// is a silent coverage gap — the build fails naming it, so a new endpoint
    /// cannot merge without either a matrix row or a documented exemption.
    /// </summary>
    [Fact]
    public void Every_authenticated_action_appears_in_the_route_matrix_or_is_exempt()
    {
        var matrixKeys = BuildMatrixKeys();
        matrixKeys.Should().NotBeEmpty("RouteMatrix.GetAll() must enumerate endpoint×persona rows.");

        var offenders = UncoveredAuthenticatedActions(matrixKeys);

        offenders.Should().BeEmpty(
            because: "VRB-300 — every authenticated controller action must either appear in RouteMatrix.GetAll() " +
                     "(add a Cell in tests/VrBook.Api.IntegrationTests/Multitenancy/RouteMatrix.cs) or carry " +
                     "[ExemptFromCrossTenantMatrix(\"reason\")]. The following authenticated actions are covered by " +
                     $"neither ({offenders.Count}):\n  {string.Join("\n  ", offenders)}\n");
    }

    /// <summary>
    /// VRB-300 — proves the gate <em>bites</em>: removing a matrix row for a
    /// non-exempt authenticated action turns the coverage gate red, naming that
    /// action. Runs the same <see cref="UncoveredAuthenticatedActions"/> logic
    /// against the full matrix key set minus one known row, so the enforcement
    /// is itself under test (rather than relying on a manual delete-and-observe).
    /// </summary>
    [Fact]
    public void Coverage_gate_bites_when_a_matrix_row_is_removed()
    {
        var full = BuildMatrixKeys();
        UncoveredAuthenticatedActions(full).Should().BeEmpty(
            "sanity check: with the full matrix every authenticated action is covered.");

        // GET /api/v1/me (IdentityController.Get) is an authenticated, non-exempt
        // action whose only coverage is its matrix row. Drop that row.
        const string identityGetKey = "GET /api/v1/me";
        var reduced = new HashSet<string>(full, StringComparer.Ordinal);
        reduced.Remove(identityGetKey).Should().BeTrue(
            "the canonical key for IdentityController.Get must be present to be removed.");

        var offenders = UncoveredAuthenticatedActions(reduced);

        offenders.Should().Contain(
            o => o.Contains("IdentityController.Get", StringComparison.Ordinal),
            because: "removing a non-exempt authenticated action's only matrix row must flip the gate red, " +
                     "naming that action — otherwise the gate is decorative.");
    }

    /// <summary>
    /// The heart of the coverage gate: given the set of (verb, normalised route)
    /// keys the matrix covers, returns every authenticated controller action not
    /// accounted for by either a matrix row or an
    /// <see cref="ExemptFromCrossTenantMatrixAttribute"/>. Extracted so the gate
    /// and the "gate bites" proof exercise identical logic.
    /// </summary>
    internal static List<string> UncoveredAuthenticatedActions(ISet<string> matrixKeys)
    {
        var apiAssembly = typeof(ExemptFromCrossTenantMatrixAttribute).Assembly;
        var controllerTypes = apiAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        controllerTypes.Should().NotBeEmpty("the API project must export some controllers.");

        var offenders = new List<string>();

        foreach (var c in controllerTypes)
        {
            // A controller-level exemption covers every one of its actions.
            if (c.GetCustomAttribute<ExemptFromCrossTenantMatrixAttribute>(inherit: false) is not null)
            {
                continue;
            }

            var controllerRoute = c.GetCustomAttribute<RouteAttribute>(inherit: true)?.Template;
            var controllerAuthorize = c.GetCustomAttribute<AuthorizeAttribute>(inherit: false) is not null;
            var controllerAllowAnon = c.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false) is not null;

            foreach (var m in c.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName)
                {
                    continue;
                }

                var httpAttrs = m.GetCustomAttributes<HttpMethodAttribute>(inherit: false).ToList();
                if (httpAttrs.Count == 0)
                {
                    continue;
                }

                // A method-level exemption removes the action from the matrix
                // requirement, with a documented reason.
                if (m.GetCustomAttribute<ExemptFromCrossTenantMatrixAttribute>(inherit: false) is not null)
                {
                    continue;
                }

                var methodAuthorize = m.GetCustomAttribute<AuthorizeAttribute>(inherit: false) is not null;
                var methodAllowAnon = m.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false) is not null;

                // In scope only when authenticated: behind [Authorize] (at
                // controller or method level) and not [AllowAnonymous]. Public
                // anonymous endpoints have no tenant to isolate.
                var isAuthenticated =
                    (controllerAuthorize || methodAuthorize) && !(controllerAllowAnon || methodAllowAnon);
                if (!isAuthenticated)
                {
                    continue;
                }

                foreach (var http in httpAttrs)
                {
                    foreach (var verb in http.HttpMethods)
                    {
                        var key = CanonicalKey(verb, CombineTemplates(controllerRoute, http.Template));
                        if (!matrixKeys.Contains(key))
                        {
                            offenders.Add($"{key}   ({c.Name}.{m.Name})");
                        }
                    }
                }
            }
        }

        offenders.Sort(StringComparer.Ordinal);
        return offenders;
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

    // ---- matching helpers -------------------------------------------------

    /// <summary>
    /// The set of (verb, normalised-route) keys the matrix enumerates. Reads
    /// <c>RouteMatrix.GetAll()</c> — pure data, no Testcontainer — so this runs
    /// in the blocking, Docker-free arch-test step.
    /// </summary>
    private static HashSet<string> BuildMatrixKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in RouteMatrix.GetAll())
        {
            var cell = (RouteMatrix.Cell)row[0];
            keys.Add(CanonicalKey(cell.Verb, cell.Route));
        }
        return keys;
    }

    /// <summary>
    /// Combine a controller-level route template with a method-level one the
    /// same way attribute routing does for the templates this codebase uses
    /// (no absolute <c>/</c>- or <c>~/</c>-prefixed method templates).
    /// </summary>
    private static string CombineTemplates(string? controllerTemplate, string? methodTemplate)
    {
        var prefix = (controllerTemplate ?? string.Empty).Trim('/');
        var suffix = (methodTemplate ?? string.Empty).Trim('/');
        if (suffix.Length == 0)
        {
            return "/" + prefix;
        }
        if (prefix.Length == 0)
        {
            return "/" + suffix;
        }
        return "/" + prefix + "/" + suffix;
    }

    private static readonly Regex RouteParam = new(@"\{[^}]+\}", RegexOptions.Compiled);

    /// <summary>
    /// Canonical key = <c>VERB /normalised/route</c>. Route parameters (with any
    /// constraint, e.g. <c>{id:guid}</c> or <c>{tenantId}</c>) collapse to a
    /// single <c>{}</c> token so an action's route template and a matrix cell's
    /// route match on structure + verb rather than on parameter names.
    /// </summary>
    private static string CanonicalKey(string verb, string route)
    {
        var path = route.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }
        path = RouteParam.Replace(path, "{}").TrimEnd('/');
        if (path.Length == 0)
        {
            path = "/";
        }
        return verb.ToUpperInvariant() + " " + path.ToLowerInvariant();
    }
}
