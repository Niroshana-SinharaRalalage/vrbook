using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Pricing.Application.Common;
using VrBook.Modules.Pricing.Infrastructure.Persistence;

namespace VrBook.Modules.Pricing.Application.Plans.Commands;

public sealed record UpdatePricingRuleCommand(Guid PropertyId, Guid RuleId, CreatePricingRuleRequest Request, Guid TenantId)
    : IRequest<PricingRuleDto>, ITenantScoped;

internal sealed class UpdatePricingRuleHandler(
    IPricingPlanRepository plans,
    PricingDbContext db) : IRequestHandler<UpdatePricingRuleCommand, PricingRuleDto>
{
    public async Task<PricingRuleDto> Handle(UpdatePricingRuleCommand request, CancellationToken cancellationToken)
    {
        // OPS.M.4 Step 3 — PricingAuthorization owner-equality check deleted;
        // TenantAuthorizationBehavior + controller [Authorize(Roles)] cover it.

        var plan = await plans.GetByPropertyIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("PricingPlan", request.PropertyId);

        if (plan.Rules.All(rl => rl.Id != request.RuleId))
        {
            throw new NotFoundException("PricingRule", request.RuleId);
        }

        // Validate via the aggregate (throws on §2.4.1) - the resulting rule
        // entity is then persisted via raw SQL DELETE + INSERT (same SLICE6
        // §2.9 contingency as Add/Remove).
        var r = request.Request ?? throw new ArgumentException("Request body is required.", nameof(request));
        plan.RemoveRule(request.RuleId);
        var rule = plan.AddRule(
            kind: r.Kind,
            priority: r.Priority,
            startDate: r.StartDate,
            endDate: r.EndDate,
            dayOfWeekMask: r.DayOfWeekMask,
            minNights: r.MinNights,
            maxNights: r.MaxNights,
            daysBeforeCheckin: r.DaysBeforeCheckin,
            adjustmentKind: r.AdjustmentKind,
            adjustmentValue: r.AdjustmentValue,
            isEnabled: r.IsEnabled);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"DELETE FROM pricing.pricing_rules WHERE ""Id"" = {request.RuleId} AND pricing_plan_id = {plan.Id}",
            cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO pricing.pricing_rules (
  ""Id"", pricing_plan_id, kind, priority,
  start_date, end_date,
  day_of_week_mask, min_nights, max_nights, days_before_checkin,
  adjustment_kind, adjustment_value, is_enabled
) VALUES (
  {rule.Id}, {plan.Id}, {rule.Kind.ToString()}, {rule.Priority},
  {rule.StartDate}, {rule.EndDate},
  {rule.DayOfWeekMask}, {rule.MinNights}, {rule.MaxNights}, {rule.DaysBeforeCheckin},
  {rule.AdjustmentKind.ToString()}, {rule.AdjustmentValue}, {rule.IsEnabled}
)", cancellationToken);

        return new PricingRuleDto(
            rule.Id, rule.Kind, rule.Priority, rule.StartDate, rule.EndDate,
            rule.DayOfWeekMask, rule.MinNights, rule.MaxNights, rule.DaysBeforeCheckin,
            rule.AdjustmentKind, rule.AdjustmentValue, rule.IsEnabled);
    }
}
