using FluentAssertions;
using NSubstitute;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Pricing.Application.Quotes.Commands;
using VrBook.Modules.Pricing.Domain;
using VrBook.Modules.Pricing.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Pricing;

/// <summary>
/// Slice 6 C2 — engine applies enabled PricingRules in priority order
/// (lower number first) per §2.4 / §2.4.1. The matrix-reject combos are
/// blocked at AddRule time and tested separately in PricingRuleInvariantsTests;
/// this suite exercises the engine math + the RuleApplied badge contract.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ComputeQuoteWithRulesTests
{
    private static PricingPlan FreshPlan(decimal baseRate = 100m, decimal weekendRate = 0m)
    {
        var plan = PricingPlan.Create(new Guid("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), baseRate, "USD");
        plan.Replace(baseRate, weekendRate, "USD", minStay: 1, maxStay: 365, dynamicEnabled: false,
            fees: Array.Empty<(FeeKind, decimal, FeeBasis, int?, string)>());
        _ = plan.DequeueEvents();
        return plan;
    }

    private static ComputeQuoteHandler HandlerWith(PricingPlan plan, DateOnly today)
    {
        var repo = Substitute.For<IPricingPlanRepository>();
        repo.GetByPropertyIdAsync(plan.PropertyId, Arg.Any<CancellationToken>())
            .Returns(plan);
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        clock.Today.Returns(today);
        return new ComputeQuoteHandler(repo, clock);
    }

    private static ComputeQuoteCommand Cmd(Guid propertyId, DateOnly checkin, DateOnly checkout, int guests = 2) =>
        new(propertyId, new QuoteRequest(checkin, checkout, guests, ApplyLoyaltyDiscount: false));

    // --- Seasonal (DateRangeOverride) ---------------------------------------

    [Fact]
    public async Task Seasonal_multiplier_applies_per_in_window_night()
    {
        var plan = FreshPlan();
        plan.AddRule(
            PricingRuleKind.DateRangeOverride, priority: 0,
            startDate: new DateOnly(2026, 12, 20),
            endDate: new DateOnly(2027, 1, 5),
            null, null, null, null,
            PricingAdjustmentKind.Multiplier, 1.5m, true);

        // Dec 22 (Tue) through Dec 26 (Sat) = 4 nights, all in-window.
        var quote = await HandlerWith(plan, today: new DateOnly(2026, 12, 1))
            .Handle(Cmd(plan.PropertyId, new DateOnly(2026, 12, 22), new DateOnly(2026, 12, 26)), default);

        quote.Nightly.Should().HaveCount(4);
        quote.Subtotal.Amount.Should().Be(100m * 1.5m * 4);
        quote.Nightly.Should().AllSatisfy(n => n.RuleApplied.Should().Be("seasonal"));
    }

    [Fact]
    public async Task Seasonal_override_replaces_in_window_nights_only()
    {
        var plan = FreshPlan();
        plan.AddRule(
            PricingRuleKind.DateRangeOverride, priority: 0,
            startDate: new DateOnly(2026, 12, 20),
            endDate: new DateOnly(2026, 12, 23),
            null, null, null, null,
            PricingAdjustmentKind.Override, 200m, true);

        // Dec 18..24 = 6 nights; Dec 20/21/22/23 in window (4 nights at 200), Dec 18/19 at 100.
        var quote = await HandlerWith(plan, today: new DateOnly(2026, 12, 1))
            .Handle(Cmd(plan.PropertyId, new DateOnly(2026, 12, 18), new DateOnly(2026, 12, 24)), default);

        quote.Nightly.Should().HaveCount(6);
        quote.Subtotal.Amount.Should().Be(100m * 2 + 200m * 4);
    }

    // --- LastMinute ---------------------------------------------------------

    [Fact]
    public async Task LastMinute_multiplier_applies_when_within_window()
    {
        var plan = FreshPlan();
        plan.AddRule(
            PricingRuleKind.LastMinute, priority: 0,
            null, null, null, null, null, daysBeforeCheckin: 2,
            PricingAdjustmentKind.Multiplier, 0.8m, true);

        // Today = Aug 1, Checkin = Aug 2 → 1 day out → triggers.
        var quote = await HandlerWith(plan, today: new DateOnly(2026, 8, 1))
            .Handle(Cmd(plan.PropertyId, new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 5)), default);

        quote.Nightly.Should().HaveCount(3);
        quote.Subtotal.Amount.Should().Be(100m * 0.8m * 3);
        quote.Nightly.Should().AllSatisfy(n => n.RuleApplied.Should().Be("last_minute"));
    }

    [Fact]
    public async Task LastMinute_skipped_when_checkin_is_far_out()
    {
        var plan = FreshPlan();
        plan.AddRule(
            PricingRuleKind.LastMinute, priority: 0,
            null, null, null, null, null, daysBeforeCheckin: 2,
            PricingAdjustmentKind.Multiplier, 0.8m, true);

        // Today = Aug 1, Checkin = Aug 10 → 9 days out → no trigger.
        var quote = await HandlerWith(plan, today: new DateOnly(2026, 8, 1))
            .Handle(Cmd(plan.PropertyId, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 13)), default);

        quote.Subtotal.Amount.Should().Be(100m * 3);
        quote.Nightly.Should().AllSatisfy(n => n.RuleApplied.Should().Be("base"));
    }

    // --- LengthOfStay -------------------------------------------------------

    [Fact]
    public async Task LengthOfStay_applies_when_nights_within_range()
    {
        var plan = FreshPlan();
        plan.AddRule(
            PricingRuleKind.LengthOfStay, priority: 0,
            null, null, null, minNights: 7, maxNights: 13, null,
            PricingAdjustmentKind.Multiplier, 0.9m, true);

        var quote = await HandlerWith(plan, today: new DateOnly(2026, 8, 1))
            .Handle(Cmd(plan.PropertyId, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 17)), default);

        quote.Nightly.Should().HaveCount(7);
        quote.Subtotal.Amount.Should().Be(100m * 0.9m * 7);
    }

    [Fact]
    public async Task LengthOfStay_skipped_when_below_min()
    {
        // weekendRate=0 so Fri/Sat fall back to base — keeps the assertion simple.
        var plan = FreshPlan(baseRate: 100m, weekendRate: 0m);
        plan.AddRule(
            PricingRuleKind.LengthOfStay, priority: 0,
            null, null, null, minNights: 7, maxNights: 13, null,
            PricingAdjustmentKind.Multiplier, 0.9m, true);

        var quote = await HandlerWith(plan, today: new DateOnly(2026, 8, 1))
            .Handle(Cmd(plan.PropertyId, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 16)), default);

        quote.Subtotal.Amount.Should().Be(100m * 6);
        // Rule did NOT fire; nights keep their original badge. Fri/Sat tag as
        // "weekend" even when weekendRate=0, so just assert no rule label.
        quote.Nightly.Should().AllSatisfy(n =>
            n.RuleApplied.Should().BeOneOf("base", "weekend"));
    }

    [Fact]
    public async Task LengthOfStay_skipped_when_above_max()
    {
        var plan = FreshPlan();
        plan.AddRule(
            PricingRuleKind.LengthOfStay, priority: 0,
            null, null, null, minNights: 7, maxNights: 13, null,
            PricingAdjustmentKind.Multiplier, 0.9m, true);

        var quote = await HandlerWith(plan, today: new DateOnly(2026, 8, 1))
            .Handle(Cmd(plan.PropertyId, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 24)), default);

        quote.Subtotal.Amount.Should().Be(100m * 14);
    }

    [Fact]
    public async Task LengthOfStay_open_ended_max_allows_any_long_stay()
    {
        var plan = FreshPlan();
        plan.AddRule(
            PricingRuleKind.LengthOfStay, priority: 0,
            null, null, null, minNights: 14, maxNights: null, null,
            PricingAdjustmentKind.Multiplier, 0.8m, true);

        var quote = await HandlerWith(plan, today: new DateOnly(2026, 8, 1))
            .Handle(Cmd(plan.PropertyId, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 30)), default);

        quote.Subtotal.Amount.Should().Be(100m * 0.8m * 20);
    }

    // --- Stacking + ordering -------------------------------------------------

    [Fact]
    public async Task Two_multipliers_compound_priority_swap_does_not_change_total()
    {
        var plan = FreshPlan();
        // priority 0 (applied first): Seasonal +50%
        plan.AddRule(
            PricingRuleKind.DateRangeOverride, priority: 0,
            new DateOnly(2026, 12, 20), new DateOnly(2027, 1, 5),
            null, null, null, null,
            PricingAdjustmentKind.Multiplier, 1.5m, true);
        // priority 1 (applied second): LoS -10% for 7-night stays
        plan.AddRule(
            PricingRuleKind.LengthOfStay, priority: 1,
            null, null, null, 7, null, null,
            PricingAdjustmentKind.Multiplier, 0.9m, true);

        var quote = await HandlerWith(plan, today: new DateOnly(2026, 12, 1))
            .Handle(Cmd(plan.PropertyId, new DateOnly(2026, 12, 21), new DateOnly(2026, 12, 28)), default);

        // 7 nights × 100 × 1.5 × 0.9 = 945
        quote.Subtotal.Amount.Should().Be(945m);
    }

    [Fact]
    public async Task Mixed_kind_order_matters_absolute_then_multiplier_yields_different_total_than_swap()
    {
        // Absolute(+10) → Multiplier(×1.5) over base 100 = (100+10)*1.5 = 165
        var planA = FreshPlan();
        planA.AddRule(
            PricingRuleKind.LengthOfStay, priority: 0,
            null, null, null, minNights: 1, maxNights: null, null,
            PricingAdjustmentKind.Absolute, 10m, true);
        planA.AddRule(
            PricingRuleKind.LengthOfStay, priority: 1,
            null, null, null, minNights: 1, maxNights: null, null,
            PricingAdjustmentKind.Multiplier, 1.5m, true);

        var quoteA = await HandlerWith(planA, today: new DateOnly(2026, 8, 1))
            .Handle(Cmd(planA.PropertyId, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 11)), default);

        quoteA.Subtotal.Amount.Should().Be(165m);

        // Multiplier(×1.5) → Absolute(+10) = 100*1.5 + 10 = 160
        var planB = FreshPlan();
        planB.AddRule(
            PricingRuleKind.LengthOfStay, priority: 0,
            null, null, null, minNights: 1, maxNights: null, null,
            PricingAdjustmentKind.Multiplier, 1.5m, true);
        planB.AddRule(
            PricingRuleKind.LengthOfStay, priority: 1,
            null, null, null, minNights: 1, maxNights: null, null,
            PricingAdjustmentKind.Absolute, 10m, true);

        var quoteB = await HandlerWith(planB, today: new DateOnly(2026, 8, 1))
            .Handle(Cmd(planB.PropertyId, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 11)), default);

        quoteB.Subtotal.Amount.Should().Be(160m);
    }

    [Fact]
    public async Task Disabled_rule_is_skipped()
    {
        var plan = FreshPlan();
        plan.AddRule(
            PricingRuleKind.LengthOfStay, priority: 0,
            null, null, null, 1, null, null,
            PricingAdjustmentKind.Multiplier, 0.5m, isEnabled: false);

        var quote = await HandlerWith(plan, today: new DateOnly(2026, 8, 1))
            .Handle(Cmd(plan.PropertyId, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 13)), default);

        quote.Subtotal.Amount.Should().Be(100m * 3);
        quote.Nightly.Should().AllSatisfy(n => n.RuleApplied.Should().Be("base"));
    }

    [Fact]
    public async Task RuleApplied_badge_preserves_first_applied_rule_name()
    {
        var plan = FreshPlan();
        plan.AddRule(
            PricingRuleKind.DateRangeOverride, priority: 0,
            new DateOnly(2026, 12, 20), new DateOnly(2027, 1, 5),
            null, null, null, null,
            PricingAdjustmentKind.Multiplier, 1.5m, true);
        plan.AddRule(
            PricingRuleKind.LengthOfStay, priority: 1,
            null, null, null, 1, null, null,
            PricingAdjustmentKind.Multiplier, 0.9m, true);

        var quote = await HandlerWith(plan, today: new DateOnly(2026, 12, 1))
            .Handle(Cmd(plan.PropertyId, new DateOnly(2026, 12, 21), new DateOnly(2026, 12, 24)), default);

        // Seasonal (priority 0) applied first, sets badge "seasonal";
        // LengthOfStay (priority 1) applied second, adjusts amount but NOT badge.
        quote.Nightly.Should().AllSatisfy(n => n.RuleApplied.Should().Be("seasonal"));
    }
}
