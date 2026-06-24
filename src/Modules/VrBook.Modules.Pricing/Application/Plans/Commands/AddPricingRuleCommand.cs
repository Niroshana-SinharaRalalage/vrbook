using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Pricing.Application.Common;
using VrBook.Modules.Pricing.Infrastructure.Persistence;

namespace VrBook.Modules.Pricing.Application.Plans.Commands;

public sealed record AddPricingRuleCommand(Guid PropertyId, CreatePricingRuleRequest Request)
    : IRequest<PricingRuleDto>;

internal sealed class AddPricingRuleHandler(
    ICurrentUser currentUser,
    IPropertyOwnerLookup ownerLookup,
    IPricingPlanRepository plans,
    IUnitOfWork uow) : IRequestHandler<AddPricingRuleCommand, PricingRuleDto>
{
    public async Task<PricingRuleDto> Handle(AddPricingRuleCommand request, CancellationToken cancellationToken)
    {
        await PricingAuthorization.RequireOwnerOrAdminAsync(
            currentUser, ownerLookup, request.PropertyId, cancellationToken);

        var plan = await plans.GetByPropertyIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("PricingPlan", request.PropertyId);

        var r = request.Request ?? throw new ArgumentException("Request body is required.", nameof(request));
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

        await uow.SaveChangesAsync(cancellationToken);
        return new PricingRuleDto(
            rule.Id, rule.Kind, rule.Priority, rule.StartDate, rule.EndDate,
            rule.DayOfWeekMask, rule.MinNights, rule.MaxNights, rule.DaysBeforeCheckin,
            rule.AdjustmentKind, rule.AdjustmentValue, rule.IsEnabled);
    }
}
