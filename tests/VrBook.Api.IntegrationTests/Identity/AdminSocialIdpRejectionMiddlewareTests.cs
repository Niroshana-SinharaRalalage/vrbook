using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Infrastructure.Auth;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// Slice OPS.M.12.2 — truth-table unit tests for
/// <see cref="AdminSocialIdpRejectionMiddleware"/>. Locks the predicate
/// (idp ∈ SocialIdpValues AND (IsPlatformAdmin OR MembershipRoles.Count > 0))
/// + the whitelist paths + anonymous short-circuit.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AdminSocialIdpRejectionMiddlewareTests
{
    private static async Task<InvocationResult> RunAsync(
        string? idp,
        bool isPlatformAdmin,
        int membershipCount,
        string path = "/api/v1/admin/bookings",
        bool authenticated = true)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        if (authenticated)
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", "test-oid") }, "test"));
        }

        var current = Substitute.For<ICurrentUser>();
        current.IdentityProvider.Returns(idp);
        current.IsPlatformAdmin.Returns(isPlatformAdmin);
        current.ExternalObjectId.Returns("test-oid");
        var roles = new Dictionary<Guid, IReadOnlySet<string>>();
        for (var i = 0; i < membershipCount; i++)
        {
            roles[Guid.NewGuid()] = new HashSet<string> { "tenant_admin" };
        }
        current.MembershipRoles.Returns((IReadOnlyDictionary<Guid, IReadOnlySet<string>>)roles);

        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var sut = new AdminSocialIdpRejectionMiddleware(next, NullLogger<AdminSocialIdpRejectionMiddleware>.Instance);

        Exception? thrown = null;
        try
        {
            await sut.InvokeAsync(ctx, current);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        return new InvocationResult(nextCalled, thrown);
    }

    [Fact]
    public async Task Anonymous_request_short_circuits_regardless_of_signals()
    {
        var r = await RunAsync(idp: null, isPlatformAdmin: true, membershipCount: 5, authenticated: false);
        r.NextCalled.Should().BeTrue();
        r.Thrown.Should().BeNull();
    }

    [Fact]
    public async Task Entra_local_with_no_admin_authority_passes()
    {
        var r = await RunAsync(idp: HttpCurrentUser.ProviderEntraLocal, isPlatformAdmin: false, membershipCount: 0);
        r.NextCalled.Should().BeTrue();
        r.Thrown.Should().BeNull();
    }

    [Fact]
    public async Task Entra_local_platform_admin_passes()
    {
        var r = await RunAsync(idp: HttpCurrentUser.ProviderEntraLocal, isPlatformAdmin: true, membershipCount: 0);
        r.NextCalled.Should().BeTrue();
        r.Thrown.Should().BeNull(because: "admin authority + entra-local IdP is the standard admin path.");
    }

    [Fact]
    public async Task Entra_local_tenant_admin_passes()
    {
        var r = await RunAsync(idp: HttpCurrentUser.ProviderEntraLocal, isPlatformAdmin: false, membershipCount: 1);
        r.NextCalled.Should().BeTrue();
        r.Thrown.Should().BeNull();
    }

    [Fact]
    public async Task Social_IdP_with_no_admin_authority_passes()
    {
        var r = await RunAsync(idp: "google.com", isPlatformAdmin: false, membershipCount: 0);
        r.NextCalled.Should().BeTrue(because: "normal guest using Google sign-in — gate does not fire.");
        r.Thrown.Should().BeNull();
    }

    [Fact]
    public async Task Social_IdP_platform_admin_rejects()
    {
        var r = await RunAsync(idp: "google.com", isPlatformAdmin: true, membershipCount: 0);
        r.NextCalled.Should().BeFalse();
        r.Thrown.Should().BeOfType<AdminSocialIdpRejectedException>();
        var ex = (AdminSocialIdpRejectedException)r.Thrown!;
        ex.Rule.Should().Be("admin_authority_requires_entra_local");
        ex.IdentityProvider.Should().Be("google.com");
        ex.IsPlatformAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task Social_IdP_tenant_admin_rejects()
    {
        var r = await RunAsync(idp: "google.com", isPlatformAdmin: false, membershipCount: 1);
        r.NextCalled.Should().BeFalse();
        r.Thrown.Should().BeOfType<AdminSocialIdpRejectedException>();
    }

    [Fact]
    public async Task Whitelist_me_path_exempts_the_gate_even_when_rejected()
    {
        var r = await RunAsync(idp: "google.com", isPlatformAdmin: true, membershipCount: 0, path: "/api/v1/me");
        r.NextCalled.Should().BeTrue(because: "SPA needs GET /me to render the error page — whitelist exempts.");
        r.Thrown.Should().BeNull();
    }

    [Fact]
    public async Task Whitelist_me_tenants_path_exempts_the_gate()
    {
        var r = await RunAsync(idp: "google.com", isPlatformAdmin: false, membershipCount: 3, path: "/api/v1/me/tenants");
        r.NextCalled.Should().BeTrue();
        r.Thrown.Should().BeNull();
    }

    private sealed record InvocationResult(bool NextCalled, Exception? Thrown);
}
