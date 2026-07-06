using FluentAssertions;
using VrBook.Modules.Identity.Infrastructure.Auth;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// Slice OPS.M.12.3 — unit tests for
/// <see cref="IdentityProviderClassifier.Classify"/>.
///
/// <para>The classifier's output is what
/// <c>UserProvisioningMiddleware</c> writes to
/// <c>identity.user_identities.provider</c>. Regressions here map to DB
/// CHECK failures at runtime (SQLSTATE 23514), user provisioning
/// breaking silently, OR — worst case — a social IdP silently collapsed
/// to <c>"entra"</c> which would bypass the OPS.M.12.2 middleware
/// gate.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class IdentityProviderClassifierTests
{
    [Fact]
    public void Null_idp_returns_entra()
    {
        IdentityProviderClassifier.Classify(null, entraTenantIssuerHost: "vrbook.ciamlogin.com")
            .Should().Be(HttpCurrentUser.ProviderEntraLocal);
    }

    [Fact]
    public void Whitespace_idp_returns_entra()
    {
        IdentityProviderClassifier.Classify("   ", entraTenantIssuerHost: "vrbook.ciamlogin.com")
            .Should().Be(HttpCurrentUser.ProviderEntraLocal);
    }

    [Fact]
    public void Idp_matching_tenant_issuer_host_returns_entra()
    {
        var host = "c6ada840-c3a5-42f1-bb13-3594570c2592.ciamlogin.com";
        IdentityProviderClassifier.Classify(host, entraTenantIssuerHost: host)
            .Should().Be(HttpCurrentUser.ProviderEntraLocal);
    }

    [Fact]
    public void Idp_google_com_returns_google()
    {
        IdentityProviderClassifier.Classify("google.com", entraTenantIssuerHost: null)
            .Should().Be(HttpCurrentUser.ProviderGoogle);
    }

    [Fact]
    public void Idp_live_com_returns_microsoft()
    {
        IdentityProviderClassifier.Classify("live.com", entraTenantIssuerHost: null)
            .Should().Be(HttpCurrentUser.ProviderMicrosoft);
    }

    [Fact]
    public void Idp_facebook_com_returns_facebook()
    {
        IdentityProviderClassifier.Classify("facebook.com", entraTenantIssuerHost: null)
            .Should().Be(HttpCurrentUser.ProviderFacebook);
    }

    [Fact]
    public void Idp_apple_com_returns_apple()
    {
        IdentityProviderClassifier.Classify("apple.com", entraTenantIssuerHost: null)
            .Should().Be(HttpCurrentUser.ProviderApple);
    }

    [Fact]
    public void Idp_matching_is_case_insensitive_across_the_board()
    {
        IdentityProviderClassifier.Classify("Google.COM", null).Should().Be(HttpCurrentUser.ProviderGoogle);
        IdentityProviderClassifier.Classify("APPLE.com", null).Should().Be(HttpCurrentUser.ProviderApple);
    }

    [Fact]
    public void Unknown_idp_value_passes_through_verbatim()
    {
        IdentityProviderClassifier.Classify("linkedin.com", entraTenantIssuerHost: null)
            .Should().Be("linkedin.com",
                because: "unmapped IdPs must hit the DB CHECK constraint loudly (23514) rather than collapse to entra silently.");
    }

    [Fact]
    public void Empty_issuer_host_config_still_classifies_correctly()
    {
        IdentityProviderClassifier.Classify("google.com", entraTenantIssuerHost: "")
            .Should().Be(HttpCurrentUser.ProviderGoogle);
        IdentityProviderClassifier.Classify(null, entraTenantIssuerHost: "")
            .Should().Be(HttpCurrentUser.ProviderEntraLocal);
    }
}
