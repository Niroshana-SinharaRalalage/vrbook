using MediatR;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Pricing.Infrastructure.Persistence;
using PricingRule = VrBook.Modules.Pricing.Domain.PricingRule;

namespace VrBook.Modules.Pricing.Application.Quotes.Commands;

internal sealed class ComputeQuoteHandler(IPricingPlanRepository plans, IDateTimeProvider clock)
    : IRequestHandler<ComputeQuoteCommand, QuoteDto>
{
    public async Task<QuoteDto> Handle(ComputeQuoteCommand request, CancellationToken cancellationToken)
    {
        var r = request.Request ?? throw new ArgumentException("Request body is required.", nameof(request));
        if (r.Checkout <= r.Checkin)
        {
            throw new BusinessRuleViolationException("quote.date_range", "Checkout must be after checkin.");
        }
        if (r.Guests < 1)
        {
            throw new BusinessRuleViolationException("quote.guests", "Guests must be at least 1.");
        }

        var plan = await plans.GetByPropertyIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("PricingPlan", request.PropertyId);

        var nights = (r.Checkout.ToDateTime(TimeOnly.MinValue) - r.Checkin.ToDateTime(TimeOnly.MinValue)).Days;
        if (nights < plan.MinStayNights)
        {
            throw new BusinessRuleViolationException("quote.min_stay", $"Minimum stay is {plan.MinStayNights} nights.");
        }
        if (nights > plan.MaxStayNights)
        {
            throw new BusinessRuleViolationException("quote.max_stay", $"Maximum stay is {plan.MaxStayNights} nights.");
        }

        var currency = plan.Currency;

        // Build per-night lines. Friday + Saturday nights use WeekendRate.
        var nightlyLines = new List<NightlyLineDto>();
        for (var i = 0; i < nights; i++)
        {
            var date = r.Checkin.AddDays(i);
            var isWeekend = date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;
            var rate = isWeekend && plan.WeekendRate > 0 ? plan.WeekendRate : plan.BaseNightlyRate;
            nightlyLines.Add(new NightlyLineDto(date, new Money(rate, currency), isWeekend ? "weekend" : "base"));
        }

        // Apply enabled rules in priority ascending (lower number first) — see
        // docs/SLICE6_PLAN.md §2.4 + §2.4.1. Order matters whenever Absolute or
        // Override appears in the stack; that's a contract, not an accident.
        foreach (var rule in plan.Rules.Where(rl => rl.IsEnabled).OrderBy(rl => rl.Priority))
        {
            ApplyRule(nightlyLines, rule, r, clock, currency, nights);
        }

        var subtotalAmount = nightlyLines.Sum(n => n.Amount.Amount);

        // Apply fees.
        var feeLines = new List<FeeLineDto>();
        var taxesAmount = 0m;
        var cleaningAmount = 0m;
        foreach (var f in plan.Fees)
        {
            var amount = f.Basis switch
            {
                FeeBasis.PerStay => f.Amount,
                FeeBasis.PerNight => f.Amount * nights,
                FeeBasis.PerGuest => f.Amount * r.Guests,
                FeeBasis.Percentage => decimal.Round(subtotalAmount * (f.Amount / 100m), 2, MidpointRounding.AwayFromZero),
                _ => 0m,
            };
            if (amount <= 0m)
            {
                continue;
            }
            feeLines.Add(new FeeLineDto(f.Label, f.Kind, new Money(amount, currency)));
            if (f.Kind == FeeKind.Tax)
            {
                taxesAmount += amount;
            }
            else if (f.Kind == FeeKind.Cleaning)
            {
                cleaningAmount += amount;
            }
        }

        var discountAmount = 0m;
        var totalAmount = subtotalAmount + cleaningAmount + taxesAmount - discountAmount;

        return new QuoteDto(
            PropertyId: request.PropertyId,
            Range: new DateRange(r.Checkin, r.Checkout),
            Guests: r.Guests,
            Nightly: nightlyLines,
            Fees: feeLines,
            Subtotal: new Money(subtotalAmount, currency),
            Discount: new Money(discountAmount, currency),
            Taxes: new Money(taxesAmount, currency),
            Total: new Money(totalAmount, currency),
            ExpiresAt: clock.UtcNow.AddMinutes(15));
    }

    /// <summary>
    /// Mutates <paramref name="nights"/> per the §2.4.1 matrix.
    /// <list type="bullet">
    ///   <item><c>DateRangeOverride</c> applies per-night within [StartDate, EndDate].</item>
    ///   <item><c>LastMinute</c> applies to every night when (Checkin - Today) ≤ DaysBeforeCheckin.</item>
    ///   <item><c>LengthOfStay</c> applies to every night when MinNights ≤ Nights and (MaxNights null or Nights ≤ MaxNights).</item>
    /// </list>
    /// The <c>RuleApplied</c> badge is preserved at the first (highest-priority)
    /// applied rule's short name — subsequent rules that also touch a night
    /// adjust the amount but don't rewrite the badge.
    /// </summary>
    private static void ApplyRule(
        List<NightlyLineDto> nights,
        PricingRule rule,
        QuoteRequest req,
        IDateTimeProvider clock,
        string currency,
        int totalNights)
    {
        var wholeStayApplies = rule.Kind switch
        {
            PricingRuleKind.LastMinute =>
                (req.Checkin.DayNumber - clock.Today.DayNumber) <= rule.DaysBeforeCheckin!.Value,
            PricingRuleKind.LengthOfStay =>
                totalNights >= rule.MinNights!.Value
                && (rule.MaxNights is null || totalNights <= rule.MaxNights.Value),
            _ => false,
        };

        var label = ShortName(rule.Kind);

        for (var i = 0; i < nights.Count; i++)
        {
            var night = nights[i];
            var applies = rule.Kind switch
            {
                PricingRuleKind.DateRangeOverride =>
                    night.Date >= rule.StartDate!.Value && night.Date <= rule.EndDate!.Value,
                PricingRuleKind.LastMinute or PricingRuleKind.LengthOfStay => wholeStayApplies,
                _ => throw new BusinessRuleViolationException(
                    "quote.invalid_rule",
                    $"Rule kind {rule.Kind} is not handled by the quote engine."),
            };

            if (!applies)
            {
                continue;
            }

            var newAmount = rule.AdjustmentKind switch
            {
                PricingAdjustmentKind.Multiplier => night.Amount.Amount * rule.AdjustmentValue,
                PricingAdjustmentKind.Absolute => night.Amount.Amount + rule.AdjustmentValue,
                PricingAdjustmentKind.Override => rule.AdjustmentValue,
                _ => throw new BusinessRuleViolationException(
                    "quote.invalid_rule",
                    $"Unknown adjustment kind {rule.AdjustmentKind}."),
            };

            // Keep the highest-priority applied rule's name on the badge.
            // Lower priority number wins, and we iterate ascending, so the
            // FIRST rule to touch this night sets the badge — later rules
            // adjust the amount but leave the label alone.
            var newBadge = night.RuleApplied is "base" or "weekend"
                ? label
                : night.RuleApplied;

            nights[i] = new NightlyLineDto(night.Date, new Money(newAmount, currency), newBadge);
        }
    }

    private static string ShortName(PricingRuleKind kind) => kind switch
    {
        PricingRuleKind.DateRangeOverride => "seasonal",
        PricingRuleKind.LastMinute => "last_minute",
        PricingRuleKind.LengthOfStay => "length_of_stay",
        _ => "rule",
    };
}
