using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Identity.Infrastructure.Auth;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// Slice OPS.M.13.6 — unit tests for the <see cref="HttpCurrentUser"/> read
/// paths that were rewired to consume <c>HttpContext.Items</c> keys stamped
/// by <c>UserProvisioningMiddleware</c>. The middleware itself needs a
/// real DB + testcontainer to exercise end-to-end; here we lock in the
/// contract that middleware + HttpCurrentUser share.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantPickerHeaderPipelineTests
{
    private static HttpContext NewCtxWithItems(
        Guid? activeTenantId = null,
        IReadOnlyDictionary<Guid, IReadOnlySet<string>>? membershipRoles = null)
    {
        var ctx = new DefaultHttpContext();
        if (activeTenantId.HasValue)
        {
            ctx.Items[HttpCurrentUser.ActiveTenantItemKey] = activeTenantId.Value;
        }
        if (membershipRoles is not null)
        {
            ctx.Items[HttpCurrentUser.MembershipRolesItemKey] = membershipRoles;
        }
        return ctx;
    }

    private static ICurrentUser NewCurrentUser(HttpContext ctx)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(ctx);
        return new HttpCurrentUser(accessor);
    }

    [Fact]
    public void TenantId_reads_from_Items_activeTenantId_when_set()
    {
        var tid = Guid.NewGuid();
        var ctx = NewCtxWithItems(activeTenantId: tid);

        var cu = NewCurrentUser(ctx);

        cu.TenantId.Should().Be(tid,
            because: "OPS.M.13.6 — Items[ActiveTenantItemKey] is the canonical source; the app_tenant_id claim is a legacy fallback only.");
    }

    [Fact]
    public void TenantId_is_null_when_neither_Items_nor_claim_set()
    {
        var cu = NewCurrentUser(new DefaultHttpContext());
        cu.TenantId.Should().BeNull();
    }

    [Fact]
    public void MembershipRoles_returns_empty_dict_when_not_stamped()
    {
        var cu = NewCurrentUser(new DefaultHttpContext());
        cu.MembershipRoles.Should().BeEmpty(
            because: "Guests + workers get the empty-dict sentinel rather than throwing.");
    }

    [Fact]
    public void MembershipRoles_returns_stamped_dictionary()
    {
        var ta = Guid.NewGuid();
        var tb = Guid.NewGuid();
        var stamped = new Dictionary<Guid, IReadOnlySet<string>>
        {
            [ta] = new HashSet<string> { "tenant_admin", "tenant_owner" },
            [tb] = new HashSet<string> { "tenant_member" },
        };
        var ctx = NewCtxWithItems(membershipRoles: stamped);

        var cu = NewCurrentUser(ctx);

        cu.MembershipRoles.Should().HaveCount(2);
        cu.MembershipRoles[ta].Should().Contain("tenant_admin");
        cu.MembershipRoles[tb].Should().Contain("tenant_member");
    }

    [Fact]
    public void HasTenantRole_reads_from_MembershipRoles_dict_scoped_to_tenant()
    {
        var ta = Guid.NewGuid();
        var tb = Guid.NewGuid();
        var stamped = new Dictionary<Guid, IReadOnlySet<string>>
        {
            [ta] = new HashSet<string> { "tenant_admin" },
            [tb] = new HashSet<string> { "tenant_member" },
        };
        var ctx = NewCtxWithItems(membershipRoles: stamped);
        var cu = NewCurrentUser(ctx);

        cu.HasTenantRole(ta, "tenant_admin").Should().BeTrue();
        cu.HasTenantRole(tb, "tenant_member").Should().BeTrue();
        // The pre-M.13.6 cross-tenant claim hazard: tenant_admin in tenant A
        // should NOT satisfy a role check for tenant B.
        cu.HasTenantRole(tb, "tenant_admin").Should().BeFalse(
            because: "M.13.6 fix for the Ev-A cross-tenant role-claim leak — roles are correlated with the specific tenant.");
        // Unknown tenant returns false.
        cu.HasTenantRole(Guid.NewGuid(), "tenant_admin").Should().BeFalse();
    }

    [Fact]
    public void HasTenantRole_rejects_Guid_Empty_and_null_role()
    {
        var cu = NewCurrentUser(new DefaultHttpContext());
        cu.HasTenantRole(Guid.Empty, "tenant_admin").Should().BeFalse();
        cu.HasTenantRole(Guid.NewGuid(), "").Should().BeFalse();
    }

    [Fact]
    public void ActiveTenantHeader_constant_matches_the_agreed_wire_format()
    {
        HttpCurrentUser.ActiveTenantHeader.Should().Be("X-Active-Tenant",
            because: "the SPA (web/src/lib/api/client.ts) sends this exact header name; renaming it here without a coordinated frontend change breaks the pipeline silently.");
    }
}
