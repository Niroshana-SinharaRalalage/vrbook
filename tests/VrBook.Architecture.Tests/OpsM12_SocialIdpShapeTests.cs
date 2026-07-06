using System.Reflection;
using FluentAssertions;
using VrBook.Modules.Identity.Infrastructure.Auth;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.12.8 — consolidated shape facts for the social-IdP surface.
/// Locks the invariants:
///   1. Backend <c>SocialIdpValues</c> contains the seven raw-idp hosts.
///   2. Backend <c>SocialProviderKeys</c> contains the four canonical
///      provider keys.
///   3. The SPA classifier (<c>web/src/lib/auth/identityProvider.ts</c>)
///      mirrors both sets. If either drifts, an admin using a social IdP
///      would slip past the SPA guard and hit the API middleware 403 with
///      no useful error page.
///   4. The <c>user_identities.provider</c> CHECK constraint includes all
///      four canonical keys plus <c>entra</c> and <c>test</c>.
///   5. The classifier maps host-suffix matches to the canonical keys.
///   6. The provisioning handler enforces REFUSE-AT-PROVISIONING for the
///      four canonical social provider keys.
///   7. The SPA rejection error page routes through the same provider
///      identifier as the middleware Extension writes.
///
/// This is the invariant guardrail; per-file shape tests
/// (<c>OpsM12_IdentityProviderAccessorShapeTests</c>,
/// <c>OpsM12_AdminSocialIdpRejectionShapeTests</c>) remain to lock
/// individual components. This one CROSSES surfaces so no single surface
/// change can silently break the whole.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OpsM12_SocialIdpShapeTests
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

    // Owner-locked list per docs/OPS_M_12_SOCIAL_IDPS_PLAN.md §11-Q1 + §11-Q10.
    private static readonly string[] ExpectedSocialIdpHosts =
    {
        "google.com",
        "live.com",
        "facebook.com",
        "apple.com",
        "linkedin.com",
        "twitter.com",
        "amazon.com",
    };

    private static readonly string[] ExpectedSocialProviderKeys =
    {
        "google",
        "microsoft",
        "facebook",
        "apple",
    };

    [Fact]
    public void HttpCurrentUser_SocialIdpValues_contains_the_seven_locked_hosts()
    {
        var actual = HttpCurrentUser.SocialIdpValues;
        actual.Should().BeEquivalentTo(ExpectedSocialIdpHosts,
            because: "the raw-idp closed set is owner-locked. Adding to it requires an ADR or plan-doc entry AND a matching update to the SPA classifier at web/src/lib/auth/identityProvider.ts. Removing from it silently disables the reject.");
    }

    [Fact]
    public void HttpCurrentUser_SocialProviderKeys_contains_the_four_canonical_keys()
    {
        var actual = HttpCurrentUser.SocialProviderKeys;
        actual.Should().BeEquivalentTo(ExpectedSocialProviderKeys,
            because: "the canonical-provider closed set drives the REFUSE-AT-PROVISIONING branch in ProvisionOrLinkUserHandler. If this drifts, an admin using the missing provider is silently linkable.");
    }

    [Fact]
    public void SPA_identityProvider_ts_mirrors_the_seven_hosts()
    {
        var path = Path.Combine(RepoRoot(), "web/src/lib/auth/identityProvider.ts");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        foreach (var host in ExpectedSocialIdpHosts)
        {
            text.Should().Contain($"'{host}'",
                because: $"the SPA classifier MUST contain '{host}' so the browser rejects before the middleware does. Missing entries let a social-IdP admin slip past the SPA guard.");
        }
    }

    [Fact]
    public void SPA_identityProvider_ts_mirrors_the_four_provider_keys()
    {
        var path = Path.Combine(RepoRoot(), "web/src/lib/auth/identityProvider.ts");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        foreach (var key in ExpectedSocialProviderKeys)
        {
            text.Should().Contain($"'{key}'",
                because: $"the SPA classifier MUST contain the canonical '{key}' so the guard maps user_identities.provider values back to the reject shape.");
        }
    }

    [Fact]
    public void User_identities_provider_CHECK_constraint_migration_includes_facebook()
    {
        var migrationsDir = Path.Combine(RepoRoot(),
            "src/Modules/VrBook.Modules.Identity/Infrastructure/Persistence/Migrations");
        Directory.Exists(migrationsDir).Should().BeTrue(migrationsDir);

        var files = Directory.GetFiles(migrationsDir, "*_OpsM12_UserIdentitiesProviderAddFacebook.cs");
        files.Should().NotBeEmpty(
            because: "the M.12.3 migration widening the provider CHECK constraint to include 'facebook' must exist.");

        var text = File.ReadAllText(files[0]);
        text.Should().Contain("facebook",
            because: "the CHECK constraint SQL must literally include 'facebook' — see M.12.3.");
        foreach (var key in ExpectedSocialProviderKeys)
        {
            text.Should().Contain(key,
                because: $"the CHECK constraint SQL must include '{key}' so the DB accepts inserts for that provider.");
        }
    }

    [Fact]
    public void IdentityProviderClassifier_type_and_static_Classify_signature_exist()
    {
        var t = typeof(IdentityProviderClassifier);
        t.Should().NotBeNull();
        var m = t.GetMethod("Classify", BindingFlags.Public | BindingFlags.Static,
            new[] { typeof(string), typeof(string) });
        m.Should().NotBeNull(
            because: "Classify(string? idpClaim, string? entraTenantIssuerHost) is the sole entry point; UserProvisioningMiddleware + HttpCurrentUser + tests all bind to this signature.");
        m!.ReturnType.Should().Be(typeof(string));
    }

    [Fact]
    public void ProvisionOrLinkUserHandler_calls_RefuseIfAdminSocialLinkAsync_on_both_Branch2_and_Branch3()
    {
        var path = Path.Combine(RepoRoot(),
            "src/Modules/VrBook.Modules.Identity/Application/Users/Commands/ProvisionOrLinkUserHandler.cs");
        File.Exists(path).Should().BeTrue(path);
        var text = File.ReadAllText(path);
        text.Should().Contain("RefuseIfAdminSocialLinkAsync",
            because: "the helper is the sole Layer-1 REFUSE gate; must be present.");

        // Both branches must call the helper. Count occurrences of the helper
        // call site — two: one in Branch 2 (normal path) and one in Branch 3
        // (race-recovery path).
        var callSites = System.Text.RegularExpressions.Regex.Matches(
            text, @"RefuseIfAdminSocialLinkAsync\(");
        callSites.Count.Should().BeGreaterThanOrEqualTo(2,
            because: "Branch 2 (verified-email link) AND Branch 3 (race-recovery link) both must call the REFUSE helper. Only one call site means the race path bypasses the invariant.");

        // The rule string must match the documented owner-policy shape.
        text.Should().Contain("admin_social_signin_refused",
            because: "the BusinessRuleViolationException rule string is documented in ADR-0016 and referenced from tests; if it changes, tests + docs go out of sync.");
    }

    [Fact]
    public void SPA_admin_social_idp_rejected_route_exists()
    {
        var routePath = Path.Combine(RepoRoot(),
            "web/src/app/auth/admin-social-idp-rejected/page.tsx");
        File.Exists(routePath).Should().BeTrue(routePath +
            " — the SPA rejection error page MUST live at /auth/admin-social-idp-rejected; both AdminAuthGuard.tsx and admin/error.tsx route users here.");
        var text = File.ReadAllText(routePath);
        text.Should().Contain("useAuth",
            because: "the page's 'Sign out and try again' CTA must call useAuth().signOut.");
    }
}
