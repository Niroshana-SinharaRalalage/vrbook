using MediatR;
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
    IUnitOfWork uow) : IRequestHandler<SetPricingRuleEnabledCommand, PricingRuleDto>
{
    public async Task<PricingRuleDto> Handle(SetPricingRuleEnabledCommand request, CancellationToken cancellationToken)
    {
        await PricingAuthorization.RequireOwnerOrAdminAsync(
            currentUser, ownerLookup, request.PropertyId, cancellationToken);

        var plan = await plans.GetByPropertyIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("PricingPlan", request.PropertyId);

        var rule = plan.Rules.FirstOrDefault(r => r.Id == request.RuleId)
            ?? throw new NotFoundException("PricingRule", request.RuleId);

        plan.SetRuleEnabled(request.RuleId, request.IsEnabled);
        await uow.SaveChangesAsync(cancellationToken);

        return new PricingRuleDto(
            rule.Id, rule.Kind, rule.Priority, rule.StartDate, rule.EndDate,
            rule.DayOfWeekMask, rule.MinNights, rule.MaxNights, rule.DaysBeforeCheckin,
            rule.AdjustmentKind, rule.AdjustmentValue, rule.IsEnabled);
    }
}
