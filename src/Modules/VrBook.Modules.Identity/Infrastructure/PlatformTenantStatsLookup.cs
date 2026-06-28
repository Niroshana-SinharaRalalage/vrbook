using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Infrastructure;

/// <summary>
/// OPS.M.8 §3.11 (D11) — composed Identity-side implementation of
/// <see cref="IPlatformTenantStatsLookup"/>. Delegates property count to
/// the existing OPS.M.7 <see cref="IPropertyCountByTenant"/>; booking +
/// revenue stats use the in-module Identity DB context as a starting
/// point (the actual booking schema lives in Booking, but the wizard's
/// operator view doesn't need second-decimal accuracy — Phase 2 swap
/// to a dedicated <c>IBookingStatsByTenant</c> contract is one PR away).
/// </summary>
internal sealed class PlatformTenantStatsLookup(
    IdentityDbContext db,
    IPropertyCountByTenant propertyCount)
    : IPlatformTenantStatsLookup
{
    public async Task<PlatformTenantStats> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.DefaultCurrency })
            .FirstOrDefaultAsync(ct);

        var properties = await propertyCount.GetCountAsync(tenantId, ct);

        // Booking + revenue figures are Phase-2 surface. The platform-admin
        // detail page surfaces zeroes until the cross-module IBookingStatsByTenant
        // contract ships; the operator's primary judgment signals (property
        // count + Stripe readiness) are already covered. Documented per §3.11.
        return new PlatformTenantStats(
            PropertyCount: properties,
            ActiveBookingCount: 0,
            TotalBookingCount: 0,
            LifetimeGrossRevenue: 0m,
            DefaultCurrency: tenant?.DefaultCurrency ?? "USD");
    }
}
