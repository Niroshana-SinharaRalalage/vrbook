using FluentAssertions;
using NSubstitute;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Domain.Common;
using VrBook.Modules.Pricing.Application.Quotes.Commands;
using VrBook.Modules.Pricing.Domain;
using VrBook.Modules.Pricing.Infrastructure.Persistence;
using Xunit;

namespace VrBook.Api.IntegrationTests.Domain;

/// <summary>
/// Unit tests for the A3 ComputeQuoteHandler. Exercises weekend/base rate selection,
/// fee math (PerStay / PerNight / PerGuest / Percentage), min/max stay guards,
/// and date-range / guest validation. Repository is NSubstitute-mocked.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ComputeQuoteHandlerTests
{
    private static PricingPlan PlanFor(
        Guid propertyId,
        decimal baseRate = 100m,
        decimal weekendRate = 150m,
        int minStay = 1,
        int maxStay = 30,
        IEnumerable<(FeeKind kind, decimal amount, FeeBasis basis, int? freeThreshold, string label)>? fees = null)
    {
        var p = PricingPlan.Create(propertyId, baseRate, "USD");
        p.Replace(baseRate, weekendRate, "USD", minStay, maxStay, dynamicEnabled: false,
            fees ?? []);
        return p;
    }

    private static ComputeQuoteHandler HandlerWith(PricingPlan plan)
    {
        var repo = Substitute.For<IPricingPlanRepository>();
        repo.GetByPropertyIdAsync(plan.PropertyId, Arg.Any<CancellationToken>())
            .Returns(plan);
        return new ComputeQuoteHandler(repo);
    }

    [Fact]
    public async Task Three_midweek_nights_all_use_base_rate()
    {
        // 2026-08-04 = Tuesday, so checkin Tue thru Thu (no weekend)
        var propertyId = Guid.NewGuid();
        var plan = PlanFor(propertyId, baseRate: 100m, weekendRate: 150m);
        var cmd = new ComputeQuoteCommand(propertyId,
            new QuoteRequest(new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 7), 2, false));

        var quote = await HandlerWith(plan).Handle(cmd, default);

        quote.Nightly.Should().HaveCount(3);
        quote.Nightly.Should().AllSatisfy(n => n.RuleApplied.Should().Be("base"));
        quote.Subtotal.Amount.Should().Be(300m);
    }

    [Fact]
    public async Task Friday_and_saturday_nights_use_weekend_rate()
    {
        // 2026-08-07 = Friday, 2026-08-08 = Saturday, 2026-08-09 = Sunday
        var propertyId = Guid.NewGuid();
        var plan = PlanFor(propertyId, baseRate: 100m, weekendRate: 150m);
        var cmd = new ComputeQuoteCommand(propertyId,
            new QuoteRequest(new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 10), 2, false));

        var quote = await HandlerWith(plan).Handle(cmd, default);

        quote.Nightly.Should().HaveCount(3);
        quote.Nightly.Select(n => n.RuleApplied).Should().Equal("weekend", "weekend", "base");
        quote.Subtotal.Amount.Should().Be(150m + 150m + 100m);
    }

    [Fact]
    public async Task Weekend_rate_zero_falls_back_to_base()
    {
        var propertyId = Guid.NewGuid();
        var plan = PlanFor(propertyId, baseRate: 100m, weekendRate: 0m);
        var cmd = new ComputeQuoteCommand(propertyId,
            new QuoteRequest(new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 10), 2, false));

        var quote = await HandlerWith(plan).Handle(cmd, default);

        quote.Subtotal.Amount.Should().Be(300m);
    }

    [Fact]
    public async Task PerStay_fee_added_once()
    {
        var propertyId = Guid.NewGuid();
        var plan = PlanFor(propertyId, fees: [(FeeKind.Cleaning, 50m, FeeBasis.PerStay, null, "Cleaning")]);
        var cmd = new ComputeQuoteCommand(propertyId,
            new QuoteRequest(new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 7), 2, false));

        var quote = await HandlerWith(plan).Handle(cmd, default);

        quote.Fees.Should().ContainSingle(f => f.Kind == FeeKind.Cleaning && f.Amount.Amount == 50m);
        quote.Total.Amount.Should().Be(300m + 50m);
    }

    [Fact]
    public async Task PerNight_fee_multiplies_by_night_count()
    {
        var propertyId = Guid.NewGuid();
        var plan = PlanFor(propertyId, fees: [(FeeKind.ExtraGuest, 10m, FeeBasis.PerNight, null, "Pet fee")]);
        var cmd = new ComputeQuoteCommand(propertyId,
            new QuoteRequest(new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 9), 2, false));

        var quote = await HandlerWith(plan).Handle(cmd, default);

        quote.Fees.Should().ContainSingle(f => f.Amount.Amount == 50m); // 10 * 5 nights
    }

    [Fact]
    public async Task PerGuest_fee_multiplies_by_guest_count()
    {
        var propertyId = Guid.NewGuid();
        var plan = PlanFor(propertyId, fees: [(FeeKind.ExtraGuest, 5m, FeeBasis.PerGuest, null, "Linen")]);
        var cmd = new ComputeQuoteCommand(propertyId,
            new QuoteRequest(new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 7), 4, false));

        var quote = await HandlerWith(plan).Handle(cmd, default);

        quote.Fees.Should().ContainSingle(f => f.Amount.Amount == 20m); // 5 * 4 guests
    }

    [Fact]
    public async Task Percentage_fee_rounds_to_two_decimals_away_from_zero()
    {
        // 12% of 333 = 39.96
        var propertyId = Guid.NewGuid();
        var plan = PlanFor(propertyId, baseRate: 111m,
            fees: [(FeeKind.Tax, 12m, FeeBasis.Percentage, null, "Lodging tax")]);
        var cmd = new ComputeQuoteCommand(propertyId,
            new QuoteRequest(new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 7), 2, false));

        var quote = await HandlerWith(plan).Handle(cmd, default);

        quote.Fees.Should().ContainSingle(f => f.Kind == FeeKind.Tax && f.Amount.Amount == 39.96m);
        quote.Taxes.Amount.Should().Be(39.96m);
    }

    [Fact]
    public async Task Total_equals_subtotal_plus_cleaning_plus_taxes()
    {
        var propertyId = Guid.NewGuid();
        var plan = PlanFor(propertyId, baseRate: 100m,
            fees: [
                (FeeKind.Cleaning, 50m, FeeBasis.PerStay, null, "Cleaning"),
                (FeeKind.Tax, 10m, FeeBasis.Percentage, null, "Tax"),
            ]);
        var cmd = new ComputeQuoteCommand(propertyId,
            new QuoteRequest(new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 7), 2, false));

        var quote = await HandlerWith(plan).Handle(cmd, default);

        // subtotal 300 + cleaning 50 + 10% of 300 = 30
        quote.Total.Amount.Should().Be(380m);
    }

    [Fact]
    public async Task Min_stay_violation_throws_BusinessRuleViolation()
    {
        var propertyId = Guid.NewGuid();
        var plan = PlanFor(propertyId, minStay: 3);
        var cmd = new ComputeQuoteCommand(propertyId,
            new QuoteRequest(new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 6), 2, false));

        Func<Task> act = () => HandlerWith(plan).Handle(cmd, default);
        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .Where(e => e.Rule == "quote.min_stay");
    }

    [Fact]
    public async Task Max_stay_violation_throws_BusinessRuleViolation()
    {
        var propertyId = Guid.NewGuid();
        var plan = PlanFor(propertyId, maxStay: 2);
        var cmd = new ComputeQuoteCommand(propertyId,
            new QuoteRequest(new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 7), 2, false));

        Func<Task> act = () => HandlerWith(plan).Handle(cmd, default);
        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .Where(e => e.Rule == "quote.max_stay");
    }

    [Fact]
    public async Task Checkout_before_checkin_throws()
    {
        var propertyId = Guid.NewGuid();
        var plan = PlanFor(propertyId);
        var cmd = new ComputeQuoteCommand(propertyId,
            new QuoteRequest(new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 4), 2, false));

        Func<Task> act = () => HandlerWith(plan).Handle(cmd, default);
        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .Where(e => e.Rule == "quote.date_range");
    }

    [Fact]
    public async Task Zero_guests_throws()
    {
        var propertyId = Guid.NewGuid();
        var plan = PlanFor(propertyId);
        var cmd = new ComputeQuoteCommand(propertyId,
            new QuoteRequest(new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 7), 0, false));

        Func<Task> act = () => HandlerWith(plan).Handle(cmd, default);
        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .Where(e => e.Rule == "quote.guests");
    }

    [Fact]
    public async Task Unknown_property_throws_NotFound()
    {
        var propertyId = Guid.NewGuid();
        var repo = Substitute.For<IPricingPlanRepository>();
        repo.GetByPropertyIdAsync(propertyId, Arg.Any<CancellationToken>())
            .Returns((PricingPlan?)null);
        var handler = new ComputeQuoteHandler(repo);
        var cmd = new ComputeQuoteCommand(propertyId,
            new QuoteRequest(new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 7), 2, false));

        Func<Task> act = () => handler.Handle(cmd, default);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Quote_expires_in_about_15_minutes()
    {
        var propertyId = Guid.NewGuid();
        var plan = PlanFor(propertyId);
        var cmd = new ComputeQuoteCommand(propertyId,
            new QuoteRequest(new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 7), 2, false));

        var quote = await HandlerWith(plan).Handle(cmd, default);

        var diff = (quote.ExpiresAt - DateTimeOffset.UtcNow).TotalMinutes;
        diff.Should().BeInRange(14.5, 15.5);
    }
}
