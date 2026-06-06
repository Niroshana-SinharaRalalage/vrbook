using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Pricing.Application.Common;
using VrBook.Modules.Pricing.Infrastructure.Persistence;

namespace VrBook.Modules.Pricing.Application.Plans.Queries;

internal sealed class GetPricingPlanHandler(IPricingPlanRepository plans)
    : IRequestHandler<GetPricingPlanQuery, PricingPlanDto?>
{
    public async Task<PricingPlanDto?> Handle(GetPricingPlanQuery request, CancellationToken cancellationToken)
    {
        var p = await plans.GetByPropertyIdAsync(request.PropertyId, cancellationToken);
        return p?.ToDto();
    }
}
