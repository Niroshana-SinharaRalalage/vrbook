using VrBook.Modules.Pricing.Domain;

namespace VrBook.Modules.Pricing.Infrastructure.Persistence;

public interface IPricingPlanRepository
{
    Task<PricingPlan?> GetByPropertyIdAsync(Guid propertyId, CancellationToken cancellationToken = default);
    Task AddAsync(PricingPlan plan, CancellationToken cancellationToken = default);
}
