using MediatR;
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
    IUnitOfWork uow) : IRequestHandler<ReorderPricingRulesCommand, PricingPlanDto>
{
    public async Task<PricingPlanDto> Handle(ReorderPricingRulesCommand request, CancellationToken cancellationToken)
    {
        await PricingAuthorization.RequireOwnerOrAdminAsync(
            currentUser, ownerLookup, request.PropertyId, cancellationToken);

        var plan = await plans.GetByPropertyIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("PricingPlan", request.PropertyId);

        plan.ReorderRules(request.RuleIds);
        await uow.SaveChangesAsync(cancellationToken);

        return plan.ToDto();
    }
}
