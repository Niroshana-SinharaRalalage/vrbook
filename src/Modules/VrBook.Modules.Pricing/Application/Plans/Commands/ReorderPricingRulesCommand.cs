using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Pricing.Application.Common;
using VrBook.Modules.Pricing.Infrastructure.Persistence;

namespace VrBook.Modules.Pricing.Application.Plans.Commands;

public sealed record ReorderPricingRulesCommand(Guid PropertyId, IReadOnlyList<Guid> RuleIds, Guid TenantId)
    : IRequest<PricingPlanDto>, ITenantScoped;

internal sealed class ReorderPricingRulesHandler(
    IPricingPlanRepository plans,
    PricingDbContext db) : IRequestHandler<ReorderPricingRulesCommand, PricingPlanDto>
{
    public async Task<PricingPlanDto> Handle(ReorderPricingRulesCommand request, CancellationToken cancellationToken)
    {
        // OPS.M.4 Step 3 — PricingAuthorization owner-equality check deleted;
        // TenantAuthorizationBehavior + controller [Authorize(Roles)] cover it.

        var plan = await plans.GetByPropertyIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("PricingPlan", request.PropertyId);

        // Validate via the aggregate (throws on size mismatch / unknown ids),
        // then UPDATE each priority via raw SQL per SLICE6 §2.9 contingency.
        plan.ReorderRules(request.RuleIds);
        for (var i = 0; i < request.RuleIds.Count; i++)
        {
            var rid = request.RuleIds[i];
            var pri = i;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE pricing.pricing_rules SET priority = {pri} WHERE ""Id"" = {rid} AND pricing_plan_id = {plan.Id}",
                cancellationToken);
        }

        var fresh = await plans.GetByPropertyIdAsync(request.PropertyId, cancellationToken)
            ?? throw new InvalidOperationException("Plan disappeared after reorder.");
        return fresh.ToDto();
    }
}
