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
            Rules: Array.Empty<PricingRuleDto>(),
            Fees: p.Fees.Select(f => new FeeDto(f.Id, f.Kind, f.Amount, f.Basis, f.FreeThreshold, f.Label)).ToArray());
}
