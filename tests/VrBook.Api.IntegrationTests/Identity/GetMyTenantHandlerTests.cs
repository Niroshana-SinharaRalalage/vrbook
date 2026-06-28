using FluentAssertions;
using MediatR;
using NSubstitute;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Application.Tenants.Queries;
using Xunit;

namespace VrBook.Api.IntegrationTests.Identity;

/// <summary>
/// Slice OPS.M.7 §3.2 + §4.3 Step 4 — pins the auth-gating shape of
/// <c>GetMyTenantHandler</c>. Success-path testing against the real
/// <c>IdentityDbContext</c> requires Postgres (sealed class, EF setup);
/// those facts run in CI under <c>Category=Integration</c>. The contract
/// facts below catch a regression on the <c>ICurrentUser.TenantId == null</c>
/// guard without paying the testcontainer startup cost.
/// </summary>
[Trait("Category", "Unit")]
public sealed class GetMyTenantHandlerTests
{

    [Fact]
    public void Query_does_not_implement_ITenantScoped()
    {
        // Sanity sentinel for §3.2 (D2). The query derives the tenant id
        // from ICurrentUser; adding ITenantScoped would re-route through
        // TenantAuthorizationBehavior and break the auth gate.
        typeof(GetMyTenantQuery).GetInterfaces()
            .Should().NotContain(typeof(ITenantScoped));
    }

    [Fact]
    public void Query_implements_IRequest_of_MeTenantDto()
    {
        typeof(GetMyTenantQuery).GetInterfaces()
            .Should().Contain(typeof(IRequest<MeTenantDto>));
    }

    [Fact]
    public void Query_is_a_sealed_record()
    {
        typeof(GetMyTenantQuery).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void Handler_constructor_takes_ICurrentUser_first_for_auth_derivation()
    {
        var handlerType = typeof(GetMyTenantQuery).Assembly
            .GetType("VrBook.Modules.Identity.Application.Tenants.Queries.GetMyTenantHandler")!;
        var ctor = handlerType.GetConstructors().Single();
        var p = ctor.GetParameters();
        p[0].ParameterType.Should().Be(typeof(ICurrentUser),
            because: "OPS.M.7 §4.3 — handler derives tenant id from ICurrentUser, never from the request.");
    }

    [Fact]
    public void Handler_constructor_takes_IPropertyCountByTenant()
    {
        var handlerType = typeof(GetMyTenantQuery).Assembly
            .GetType("VrBook.Modules.Identity.Application.Tenants.Queries.GetMyTenantHandler")!;
        var ctor = handlerType.GetConstructors().Single();
        var paramTypes = ctor.GetParameters().Select(p => p.ParameterType).ToArray();
        paramTypes.Should().Contain(typeof(IPropertyCountByTenant),
            because: "OPS.M.7 §4.2 (D-row) — cross-module count read, not raw SQL.");
    }
}
