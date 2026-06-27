using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Pricing.Application.Common;
using VrBook.Modules.Pricing.Infrastructure.Persistence;

namespace VrBook.Modules.Pricing.Application.Plans.Commands;

public sealed record RemovePricingRuleCommand(Guid PropertyId, Guid RuleId, Guid TenantId) : IRequest, ITenantScoped;

internal sealed class RemovePricingRuleHandler(
    IPricingPlanRepository plans,
    PricingDbContext db) : IRequestHandler<RemovePricingRuleCommand>
{
    public async Task Handle(RemovePricingRuleCommand request, CancellationToken cancellationToken)
    {
        // OPS.M.4 Step 3 — PricingAuthorization owner-equality check deleted;
        // TenantAuthorizationBehavior + controller [Authorize(Roles)] cover it.

        var plan = await plans.GetByPropertyIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("PricingPlan", request.PropertyId);

        // Raw SQL DELETE - same EF tracking weirdness contingency as the Add path.
        // Idempotent on unknown id (rows == 0) so the controller still returns 204.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"DELETE FROM pricing.pricing_rules WHERE ""Id"" = {request.RuleId} AND pricing_plan_id = {plan.Id}",
            cancellationToken);
    }
}
