using System.Reflection;
using FluentAssertions;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Common;
using VrBook.Modules.Identity.Infrastructure.Auth;
using Xunit;

namespace VrBook.Architecture.Tests;

/// <summary>
/// Slice OPS.M.12.1 — locks the shape of the new
/// <see cref="ICurrentUser.IdentityProvider"/> accessor + the closed sets on
/// <see cref="HttpCurrentUser"/>. Guards against a future refactor
/// accidentally dropping a member the middleware or provisioning handler
/// depends on.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OpsM12_IdentityProviderAccessorShapeTests
{
    [Fact]
    public void ICurrentUser_exposes_IdentityProvider_string()
    {
        var prop = typeof(ICurrentUser).GetProperty(
            nameof(ICurrentUser.IdentityProvider),
            BindingFlags.Public | BindingFlags.Instance);
        prop.Should().NotBeNull(
            because: "the middleware admin-vs-social gate + provisioning classifier both read this accessor.");
        prop!.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void HttpCurrentUser_defines_IdpClaim_constant()
    {
        var field = typeof(HttpCurrentUser).GetField(
            nameof(HttpCurrentUser.IdpClaim),
            BindingFlags.Public | BindingFlags.Static);
        field.Should().NotBeNull();
        field!.GetRawConstantValue().Should().Be(
            "idp",
            because: "this is Entra External ID's canonical claim name for the source-of-authentication IdP.");
    }

    [Fact]
    public void HttpCurrentUser_SocialIdpValues_contains_all_four_M12_providers_plus_deferred_ones()
    {
        var set = HttpCurrentUser.SocialIdpValues;
        // The four IdPs shipping in M.12.
        set.Should().Contain("google.com");
        set.Should().Contain("live.com");
        set.Should().Contain("facebook.com");
        set.Should().Contain("apple.com");
        // Deferred but pre-listed so the gate closes on them too if
        // ever enabled at the tenant level.
        set.Should().Contain("linkedin.com");
    }

    [Fact]
    public void HttpCurrentUser_SocialIdpValues_is_case_insensitive()
    {
        HttpCurrentUser.SocialIdpValues.Contains("Google.COM").Should().BeTrue(
            because: "Entra's idp claim casing varies across providers; the set must handle both without callers second-guessing.");
    }

    [Fact]
    public void HttpCurrentUser_SocialProviderKeys_matches_the_DB_CHECK_constraint_set()
    {
        var keys = HttpCurrentUser.SocialProviderKeys;
        keys.Should().Contain("google");
        keys.Should().Contain("microsoft");
        keys.Should().Contain("facebook");
        keys.Should().Contain("apple");
        // The Entra-local canonical must NOT appear here — it's the
        // non-social baseline.
        keys.Should().NotContain(HttpCurrentUser.ProviderEntraLocal);
    }

    [Fact]
    public void AnonymousCurrentUser_IdentityProvider_is_null()
    {
        var anon = new AnonymousCurrentUser();
        anon.IdentityProvider.Should().BeNull(
            because: "background workers and unauthenticated contexts have no IdP; the anon stub must not fabricate one.");
    }
}
