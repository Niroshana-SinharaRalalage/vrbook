using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Pricing.Application.Common;
using VrBook.Modules.Pricing.Infrastructure.Persistence;

namespace VrBook.Modules.Pricing.Application.Plans.Commands;

public sealed record RemovePricingRuleCommand(Guid PropertyId, Guid RuleId) : IRequest;

internal sealed class RemovePricingRuleHandler(
    ICurrentUser currentUser,
    IPropertyOwnerLookup ownerLookup,
    IPricingPlanRepository plans,
    PricingDbContext db) : IRequestHandler<RemovePricingRuleCommand>
{
    public async Task Handle(RemovePricingRuleCommand request, CancellationToken cancellationToken)
    {
        await PricingAuthorization.RequireOwnerOrAdminAsync(
            currentUser, ownerLookup, request.PropertyId, cancellationToken);

        var plan = await plans.GetByPropertyIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("PricingPlan", request.PropertyId);

        // Raw SQL DELETE - same EF tracking weirdness contingency as the Add path.
        // Idempotent on unknown id (rows == 0) so the controller still returns 204.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"DELETE FROM pricing.pricing_rules WHERE ""Id"" = {request.RuleId} AND pricing_plan_id = {plan.Id}",
            cancellationToken);
    }
}
