using Microsoft.EntityFrameworkCore;
using VrBook.Modules.Pricing.Domain;

namespace VrBook.Modules.Pricing.Infrastructure.Persistence;

internal sealed class PricingPlanRepository(PricingDbContext db) : IPricingPlanRepository
{
    public Task<PricingPlan?> GetByPropertyIdAsync(Guid propertyId, CancellationToken cancellationToken = default) =>
        db.PricingPlans
            .Include(p => p.Fees)
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.PropertyId == propertyId, cancellationToken);

    public Task AddAsync(PricingPlan plan, CancellationToken cancellationToken = default)
    {
        db.PricingPlans.Add(plan);
        return Task.CompletedTask;
    }
}
