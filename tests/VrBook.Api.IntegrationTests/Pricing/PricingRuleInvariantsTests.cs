using FluentAssertions;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Modules.Pricing.Domain;
using Xunit;

namespace VrBook.Api.IntegrationTests.Pricing;

/// <summary>
/// Slice 6 C1 — exercises the §2.4.1 rule-kind × adjustment-kind matrix guards,
/// the AddRule/RemoveRule/ReorderRules/SetRuleEnabled aggregate methods, and
/// the event emission contract (Added/Removed raise; Enabled does not).
/// </summary>
[Trait("Category", "Unit")]
public sealed class PricingRuleInvariantsTests
{
    private static PricingPlan FreshPlan()
    {
        var plan = PricingPlan.Create(new Guid("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), 100m, "USD");
        _ = plan.DequeueEvents(); // drain the Create-time PricingPlanUpdated
        return plan;
    }

    // --- §2.4.1 matrix: happy cells ----------------------------------------

    [Theory]
    [InlineData(PricingAdjustmentKind.Absolute)]
    [InlineData(PricingAdjustmentKind.Multiplier)]
    [InlineData(PricingAdjustmentKind.Override)]
    public void DateRangeOverride_accepts_any_adjustment_kind(PricingAdjustmentKind kind)
    {
        var plan = FreshPlan();
        var act = () => plan.AddRule(
            kind: PricingRuleKind.DateRangeOverride,
            priority: 0,
            startDate: new DateOnly(2026, 12, 20),
            endDate: new DateOnly(2027, 1, 5),
            dayOfWeekMask: null,
            minNights: null,
            maxNights: null,
            daysBeforeCheckin: null,
            adjustmentKind: kind,
            adjustmentValue: kind == PricingAdjustmentKind.Multiplier ? 1.5m : 50m,
            isEnabled: true);
        act.Should().NotThrow();
        plan.Rules.Should().ContainSingle();
    }

    [Theory]
    [InlineData(PricingAdjustmentKind.Absolute)]
    [InlineData(PricingAdjustmentKind.Multiplier)]
    public void LastMinute_accepts_absolute_and_multiplier(PricingAdjustmentKind kind)
    {
        var plan = FreshPlan();
        var act = () => plan.AddRule(
            kind: PricingRuleKind.LastMinute,
            priority: 0,
            startDate: null,
            endDate: null,
            dayOfWeekMask: null,
            minNights: null,
            maxNights: null,
            daysBeforeCheckin: 2,
            adjustmentKind: kind,
            adjustmentValue: 0.8m,
            isEnabled: true);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(PricingAdjustmentKind.Absolute)]
    [InlineData(PricingAdjustmentKind.Multiplier)]
    public void LengthOfStay_accepts_absolute_and_multiplier(PricingAdjustmentKind kind)
    {
        var plan = FreshPlan();
        var act = () => plan.AddRule(
            kind: PricingRuleKind.LengthOfStay,
            priority: 0,
            startDate: null,
            endDate: null,
            dayOfWeekMask: null,
            minNights: 7,
            maxNights: 13,
            daysBeforeCheckin: null,
            adjustmentKind: kind,
            adjustmentValue: 0.9m,
            isEnabled: true);
        act.Should().NotThrow();
    }

    // --- §2.4.1 matrix: rejected cells --------------------------------------

    [Fact]
    public void LastMinute_with_override_is_rejected()
    {
        var plan = FreshPlan();
        var act = () => plan.AddRule(
            kind: PricingRuleKind.LastMinute,
            priority: 0,
            startDate: null,
            endDate: null,
            dayOfWeekMask: null,
            minNights: null,
            maxNights: null,
            daysBeforeCheckin: 2,
            adjustmentKind: PricingAdjustmentKind.Override,
            adjustmentValue: 99m,
            isEnabled: true);
        act.Should().Throw<ArgumentException>().WithMessage("*quote.invalid_rule*");
    }

    [Fact]
    public void LengthOfStay_with_override_is_rejected()
    {
        var plan = FreshPlan();
        var act = () => plan.AddRule(
            kind: PricingRuleKind.LengthOfStay,
            priority: 0,
            startDate: null,
            endDate: null,
            dayOfWeekMask: null,
            minNights: 7,
            maxNights: null,
            daysBeforeCheckin: null,
            adjustmentKind: PricingAdjustmentKind.Override,
            adjustmentValue: 99m,
            isEnabled: true);
        act.Should().Throw<ArgumentException>().WithMessage("*quote.invalid_rule*");
    }

    // --- per-kind required fields -------------------------------------------

    [Fact]
    public void DateRangeOverride_requires_start_and_end()
    {
        var plan = FreshPlan();
        var act = () => plan.AddRule(
            PricingRuleKind.DateRangeOverride, 0,
            startDate: null, endDate: new DateOnly(2027, 1, 1),
            null, null, null, null,
            PricingAdjustmentKind.Multiplier, 1.5m, true);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DateRangeOverride_requires_start_before_end()
    {
        var plan = FreshPlan();
        var act = () => plan.AddRule(
            PricingRuleKind.DateRangeOverride, 0,
            startDate: new DateOnly(2027, 1, 5), endDate: new DateOnly(2026, 12, 20),
            null, null, null, null,
            PricingAdjustmentKind.Multiplier, 1.5m, true);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LastMinute_requires_positive_days_before()
    {
        var plan = FreshPlan();
        var act = () => plan.AddRule(
            PricingRuleKind.LastMinute, 0,
            null, null, null, null, null,
            daysBeforeCheckin: 0,
            PricingAdjustmentKind.Multiplier, 0.8m, true);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LengthOfStay_max_must_be_at_least_min()
    {
        var plan = FreshPlan();
        var act = () => plan.AddRule(
            PricingRuleKind.LengthOfStay, 0,
            null, null, null,
            minNights: 7, maxNights: 6,
            null,
            PricingAdjustmentKind.Multiplier, 0.9m, true);
        act.Should().Throw<ArgumentException>();
    }

    // --- mutators ------------------------------------------------------------

    [Fact]
    public void AddRule_raises_PricingRuleAdded()
    {
        var plan = FreshPlan();
        var rule = plan.AddRule(
            PricingRuleKind.LastMinute, 0,
            null, null, null, null, null, 2,
            PricingAdjustmentKind.Multiplier, 0.8m, true);
        var added = plan.DequeueEvents().OfType<PricingRuleAdded>().Should().ContainSingle().Subject;
        added.PricingPlanId.Should().Be(plan.Id);
        added.RuleId.Should().Be(rule.Id);
    }

    [Fact]
    public void RemoveRule_raises_PricingRuleRemoved_and_drops_rule()
    {
        var plan = FreshPlan();
        var rule = plan.AddRule(
            PricingRuleKind.LastMinute, 0,
            null, null, null, null, null, 2,
            PricingAdjustmentKind.Multiplier, 0.8m, true);
        _ = plan.DequeueEvents(); // drain the AddRule event

        plan.RemoveRule(rule.Id);

        plan.Rules.Should().BeEmpty();
        plan.DequeueEvents().OfType<PricingRuleRemoved>().Should().ContainSingle()
            .Which.RuleId.Should().Be(rule.Id);
    }

    [Fact]
    public void RemoveRule_on_unknown_id_is_idempotent()
    {
        var plan = FreshPlan();
        var act = () => plan.RemoveRule(Guid.NewGuid());
        act.Should().NotThrow();
        plan.DequeueEvents().OfType<PricingRuleRemoved>().Should().BeEmpty();
    }

    [Fact]
    public void ReorderRules_rewrites_all_priorities_zero_to_n_minus_one()
    {
        var plan = FreshPlan();
        var r1 = plan.AddRule(PricingRuleKind.LastMinute, priority: 10,
            null, null, null, null, null, 2,
            PricingAdjustmentKind.Multiplier, 0.8m, true);
        var r2 = plan.AddRule(PricingRuleKind.LengthOfStay, priority: 20,
            null, null, null, 7, null, null,
            PricingAdjustmentKind.Multiplier, 0.9m, true);
        var r3 = plan.AddRule(PricingRuleKind.DateRangeOverride, priority: 30,
            new DateOnly(2026, 12, 20), new DateOnly(2027, 1, 5),
            null, null, null, null,
            PricingAdjustmentKind.Multiplier, 1.5m, true);

        plan.ReorderRules(new[] { r3.Id, r1.Id, r2.Id });

        r3.Priority.Should().Be(0);
        r1.Priority.Should().Be(1);
        r2.Priority.Should().Be(2);
    }

    [Fact]
    public void ReorderRules_rejects_size_mismatch()
    {
        var plan = FreshPlan();
        plan.AddRule(PricingRuleKind.LastMinute, 0,
            null, null, null, null, null, 2,
            PricingAdjustmentKind.Multiplier, 0.8m, true);
        var act = () => plan.ReorderRules(Array.Empty<Guid>());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ReorderRules_rejects_unknown_id()
    {
        var plan = FreshPlan();
        plan.AddRule(PricingRuleKind.LastMinute, 0,
            null, null, null, null, null, 2,
            PricingAdjustmentKind.Multiplier, 0.8m, true);
        var act = () => plan.ReorderRules(new[] { Guid.NewGuid() });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetRuleEnabled_flips_flag_without_raising_events()
    {
        var plan = FreshPlan();
        var rule = plan.AddRule(
            PricingRuleKind.LastMinute, 0,
            null, null, null, null, null, 2,
            PricingAdjustmentKind.Multiplier, 0.8m, isEnabled: true);
        _ = plan.DequeueEvents(); // drain the AddRule event

        plan.SetRuleEnabled(rule.Id, false);

        rule.IsEnabled.Should().BeFalse();
        plan.DequeueEvents().Should().BeEmpty();
    }

    [Fact]
    public void AddRule_without_priority_appends_at_max_plus_one()
    {
        var plan = FreshPlan();
        plan.AddRule(PricingRuleKind.LastMinute, priority: 5,
            null, null, null, null, null, 2,
            PricingAdjustmentKind.Multiplier, 0.8m, true);
        var r = plan.AddRule(PricingRuleKind.LengthOfStay, priority: null,
            null, null, null, 7, null, null,
            PricingAdjustmentKind.Multiplier, 0.9m, true);
        r.Priority.Should().Be(6);
    }
}
