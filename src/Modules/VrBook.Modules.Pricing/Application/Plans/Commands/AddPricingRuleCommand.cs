using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Pricing.Application.Common;
using VrBook.Modules.Pricing.Infrastructure.Persistence;

namespace VrBook.Modules.Pricing.Application.Plans.Commands;

public sealed record AddPricingRuleCommand(Guid PropertyId, CreatePricingRuleRequest Request, Guid TenantId)
    : IRequest<PricingRuleDto>, ITenantScoped;

internal sealed class AddPricingRuleHandler(
    IPricingPlanRepository plans,
    PricingDbContext db) : IRequestHandler<AddPricingRuleCommand, PricingRuleDto>
{
    public async Task<PricingRuleDto> Handle(AddPricingRuleCommand request, CancellationToken cancellationToken)
    {
        // OPS.M.4 Step 3 — PricingAuthorization owner-equality check deleted;
        // TenantAuthorizationBehavior + controller [Authorize(Roles)] cover it.

        var plan = await plans.GetByPropertyIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("PricingPlan", request.PropertyId);

        var r = request.Request ?? throw new ArgumentException("Request body is required.", nameof(request));

        // Validate per-kind invariants via the aggregate (throws on §2.4.1 reject).
        // The entity is then persisted via raw SQL INSERT because EF tracking
        // silently no-ops on the new rule row - same SLICE6_PLAN §2.9
        // contingency that UpdatePricingPlanHandler uses for fees.
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
