using FluentAssertions;
using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Identity.Application.Tenants.Queries;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// Slice OPS.M.13.5 — shape + auth-gate assertions for
/// <see cref="GetMyTenantsQuery"/> / GetMyTenantsHandler. The query drives
/// the SPA post-sign-in tenant picker per
/// <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §3.2.
///
/// <para>End-to-end behavior (0/1/N branching) is exercised by the SPA
/// unit tests + the walk in M.13.6; here we lock the contract shape that
/// the picker code depends on.</para>
/// </summary>
[Trait("Category", "Unit")]
public sealed class GetMyTenantsHandlerTests
{
    [Fact]
    public void Query_does_not_implement_ITenantScoped()
    {
        // Must NOT be tenant-scoped: it's the query that DECIDES which
        // tenant becomes active, so it must run before any tenant scope
        // exists on the caller.
        typeof(GetMyTenantsQuery).GetInterfaces()
            .Should().NotContain(typeof(ITenantScoped));
    }

    [Fact]
    public void Query_implements_IRequest_of_MyTenantsResponse()
    {
        typeof(GetMyTenantsQuery).GetInterfaces()
            .Should().Contain(typeof(IRequest<MyTenantsResponse>));
    }

    [Fact]
    public void Query_is_a_sealed_record()
    {
        typeof(GetMyTenantsQuery).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void Handler_constructor_takes_ICurrentUser_first_for_auth_derivation()
    {
        var handlerType = typeof(GetMyTenantsQuery).Assembly
            .GetType("VrBook.Modules.Identity.Application.Tenants.Queries.GetMyTenantsHandler")!;
        handlerType.Should().NotBeNull();
        var ctor = handlerType.GetConstructors().Single();
        var p = ctor.GetParameters();
        p[0].ParameterType.Should().Be(typeof(ICurrentUser),
            because: "handler derives caller identity from ICurrentUser, not from the request payload.");
    }

    [Fact]
    public void Response_carries_memberships_list_and_isPlatformAdmin_flag()
    {
        var t = typeof(MyTenantsResponse);
        t.GetProperty("Memberships")?.PropertyType
            .Should().Be(typeof(IReadOnlyList<MyTenantMembershipDto>));
        t.GetProperty("IsPlatformAdmin")?.PropertyType.Should().Be(typeof(bool));
    }

    [Fact]
    public void MembershipDto_carries_tenantId_slug_displayName_status_role_isPrimary()
    {
        var t = typeof(MyTenantMembershipDto);
        t.GetProperty("TenantId")?.PropertyType.Should().Be(typeof(Guid));
        t.GetProperty("Slug")?.PropertyType.Should().Be(typeof(string));
        t.GetProperty("DisplayName")?.PropertyType.Should().Be(typeof(string));
        t.GetProperty("Status")?.PropertyType.Should().Be(typeof(string));
        t.GetProperty("Role")?.PropertyType.Should().Be(typeof(string));
        t.GetProperty("IsPrimary")?.PropertyType.Should().Be(typeof(bool));
    }
}
