using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VrBook.Modules.Identity.Infrastructure.Auth;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// Slice OPS.M.12.1 — unit tests for
/// <see cref="HttpCurrentUser.IdentityProvider"/> accessor semantics.
///
/// <para>Locks the classification: absent claim → <c>"entra"</c>; tenant-
/// issuer-host claim → <c>"entra"</c>; social host → raw literal preserved;
/// anonymous request (no HttpContext) → null. A regression here would
/// cause the OPS.M.12.2 <c>AdminSocialIdpRejectionMiddleware</c> to
/// silently mis-classify tokens.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class HttpCurrentUserIdpAccessorTests
{
    private static (HttpCurrentUser Sut, DefaultHttpContext Ctx) NewSut(
        string? idpClaim = null,
        string? tenantIssuerHost = null)
    {
        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EntraExternalId:TenantIssuerHost"] = tenantIssuerHost,
            })
            .Build();
        services.AddSingleton<IConfiguration>(cfg);

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (idpClaim is not null)
        {
            var identity = new ClaimsIdentity(new[] { new Claim(HttpCurrentUser.IdpClaim, idpClaim) }, "test");
            ctx.User = new ClaimsPrincipal(identity);
        }

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);
        return (new HttpCurrentUser(accessor), ctx);
    }

    [Fact]
    public void No_idp_claim_normalizes_to_entra()
    {
        var (sut, _) = NewSut();
        sut.IdentityProvider.Should().Be(HttpCurrentUser.ProviderEntraLocal);
    }

    [Fact]
    public void Google_idp_claim_returned_verbatim()
    {
        var (sut, _) = NewSut(idpClaim: "google.com");
        sut.IdentityProvider.Should().Be("google.com",
            because: "the middleware gate compares against SocialIdpValues using the raw issuer host; normalizing to 'google' here would break that comparison.");
    }

    [Fact]
    public void Idp_matching_tenant_issuer_host_normalizes_to_entra()
    {
        var host = "c6ada840-c3a5-42f1-bb13-3594570c2592.ciamlogin.com";
        var (sut, _) = NewSut(idpClaim: host, tenantIssuerHost: host);
        sut.IdentityProvider.Should().Be(HttpCurrentUser.ProviderEntraLocal,
            because: "some Entra tenants emit idp equal to the issuer host on local sign-ins; the accessor must treat that as entra-local, not as a mystery social IdP.");
    }

    [Fact]
    public void Idp_claim_is_case_insensitive_against_tenant_issuer_host()
    {
        var host = "MyTenant.ciamlogin.com";
        var (sut, _) = NewSut(idpClaim: host.ToLowerInvariant(), tenantIssuerHost: host.ToUpperInvariant());
        sut.IdentityProvider.Should().Be(HttpCurrentUser.ProviderEntraLocal);
    }

    [Fact]
    public void Anonymous_request_returns_null()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var sut = new HttpCurrentUser(accessor);
        sut.IdentityProvider.Should().BeNull();
    }

    [Fact]
    public void Unknown_idp_value_passes_through_verbatim()
    {
        var (sut, _) = NewSut(idpClaim: "someunknown.example");
        sut.IdentityProvider.Should().Be("someunknown.example",
            because: "unmapped IdP values must reach IdentityProviderClassifier so a future portal-add is diagnosable in logs, not silently collapsed to entra.");
    }
}
