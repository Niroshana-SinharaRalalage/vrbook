using MediatR;
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
    IUnitOfWork uow) : IRequestHandler<RemovePricingRuleCommand>
{
    public async Task Handle(RemovePricingRuleCommand request, CancellationToken cancellationToken)
    {
        await PricingAuthorization.RequireOwnerOrAdminAsync(
            currentUser, ownerLookup, request.PropertyId, cancellationToken);

        var plan = await plans.GetByPropertyIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("PricingPlan", request.PropertyId);

        plan.RemoveRule(request.RuleId);
        await uow.SaveChangesAsync(cancellationToken);
    }
}
