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
    private static readonly Guid OtherOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

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
            .Returns(new PropertyOwnerSnapshot(propertyId, propertyOwnerId ?? OwnerUserId, "Test Property"));

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(OwnerUserId);
        user.B2CObjectId.Returns(OwnerB2C);
        user.IsAuthenticated.Returns(true);
        user.IsOwner.Returns(true);
        user.IsAdmin.Returns(false);

        // No provider — the auth guard and aggregate invariants throw BEFORE
        // any db.Database.ExecuteSqlInterpolatedAsync call, so the context
        // never has to actually run a query.
        var opts = new DbContextOptionsBuilder<PricingDbContext>().Options;
        var clock = Substitute.For<IDateTimeProvider>();
        var db = new PricingDbContext(opts, user, clock);

        return (plan, repo, lookup, user, db);
    }

    private static CreatePricingRuleRequest SeasonalReq(int priority = 0) =>
        new(
            Kind: PricingRuleKind.DateRangeOverride,
            Priority: priority,
            StartDate: new DateOnly(2026, 12, 20),
            EndDate: new DateOnly(2027, 1, 5),
            DayOfWeekMask: null,
            MinNights: null,
            MaxNights: null,
            DaysBeforeCheckin: null,
            AdjustmentKind: PricingAdjustmentKind.Multiplier,
            AdjustmentValue: 1.5m,
            IsEnabled: true);

    // --- cross-property auth (§2.12) ---------------------------------------

    [Fact]
    public async Task Add_for_property_owned_by_someone_else_throws_Forbidden()
    {
        var (plan, repo, lookup, user, db) = Setup(propertyOwnerId: OtherOwnerId);
        var handler = new AddPricingRuleHandler(user, lookup, repo, db);
        var act = () => handler.Handle(new AddPricingRuleCommand(plan.PropertyId, SeasonalReq()), default);
        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*not the owner*");
    }

    [Fact]
    public async Task Delete_for_property_owned_by_someone_else_throws_Forbidden()
    {
        var (plan, repo, lookup, user, db) = Setup(propertyOwnerId: OtherOwnerId);
        var handler = new RemovePricingRuleHandler(user, lookup, repo, db);
        var act = () => handler.Handle(new RemovePricingRuleCommand(plan.PropertyId, Guid.NewGuid()), default);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Update_for_property_owned_by_someone_else_throws_Forbidden()
    {
        var (plan, repo, lookup, user, db) = Setup(propertyOwnerId: OtherOwnerId);
        var handler = new UpdatePricingRuleHandler(user, lookup, repo, db);
        var act = () => handler.Handle(
            new UpdatePricingRuleCommand(plan.PropertyId, Guid.NewGuid(), SeasonalReq()), default);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task SetEnabled_for_property_owned_by_someone_else_throws_Forbidden()
    {
        var (plan, repo, lookup, user, db) = Setup(propertyOwnerId: OtherOwnerId);
        var handler = new SetPricingRuleEnabledHandler(user, lookup, repo, db);
        var act = () => handler.Handle(
            new SetPricingRuleEnabledCommand(plan.PropertyId, Guid.NewGuid(), false), default);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Reorder_for_property_owned_by_someone_else_throws_Forbidden()
    {
        var (plan, repo, lookup, user, db) = Setup(propertyOwnerId: OtherOwnerId);
        var handler = new ReorderPricingRulesHandler(user, lookup, repo, db);
        var act = () => handler.Handle(
            new ReorderPricingRulesCommand(plan.PropertyId, Array.Empty<Guid>()), default);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Anonymous_user_throws_Forbidden()
    {
        var (plan, repo, lookup, user, db) = Setup();
        user.UserId.Returns((Guid?)null);
        user.IsAuthenticated.Returns(false);
        user.IsAdmin.Returns(false);
        var handler = new AddPricingRuleHandler(user, lookup, repo, db);
        var act = () => handler.Handle(new AddPricingRuleCommand(plan.PropertyId, SeasonalReq()), default);
        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*Sign-in required*");
    }

    // --- §2.4.1 invalid combos surface as ArgumentException at AddRule -----

    [Fact]
    public async Task Add_LastMinute_with_Override_is_rejected()
    {
        var (plan, repo, lookup, user, db) = Setup();
        var req = new CreatePricingRuleRequest(
            PricingRuleKind.LastMinute, 0, null, null, null, null, null, 2,
            PricingAdjustmentKind.Override, 99m, true);
        var handler = new AddPricingRuleHandler(user, lookup, repo, db);
        var act = () => handler.Handle(new AddPricingRuleCommand(plan.PropertyId, req), default);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*quote.invalid_rule*");
    }

    [Fact]
    public async Task Add_LengthOfStay_with_Override_is_rejected()
    {
        var (plan, repo, lookup, user, db) = Setup();
        var req = new CreatePricingRuleRequest(
            PricingRuleKind.LengthOfStay, 0, null, null, null, 7, null, null,
            PricingAdjustmentKind.Override, 99m, true);
        var handler = new AddPricingRuleHandler(user, lookup, repo, db);
        var act = () => handler.Handle(new AddPricingRuleCommand(plan.PropertyId, req), default);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*quote.invalid_rule*");
    }
}
