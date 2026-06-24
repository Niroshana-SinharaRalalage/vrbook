using VrBook.Contracts.Dtos;
using VrBook.Modules.Pricing.Domain;

namespace VrBook.Modules.Pricing.Application.Common;

internal static class PricingMapping
{
    public static PricingPlanDto ToDto(this PricingPlan p) =>
        new(
            Id: p.Id,
            PropertyId: p.PropertyId,
            BaseNightlyRate: p.BaseNightlyRate,
            WeekendRate: p.WeekendRate,
            Currency: p.Currency,
            MinStayNights: p.MinStayNights,
            MaxStayNights: p.MaxStayNights,
            DynamicEnabled: p.DynamicEnabled,
            Rules: p.Rules
                .OrderBy(r => r.Priority)
                .Select(r => new PricingRuleDto(
                    r.Id,
                    r.Kind,
                    r.Priority,
                    r.StartDate,
                    r.EndDate,
                    r.DayOfWeekMask,
                    r.MinNights,
                    r.MaxNights,
                    r.DaysBeforeCheckin,
                    r.AdjustmentKind,
                    r.AdjustmentValue,
                    r.IsEnabled))
                .ToArray(),
            Fees: p.Fees.Select(f => new FeeDto(f.Id, f.Kind, f.Amount, f.Basis, f.FreeThreshold, f.Label)).ToArray());
}
