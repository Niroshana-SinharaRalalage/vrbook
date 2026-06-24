using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Pricing.Application.Common;
using VrBook.Modules.Pricing.Infrastructure.Persistence;

namespace VrBook.Modules.Pricing.Application.Plans.Commands;

public sealed record ReorderPricingRulesCommand(Guid PropertyId, IReadOnlyList<Guid> RuleIds)
    : IRequest<PricingPlanDto>;

internal sealed class ReorderPricingRulesHandler(
    ICurrentUser currentUser,
    IPropertyOwnerLookup ownerLookup,
    IPricingPlanRepository plans,
    PricingDbContext db) : IRequestHandler<ReorderPricingRulesCommand, PricingPlanDto>
{
    public async Task<PricingPlanDto> Handle(ReorderPricingRulesCommand request, CancellationToken cancellationToken)
    {
        await PricingAuthorization.RequireOwnerOrAdminAsync(
            currentUser, ownerLookup, request.PropertyId, cancellationToken);

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
