using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using VrBook.Modules.Identity.Infrastructure.Auth;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// Slice OPS.M.14.1 — unit tests for <see cref="HttpCurrentUser.HasTenantRole"/>.
///
/// <para>Replaces the integration coverage previously in
/// <c>TenantClaimWiringTests</c> (deleted in M.14.1). That file exercised
/// <c>HasTenantRole</c> semantics via the <c>/api/v1/dev-auth/current-tenant</c>
/// diagnostic endpoint, which is being retired with the rest of the DevAuth
/// surface in M.14.2. The DB→membership wiring is covered end-to-end by
/// <c>GetMyTenantsHandlerTests</c> + the M.13.5 <c>ProvisionOrLinkUser</c>
/// integration pack; this file locks the pure <c>HasTenantRole</c> logic
/// against a mocked <c>IHttpContextAccessor</c> so a middleware regression
/// that stops populating <c>Items[MembershipRolesItemKey]</c> is caught
/// without a testcontainer.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class HasTenantRoleUnitTests
{
    private static (HttpCurrentUser Sut, DefaultHttpContext Ctx) NewSut(
        IReadOnlyDictionary<Guid, IReadOnlySet<string>>? membershipRoles = null,
        Guid? activeTenantId = null)
    {
        var ctx = new DefaultHttpContext();
        if (membershipRoles is not null)
        {
            ctx.Items[HttpCurrentUser.MembershipRolesItemKey] = membershipRoles;
        }
        if (activeTenantId is Guid tid)
        {
            ctx.Items[HttpCurrentUser.ActiveTenantItemKey] = tid;
        }
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);
        return (new HttpCurrentUser(accessor), ctx);
    }

    [Fact]
    public void HasTenantRole_returns_true_when_membership_dict_carries_the_pair()
    {
        var tid = Guid.NewGuid();
        var (sut, _) = NewSut(new Dictionary<Guid, IReadOnlySet<string>>
        {
            [tid] = new HashSet<string> { "tenant_admin" },
        });
        sut.HasTenantRole(tid, "tenant_admin").Should().BeTrue();
    }

    [Fact]
    public void HasTenantRole_returns_false_when_tenant_absent_from_dict()
    {
        var actual = Guid.NewGuid();
        var challenged = Guid.NewGuid();
        var (sut, _) = NewSut(new Dictionary<Guid, IReadOnlySet<string>>
        {
            [actual] = new HashSet<string> { "tenant_admin" },
        });
        sut.HasTenantRole(challenged, "tenant_admin").Should().BeFalse(
            because: "membership in tenant A must not satisfy a role check against tenant B — Ev-A cross-tenant claim leak is closed by this exact code path.");
    }

    [Fact]
    public void HasTenantRole_returns_false_when_role_absent_for_matching_tenant()
    {
        var tid = Guid.NewGuid();
        var (sut, _) = NewSut(new Dictionary<Guid, IReadOnlySet<string>>
        {
            [tid] = new HashSet<string> { "tenant_owner" },
        });
        sut.HasTenantRole(tid, "tenant_admin").Should().BeFalse();
    }

    [Fact]
    public void HasTenantRole_returns_false_when_membership_dict_never_stamped()
    {
        // Simulates a fresh anonymous request or a request where
        // UserProvisioningMiddleware didn't run (e.g. AllowAnonymous endpoint).
        var (sut, _) = NewSut();
        sut.HasTenantRole(Guid.NewGuid(), "tenant_admin").Should().BeFalse();
    }
}
