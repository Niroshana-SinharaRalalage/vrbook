using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Pricing.Application.Common;
using VrBook.Modules.Pricing.Infrastructure.Persistence;

namespace VrBook.Modules.Pricing.Application.Plans.Commands;

public sealed record SetPricingRuleEnabledCommand(Guid PropertyId, Guid RuleId, bool IsEnabled)
    : IRequest<PricingRuleDto>;

internal sealed class SetPricingRuleEnabledHandler(
    ICurrentUser currentUser,
    IPropertyOwnerLookup ownerLookup,
    IPricingPlanRepository plans,
    PricingDbContext db) : IRequestHandler<SetPricingRuleEnabledCommand, PricingRuleDto>
{
    public async Task<PricingRuleDto> Handle(SetPricingRuleEnabledCommand request, CancellationToken cancellationToken)
    {
        await PricingAuthorization.RequireOwnerOrAdminAsync(
            currentUser, ownerLookup, request.PropertyId, cancellationToken);

        var plan = await plans.GetByPropertyIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("PricingPlan", request.PropertyId);

        var rule = plan.Rules.FirstOrDefault(r => r.Id == request.RuleId)
            ?? throw new NotFoundException("PricingRule", request.RuleId);

        // Raw SQL UPDATE - same SLICE6 §2.9 contingency. Does NOT raise an event.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE pricing.pricing_rules SET is_enabled = {request.IsEnabled} WHERE ""Id"" = {request.RuleId} AND pricing_plan_id = {plan.Id}",
            cancellationToken);

        return new PricingRuleDto(
            rule.Id, rule.Kind, rule.Priority, rule.StartDate, rule.EndDate,
            rule.DayOfWeekMask, rule.MinNights, rule.MaxNights, rule.DaysBeforeCheckin,
            rule.AdjustmentKind, rule.AdjustmentValue, request.IsEnabled);
    }
}
