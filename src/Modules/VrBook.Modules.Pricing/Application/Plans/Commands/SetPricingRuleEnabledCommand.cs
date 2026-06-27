using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Pricing.Application.Common;
using VrBook.Modules.Pricing.Infrastructure.Persistence;

namespace VrBook.Modules.Pricing.Application.Plans.Commands;

public sealed record SetPricingRuleEnabledCommand(Guid PropertyId, Guid RuleId, bool IsEnabled, Guid TenantId)
    : IRequest<PricingRuleDto>, ITenantScoped;

internal sealed class SetPricingRuleEnabledHandler(
    IPricingPlanRepository plans,
    PricingDbContext db) : IRequestHandler<SetPricingRuleEnabledCommand, PricingRuleDto>
{
    public async Task<PricingRuleDto> Handle(SetPricingRuleEnabledCommand request, CancellationToken cancellationToken)
    {
        // OPS.M.4 Step 3 — PricingAuthorization owner-equality check deleted;
        // TenantAuthorizationBehavior + controller [Authorize(Roles)] cover it.

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
