using FluentAssertions;
using VrBook.Contracts.Events;
using VrBook.Modules.Identity.Domain;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// OPS.M.1 — unit tests for TenantMembership aggregate invariants per
/// `docs/OPS_M_1_PLAN.md` §4 Step 1.
/// Role CHECK enforcement, primary toggling, soft-delete via Revoke.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TenantMembershipAggregateTests
{
    private static readonly Guid AnyUserId = Guid.NewGuid();
    private static readonly Guid AnyTenantId = Guid.NewGuid();

    [Fact]
    public void Create_with_tenant_admin_succeeds_and_raises_event()
    {
        var membership = TenantMembership.Create(
            AnyUserId, AnyTenantId, TenantMembership.RoleTenantAdmin, isPrimary: true);

        membership.UserId.Should().Be(AnyUserId);
        membership.TenantId.Should().Be(AnyTenantId);
        membership.Role.Should().Be(TenantMembership.RoleTenantAdmin);
        membership.IsPrimary.Should().BeTrue();
        membership.IsDeleted.Should().BeFalse();

        var evt = membership.DequeueEvents().Should().ContainSingle()
            .Which.Should().BeOfType<TenantMembershipCreated>().Subject;
        evt.UserId.Should().Be(AnyUserId);
        evt.TenantId.Should().Be(AnyTenantId);
        evt.Role.Should().Be(TenantMembership.RoleTenantAdmin);
    }

    [Fact]
    public void Create_with_tenant_member_succeeds()
    {
        var membership = TenantMembership.Create(AnyUserId, AnyTenantId, TenantMembership.RoleTenantMember);
        membership.Role.Should().Be(TenantMembership.RoleTenantMember);
        membership.IsPrimary.Should().BeFalse();
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("admin")]
    [InlineData("super_admin")]
    [InlineData("hacker")]
    public void Create_rejects_unknown_role(string role)
    {
        var act = () => TenantMembership.Create(AnyUserId, AnyTenantId, role);
        act.Should().Throw<ArgumentException>().WithParameterName("role");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_rejects_blank_role(string? role)
    {
        var act = () => TenantMembership.Create(AnyUserId, AnyTenantId, role!);
        act.Should().Throw<ArgumentException>().WithParameterName("role");
    }

    [Fact]
    public void Create_rejects_empty_user_id()
    {
        var act = () => TenantMembership.Create(Guid.Empty, AnyTenantId, TenantMembership.RoleTenantAdmin);
        act.Should().Throw<ArgumentException>().WithParameterName("userId");
    }

    [Fact]
    public void Create_rejects_empty_tenant_id()
    {
        var act = () => TenantMembership.Create(AnyUserId, Guid.Empty, TenantMembership.RoleTenantAdmin);
        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void MakePrimary_and_ClearPrimary_toggle_correctly()
    {
        var membership = TenantMembership.Create(AnyUserId, AnyTenantId, TenantMembership.RoleTenantAdmin);
        membership.IsPrimary.Should().BeFalse();

        membership.MakePrimary();
        membership.IsPrimary.Should().BeTrue();

        membership.ClearPrimary();
        membership.IsPrimary.Should().BeFalse();
    }

    [Fact]
    public void ChangeRole_to_other_allowed_role_raises_event()
    {
        var membership = TenantMembership.Create(AnyUserId, AnyTenantId, TenantMembership.RoleTenantMember);
        _ = membership.DequeueEvents();

        membership.ChangeRole(TenantMembership.RoleTenantAdmin);

        membership.Role.Should().Be(TenantMembership.RoleTenantAdmin);
        var evt = membership.DequeueEvents().Should().ContainSingle()
            .Which.Should().BeOfType<TenantMembershipRoleChanged>().Subject;
        evt.OldRole.Should().Be(TenantMembership.RoleTenantMember);
        evt.NewRole.Should().Be(TenantMembership.RoleTenantAdmin);
    }

    [Fact]
    public void ChangeRole_to_same_role_is_noop()
    {
        var membership = TenantMembership.Create(AnyUserId, AnyTenantId, TenantMembership.RoleTenantAdmin);
        _ = membership.DequeueEvents();

        membership.ChangeRole(TenantMembership.RoleTenantAdmin);

        membership.DequeueEvents().Should().BeEmpty();
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("super_admin")]
    public void ChangeRole_rejects_unknown_role(string role)
    {
        var membership = TenantMembership.Create(AnyUserId, AnyTenantId, TenantMembership.RoleTenantAdmin);
        var act = () => membership.ChangeRole(role);
        act.Should().Throw<ArgumentException>().WithParameterName("role");
    }

    [Fact]
    public void Revoke_soft_deletes_and_raises_event_once()
    {
        var membership = TenantMembership.Create(AnyUserId, AnyTenantId, TenantMembership.RoleTenantAdmin);
        _ = membership.DequeueEvents();
        var actor = Guid.NewGuid();

        membership.Revoke(actor);

        membership.IsDeleted.Should().BeTrue();
        membership.DeletedBy.Should().Be(actor);
        var evt = membership.DequeueEvents().Should().ContainSingle()
            .Which.Should().BeOfType<TenantMembershipRevoked>().Subject;
        evt.UserId.Should().Be(AnyUserId);
        evt.TenantId.Should().Be(AnyTenantId);

        membership.Revoke(Guid.NewGuid());
        membership.DequeueEvents().Should().BeEmpty("re-revoking already-soft-deleted membership is a no-op");
    }
}
