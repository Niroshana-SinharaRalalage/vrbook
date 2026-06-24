using FluentAssertions;
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
/// Slice 6 C3 — handler-level tests for the 5 rule-mutation commands.
/// Covers happy CRUD, the §2.12 cross-property auth guard, admin bypass,
/// idempotent remove, and §2.4.1 invalid combos surfacing at AddRule time.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PricingRuleEndpointsTests
{
    private const string OwnerB2C = "owner-1";
    private static readonly Guid OwnerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherOwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // --- harness -----------------------------------------------------------

    private static (PricingPlan plan, IPricingPlanRepository repo, IPropertyOwnerLookup lookup, ICurrentUser user, IUnitOfWork uow) Setup(
        Guid? propertyOwnerId = null,
        bool currentUserIsAdmin = false)
    {
        var propertyId = Guid.NewGuid();
        var plan = PricingPlan.Create(propertyId, 100m, "USD");
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
        user.IsAdmin.Returns(currentUserIsAdmin);

        // Aggregate mutations happen in-memory on the plan returned by the
        // mocked repo; the UoW.SaveChangesAsync is a no-op here. Production
        // wires IUnitOfWork → PricingDbContext via PricingModule.
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));

        return (plan, repo, lookup, user, uow);
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

    private static CreatePricingRuleRequest LastMinuteReq(int priority = 0) =>
        new(PricingRuleKind.LastMinute, priority, null, null, null, null, null, 2,
            PricingAdjustmentKind.Multiplier, 0.8m, true);

    private static CreatePricingRuleRequest LengthOfStayReq(int priority = 0) =>
        new(PricingRuleKind.LengthOfStay, priority, null, null, null, 7, 13, null,
            PricingAdjustmentKind.Multiplier, 0.9m, true);

    // --- happy CRUD for each kind ------------------------------------------

    [Theory]
    [InlineData(PricingRuleKind.DateRangeOverride)]
    [InlineData(PricingRuleKind.LastMinute)]
    [InlineData(PricingRuleKind.LengthOfStay)]
    public async Task Add_creates_rule_for_each_kind(PricingRuleKind kind)
    {
        var (plan, repo, lookup, user, uow) = Setup();
        var req = kind switch
        {
            PricingRuleKind.DateRangeOverride => SeasonalReq(),
            PricingRuleKind.LastMinute => LastMinuteReq(),
            PricingRuleKind.LengthOfStay => LengthOfStayReq(),
            _ => throw new InvalidOperationException(),
        };
        var handler = new AddPricingRuleHandler(user, lookup, repo, uow);

        var dto = await handler.Handle(new AddPricingRuleCommand(plan.PropertyId, req), default);

        dto.Kind.Should().Be(kind);
        plan.Rules.Should().ContainSingle().Which.Id.Should().Be(dto.Id);
    }

    [Fact]
    public async Task Update_replaces_rule_and_keeps_aggregate_consistent()
    {
        var (plan, repo, lookup, user, uow) = Setup();
        var addHandler = new AddPricingRuleHandler(user, lookup, repo, uow);
        var added = await addHandler.Handle(new AddPricingRuleCommand(plan.PropertyId, SeasonalReq()), default);

        var updateHandler = new UpdatePricingRuleHandler(user, lookup, repo, uow);
        var changed = await updateHandler.Handle(
            new UpdatePricingRuleCommand(plan.PropertyId, added.Id, LengthOfStayReq()), default);

        plan.Rules.Should().ContainSingle();
        changed.Kind.Should().Be(PricingRuleKind.LengthOfStay);
        changed.Id.Should().NotBe(added.Id); // Remove + Add yields a new rule id
    }

    [Fact]
    public async Task Delete_removes_rule_idempotently()
    {
        var (plan, repo, lookup, user, uow) = Setup();
        var added = await new AddPricingRuleHandler(user, lookup, repo, uow)
            .Handle(new AddPricingRuleCommand(plan.PropertyId, SeasonalReq()), default);

        var deleteHandler = new RemovePricingRuleHandler(user, lookup, repo, uow);
        await deleteHandler.Handle(new RemovePricingRuleCommand(plan.PropertyId, added.Id), default);
        plan.Rules.Should().BeEmpty();

        // Second delete on the same id no-ops without throwing (idempotent).
        var act = () => deleteHandler.Handle(new RemovePricingRuleCommand(plan.PropertyId, added.Id), default);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetEnabled_toggles_flag_without_replacing_the_rule()
    {
        var (plan, repo, lookup, user, uow) = Setup();
        var added = await new AddPricingRuleHandler(user, lookup, repo, uow)
            .Handle(new AddPricingRuleCommand(plan.PropertyId, SeasonalReq()), default);

        var dto = await new SetPricingRuleEnabledHandler(user, lookup, repo, uow)
            .Handle(new SetPricingRuleEnabledCommand(plan.PropertyId, added.Id, IsEnabled: false), default);

        dto.IsEnabled.Should().BeFalse();
        plan.Rules.Should().ContainSingle().Which.Id.Should().Be(added.Id);
    }

    [Fact]
    public async Task Reorder_rewrites_priorities_to_zero_to_n_minus_one()
    {
        var (plan, repo, lookup, user, uow) = Setup();
        var add = new AddPricingRuleHandler(user, lookup, repo, uow);
        var a = await add.Handle(new AddPricingRuleCommand(plan.PropertyId, SeasonalReq(priority: 0)), default);
        var b = await add.Handle(new AddPricingRuleCommand(plan.PropertyId, LastMinuteReq(priority: 1)), default);
        var c = await add.Handle(new AddPricingRuleCommand(plan.PropertyId, LengthOfStayReq(priority: 2)), default);

        var dto = await new ReorderPricingRulesHandler(user, lookup, repo, uow)
            .Handle(new ReorderPricingRulesCommand(plan.PropertyId, new[] { c.Id, a.Id, b.Id }), default);

        dto.Rules.Select(r => r.Id).Should().Equal(c.Id, a.Id, b.Id);
        dto.Rules.Select(r => r.Priority).Should().Equal(0, 1, 2);
    }

    // --- cross-property auth (§2.12) ---------------------------------------

    [Fact]
    public async Task Add_for_property_owned_by_someone_else_throws_Forbidden()
    {
        var (plan, repo, lookup, user, uow) = Setup(propertyOwnerId: OtherOwnerId);

        var handler = new AddPricingRuleHandler(user, lookup, repo, uow);

        var act = () => handler.Handle(new AddPricingRuleCommand(plan.PropertyId, SeasonalReq()), default);
        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*not the owner*");
    }

    [Fact]
    public async Task Delete_for_property_owned_by_someone_else_throws_Forbidden()
    {
        var (plan, repo, lookup, user, uow) = Setup(propertyOwnerId: OtherOwnerId);

        var handler = new RemovePricingRuleHandler(user, lookup, repo, uow);

        var act = () => handler.Handle(new RemovePricingRuleCommand(plan.PropertyId, Guid.NewGuid()), default);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Update_for_property_owned_by_someone_else_throws_Forbidden()
    {
        var (plan, repo, lookup, user, uow) = Setup(propertyOwnerId: OtherOwnerId);
        var handler = new UpdatePricingRuleHandler(user, lookup, repo, uow);

        var act = () => handler.Handle(
            new UpdatePricingRuleCommand(plan.PropertyId, Guid.NewGuid(), SeasonalReq()), default);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task SetEnabled_for_property_owned_by_someone_else_throws_Forbidden()
    {
        var (plan, repo, lookup, user, uow) = Setup(propertyOwnerId: OtherOwnerId);
        var handler = new SetPricingRuleEnabledHandler(user, lookup, repo, uow);

        var act = () => handler.Handle(
            new SetPricingRuleEnabledCommand(plan.PropertyId, Guid.NewGuid(), false), default);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Reorder_for_property_owned_by_someone_else_throws_Forbidden()
    {
        var (plan, repo, lookup, user, uow) = Setup(propertyOwnerId: OtherOwnerId);
        var handler = new ReorderPricingRulesHandler(user, lookup, repo, uow);

        var act = () => handler.Handle(
            new ReorderPricingRulesCommand(plan.PropertyId, Array.Empty<Guid>()), default);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Admin_bypasses_ownership_check()
    {
        var (plan, repo, lookup, user, uow) = Setup(propertyOwnerId: OtherOwnerId, currentUserIsAdmin: true);
        var handler = new AddPricingRuleHandler(user, lookup, repo, uow);

        var dto = await handler.Handle(new AddPricingRuleCommand(plan.PropertyId, SeasonalReq()), default);

        dto.Should().NotBeNull();
        plan.Rules.Should().ContainSingle();
    }

    [Fact]
    public async Task Anonymous_user_throws_Forbidden()
    {
        var (plan, repo, lookup, user, uow) = Setup();
        user.UserId.Returns((Guid?)null);
        user.IsAuthenticated.Returns(false);
        user.IsAdmin.Returns(false);

        var handler = new AddPricingRuleHandler(user, lookup, repo, uow);

        var act = () => handler.Handle(new AddPricingRuleCommand(plan.PropertyId, SeasonalReq()), default);
        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*Sign-in required*");
    }

    // --- §2.4.1 invalid combos surface as ArgumentException at AddRule -----

    [Fact]
    public async Task Add_LastMinute_with_Override_is_rejected()
    {
        var (plan, repo, lookup, user, uow) = Setup();
        var req = new CreatePricingRuleRequest(
            PricingRuleKind.LastMinute, 0, null, null, null, null, null, 2,
            PricingAdjustmentKind.Override, 99m, true);
        var handler = new AddPricingRuleHandler(user, lookup, repo, uow);

        var act = () => handler.Handle(new AddPricingRuleCommand(plan.PropertyId, req), default);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*quote.invalid_rule*");
        plan.Rules.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_LengthOfStay_with_Override_is_rejected()
    {
        var (plan, repo, lookup, user, uow) = Setup();
        var req = new CreatePricingRuleRequest(
            PricingRuleKind.LengthOfStay, 0, null, null, null, 7, null, null,
            PricingAdjustmentKind.Override, 99m, true);
        var handler = new AddPricingRuleHandler(user, lookup, repo, uow);

        var act = () => handler.Handle(new AddPricingRuleCommand(plan.PropertyId, req), default);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*quote.invalid_rule*");
    }
}
