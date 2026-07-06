using System.Reflection;
using FluentAssertions;
using VrBook.Contracts.Common;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Infrastructure.Auth;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.12.2 — locks the shape of the admin-vs-social rejection
/// pipeline: exception type + inheritance, ProblemTypes constant,
/// middleware existence, and load-bearing source substrings that a future
/// refactor could silently drop.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OpsM12_AdminSocialIdpRejectionShapeTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(
            because: "the test must run from inside the repo so it can read source files.");
        return dir!.FullName;
    }

    [Fact]
    public void AdminSocialIdpRejectionMiddleware_type_exists_in_Identity_assembly()
    {
        var t = typeof(AdminSocialIdpRejectionMiddleware);
        t.Should().NotBeNull();
        t.Assembly.GetName().Name.Should().Contain("Identity");
    }

    [Fact]
    public void AdminSocialIdpRejectedException_inherits_from_ForbiddenException()
    {
        typeof(AdminSocialIdpRejectedException).IsSubclassOf(typeof(ForbiddenException))
            .Should().BeTrue(
                because: "the exception falls through the same 403 path as CrossTenantAccessException; " +
                          "if inheritance is broken, ProblemDetails maps to 500.");
    }

    [Fact]
    public void ProblemTypes_AdminSocialIdpRejected_matches_documented_URI()
    {
        ProblemTypes.AdminSocialIdpRejected
            .Should().Be($"{ProblemTypes.Base}/admin-social-idp-rejected",
                because: "SPA companion (M.12.7) switches on the problem type; changing it silently breaks the error-page routing.");
    }

    [Fact]
    public void ProblemDetailsConfig_source_registers_the_specific_mapper()
    {
        var path = Path.Combine(RepoRoot(), "src/VrBook.Api/Middleware/ProblemDetailsConfig.cs");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        text.Should().Contain(
            "AdminSocialIdpRejectedException",
            because: "the specific mapper must be registered so the response body carries the rule + identityProvider extensions.");
        text.Should().Contain(
            "ProblemTypes.AdminSocialIdpRejected",
            because: "the problem type must reference the ProblemTypes constant (single source of truth for the URI).");

        // The specific mapper must be registered BEFORE the generic
        // ForbiddenException mapper — Hellang matches first.
        var specificIdx = text.IndexOf("AdminSocialIdpRejectedException", StringComparison.Ordinal);
        var genericIdx = text.IndexOf("opts.Map<ForbiddenException>", StringComparison.Ordinal);
        specificIdx.Should().BeLessThan(genericIdx,
            because: "Hellang picks first-match; if the generic ForbiddenException mapper is registered first, it swallows the specific one.");
    }

    [Fact]
    public void Middleware_source_contains_load_bearing_check_substrings()
    {
        var path = Path.Combine(RepoRoot(),
            "src/Modules/VrBook.Modules.Identity/Infrastructure/Auth/AdminSocialIdpRejectionMiddleware.cs");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        text.Should().Contain("IdentityProvider",
            because: "the IdP-claim check must survive future refactors.");
        text.Should().Contain("SocialIdpValues",
            because: "the closed-set social IdP check must survive future refactors.");
        text.Should().Contain("IsPlatformAdmin",
            because: "the PA authority check must survive.");
        text.Should().Contain("MembershipRoles",
            because: "the tenant-admin authority check must survive.");
    }

    [Fact]
    public void Middleware_is_wired_via_UseIdentityModule_immediately_after_UserProvisioning()
    {
        var path = Path.Combine(RepoRoot(),
            "src/Modules/VrBook.Modules.Identity/IdentityModule.cs");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        var provIdx = text.IndexOf("UseMiddleware<UserProvisioningMiddleware>", StringComparison.Ordinal);
        var gateIdx = text.IndexOf("UseMiddleware<AdminSocialIdpRejectionMiddleware>", StringComparison.Ordinal);
        provIdx.Should().BeGreaterOrEqualTo(0);
        gateIdx.Should().BeGreaterOrEqualTo(0);
        gateIdx.Should().BeGreaterThan(provIdx,
            because: "the gate reads IsPlatformAdmin + MembershipRoles stamped by the provisioning middleware; if the gate runs first, all state is null and the check silently passes.");
    }
}
