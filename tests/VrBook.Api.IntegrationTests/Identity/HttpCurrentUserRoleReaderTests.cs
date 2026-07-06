using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using VrBook.Modules.Identity.Infrastructure.Auth;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// Slice OPS.M.15.2 — post-legacy role-reader semantics for
/// <see cref="HttpCurrentUser.HasRole"/>, <see cref="HttpCurrentUser.IsOwner"/>,
/// and <see cref="HttpCurrentUser.IsAdmin"/>.
///
/// <para>Pre-M.15.2, <c>IsOwner</c> also read the pre-ADR-0014
/// <c>extension_isOwner</c> claim via the private <c>ReadBoolClaim</c>
/// helper. Post-M.15.2 the ONLY reader is <c>HasRole</c>, which resolves
/// either <see cref="ClaimTypes.Role"/> or the raw JwtBearer
/// <c>"roles"</c> claim (JwtBearer maps the latter to the former during
/// token validation — the raw path is a belt against future JwtBearer
/// config drift).</para>
///
/// <para>These facts guard against a regression that re-introduces the
/// extension-claim reader — a caller passing <c>extension_isOwner=true</c>
/// on a token whose <c>roles</c> claim is empty MUST NOT satisfy
/// <c>IsOwner</c>.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class HttpCurrentUserRoleReaderTests
{
    private static HttpCurrentUser NewSut(params Claim[] claims)
    {
        var ctx = new DefaultHttpContext();
        if (claims.Length > 0)
        {
            var identity = new ClaimsIdentity(claims, "test");
            ctx.User = new ClaimsPrincipal(identity);
        }
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);
        return new HttpCurrentUser(accessor);
    }

    [Fact]
    public void HasRole_returns_true_for_ClaimTypes_Role_shape()
    {
        var sut = NewSut(new Claim(ClaimTypes.Role, "Owner"));
        sut.HasRole("Owner").Should().BeTrue();
    }

    [Fact]
    public void HasRole_returns_true_for_native_roles_claim_shape()
    {
        // JwtBearer maps the "roles" claim to ClaimTypes.Role during token
        // validation, but the belt-suspenders in HasRole reads both. This
        // guards against a JwtBearer config drift silently breaking every
        // authenticated caller.
        var sut = NewSut(new Claim("roles", "Owner"));
        sut.HasRole("Owner").Should().BeTrue();
    }

    [Fact]
    public void HasRole_is_case_insensitive()
    {
        var sut = NewSut(new Claim(ClaimTypes.Role, "owner"));
        sut.HasRole("Owner").Should().BeTrue();
    }

    [Fact]
    public void HasRole_returns_false_for_anonymous_principal()
    {
        var sut = NewSut();
        sut.HasRole("Owner").Should().BeFalse();
        sut.HasRole("Admin").Should().BeFalse();
        sut.HasRole("PlatformAdmin").Should().BeFalse();
    }

    [Fact]
    public void IsOwner_reflects_HasRole_and_ignores_extension_claim()
    {
        var withRole = NewSut(new Claim(ClaimTypes.Role, "Owner"));
        withRole.IsOwner.Should().BeTrue();
        withRole.IsAdmin.Should().BeFalse();

        // The extension claim MUST NOT satisfy IsOwner post-M.15.2.
        var legacyOnly = NewSut(new Claim("extension_isOwner", "true"));
        legacyOnly.IsOwner.Should().BeFalse(
            because: "M.15.2 dropped the extension_isOwner reader; only ClaimTypes.Role satisfies IsOwner.");
    }

    [Fact]
    public void IsAdmin_reflects_HasRole_and_ignores_extension_claim()
    {
        var withRole = NewSut(new Claim(ClaimTypes.Role, "Admin"));
        withRole.IsAdmin.Should().BeTrue();
        withRole.IsOwner.Should().BeFalse();

        var legacyOnly = NewSut(new Claim("extension_isAdmin", "true"));
        legacyOnly.IsAdmin.Should().BeFalse(
            because: "M.15.2 dropped the extension_isAdmin reader; only ClaimTypes.Role satisfies IsAdmin.");
    }
}
