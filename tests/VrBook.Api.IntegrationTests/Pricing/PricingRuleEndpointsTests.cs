using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Pricing.Application.Plans.Commands;
using VrBook.Modules.Pricing.Domain;
using VrBook.Modules.Pricing.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Pricing;

/// <summary>
/// Slice 6 C3 + raw-SQL contingency — handler-level tests for the §2.12
/// cross-property auth guard, anonymous-user rejection, and §2.4.1 invariant
/// rejection at AddRule time. These all throw BEFORE the raw SQL fires so they
/// don't need a real Postgres. Happy-path persistence is verified on staging.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PricingRuleEndpointsTests
{
    private const string OwnerB2C = "owner-1";
    private static readonly Guid OwnerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static (PricingPlan plan, IPricingPlanRepository repo, IPropertyOwnerLookup lookup, ICurrentUser user, PricingDbContext db) Setup(
        Guid? propertyOwnerId = null)
    {
        var propertyId = Guid.NewGuid();
        var plan = PricingPlan.Create(new Guid("00000000-0000-0000-0000-000000000001"), propertyId, 100m, "USD");
        _ = plan.DequeueEvents();

        var repo = Substitute.For<IPricingPlanRepository>();
        repo.GetByPropertyIdAsync(propertyId, Arg.Any<CancellationToken>()).Returns(plan);

        var lookup = Substitute.For<IPropertyOwnerLookup>();
        lookup.GetAsync(propertyId, Arg.Any<CancellationToken>())
            .Returns(new PropertyOwnerSnapshot(propertyId, propertyOwnerId ?? OwnerUserId, "Test Property", new Guid("00000000-0000-0000-0000-000000000001")));

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(OwnerUserId);
        user.ExternalObjectId.Returns(OwnerB2C);
        user.IsAuthenticated.Returns(true);
        // Slice OPS.M.15.5 — IsOwner/IsAdmin removed. HasTenantRole
        // returns false by default; tests exercising the admin bypass
        // can override per-test.
        user.TenantId.Returns((Guid?)null);

        // No provider — the auth guard and aggregate invariants throw BEFORE
        // any db.Database.ExecuteSqlInterpolatedAsync call, so the context
        // never has to actually run a query.
        var opts = new DbContextOptionsBuilder<PricingDbContext>().Options;
        var clock = Substitute.For<IDateTimeProvider>();
        var db = new PricingDbContext(opts, user, clock);

        return (plan, repo, lookup, user, db);
    }

    // --- cross-property auth (§2.12) ---------------------------------------
    //
    // OPS.M.4 Step 3 — the 6 cross-property-auth tests below were removed
    // because the authorization concern they exercised moved out of the
    // handler and into the MediatR TenantAuthorizationBehavior at the
    // pipeline layer. Per the M.4 plan §5/§D6 + §6:
    //   • PricingAuthorization.RequireOwnerOrAdminAsync calls deleted from
    //     all 5 owner-mutation handlers
    //   • the handler ctors no longer take ICurrentUser / IPropertyOwnerLookup
    //   • cross-tenant rejection is exercised end-to-end by the new
    //     tests/VrBook.Api.IntegrationTests/Multitenancy/CrossTenantWriteRejectionTests
    //     test pack (lands in Step 5 of OPS.M.4).

    // --- §2.4.1 invalid combos surface as ArgumentException at AddRule -----

    [Fact]
    public async Task Add_LastMinute_with_Override_is_rejected()
    {
        var (plan, repo, _, _, db) = Setup();
        var req = new CreatePricingRuleRequest(
            PricingRuleKind.LastMinute, 0, null, null, null, null, null, 2,
            PricingAdjustmentKind.Override, 99m, true);
        var handler = new AddPricingRuleHandler(repo, db);
        var act = () => handler.Handle(new AddPricingRuleCommand(plan.PropertyId, req, new Guid("00000000-0000-0000-0000-000000000001")), default);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*quote.invalid_rule*");
    }

    [Fact]
    public async Task Add_LengthOfStay_with_Override_is_rejected()
    {
        var (plan, repo, _, _, db) = Setup();
        var req = new CreatePricingRuleRequest(
            PricingRuleKind.LengthOfStay, 0, null, null, null, 7, null, null,
            PricingAdjustmentKind.Override, 99m, true);
        var handler = new AddPricingRuleHandler(repo, db);
        var act = () => handler.Handle(new AddPricingRuleCommand(plan.PropertyId, req, new Guid("00000000-0000-0000-0000-000000000001")), default);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*quote.invalid_rule*");
    }
}
