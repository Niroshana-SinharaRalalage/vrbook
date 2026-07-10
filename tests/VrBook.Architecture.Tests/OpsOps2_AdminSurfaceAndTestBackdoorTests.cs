using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using VrBook.Api.Common;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.2.7 — cross-surface guards that the Playwright E2E work never
/// loosened the owner-locked admin auth posture (ADR-0016/0017) to make authed
/// specs easier. Two facts:
///   (c) No <see cref="AllowAnonymousAttribute"/> on any admin-routed controller
///       (class or action). The E2E suite drives admins through REAL Entra
///       sign-in; a fake-auth backdoor on the admin surface is forbidden.
///   (d) The production API <c>Program.cs</c> registers no test-only auth
///       middleware/handler (e.g. the integration-test <c>TestAuthHandler</c> /
///       <c>TwoTenantDevAuthHandler</c>, or the retired DevAuth).
/// </summary>
public sealed class OpsOps2_AdminSurfaceAndTestBackdoorTests
{
    private static readonly Assembly ApiAssembly = typeof(ExemptFromCrossTenantMatrixAttribute).Assembly;

    [Fact]
    public void No_admin_routed_controller_allows_anonymous()
    {
        var controllers = ApiAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(IsAdminRouted)
            .ToList();

        controllers.Should().NotBeEmpty("the admin surface must expose some controllers to guard.");

        var offenders = new List<string>();
        foreach (var c in controllers)
        {
            if (c.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false) is not null)
            {
                offenders.Add($"{c.Name} (class-level [AllowAnonymous])");
            }

            foreach (var m in c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (m.GetCustomAttributes<HttpMethodAttribute>(inherit: false).Any()
                    && m.GetCustomAttribute<AllowAnonymousAttribute>(inherit: false) is not null)
                {
                    offenders.Add($"{c.Name}.{m.Name} ([AllowAnonymous])");
                }
            }
        }

        offenders.Should().BeEmpty(
            "the admin surface is Entra-local only (ADR-0016) — no [AllowAnonymous] backdoor. Offenders: {0}",
            string.Join(", ", offenders));
    }

    [Fact]
    public void Production_Program_registers_no_test_only_auth_middleware()
    {
        var programPath = FindRepoFile(Path.Combine("src", "VrBook.Api", "Program.cs"));
        File.Exists(programPath).Should().BeTrue($"expected to locate the API Program.cs at {programPath}");

        var source = File.ReadAllText(programPath);
        var forbidden = new[]
        {
            "TestAuthHandler",          // VrBook.Api.IntegrationTests test double
            "TwoTenantDevAuthHandler",  // OPS.M.10 cross-tenant test handler
            "DevAuthHandler",           // retired (OPS.M.14) — must not resurrect
            "E2eBackdoor",
            "e2e-backdoor",
        };

        var hits = forbidden.Where(f => source.Contains(f, StringComparison.Ordinal)).ToList();
        hits.Should().BeEmpty(
            "production Program.cs must not register test-only auth middleware. Found: {0}",
            string.Join(", ", hits));
    }

    private static bool IsAdminRouted(Type controller)
    {
        var routes = controller.GetCustomAttributes<RouteAttribute>(inherit: false)
            .Select(r => r.Template ?? string.Empty);
        return routes.Any(r => r.Contains("api/v1/admin", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return relativePath;
    }
}
