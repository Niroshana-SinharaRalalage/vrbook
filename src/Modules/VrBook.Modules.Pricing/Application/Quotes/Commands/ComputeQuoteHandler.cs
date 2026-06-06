using MediatR;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Domain.Common;
using VrBook.Modules.Pricing.Infrastructure.Persistence;

namespace VrBook.Modules.Pricing.Application.Quotes.Commands;

internal sealed class ComputeQuoteHandler(IPricingPlanRepository plans)
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
        var subtotalAmount = 0m;
        for (var i = 0; i < nights; i++)
        {
            var date = r.Checkin.AddDays(i);
            var isWeekend = date.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;
            var rate = isWeekend && plan.WeekendRate > 0 ? plan.WeekendRate : plan.BaseNightlyRate;
            nightlyLines.Add(new NightlyLineDto(date, new Money(rate, currency), isWeekend ? "weekend" : "base"));
            subtotalAmount += rate;
        }

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

        // A3 v1: no loyalty discount, no dynamic adjustments. A3.1 work.
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
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(15));
    }
}
