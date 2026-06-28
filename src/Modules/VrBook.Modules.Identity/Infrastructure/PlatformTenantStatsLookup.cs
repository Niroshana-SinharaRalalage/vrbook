using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Persistence;
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
///
/// <para>OPS.M.9 §7.2 — both the local <c>Tenants</c> read AND the
/// cross-module <see cref="IPropertyCountByTenant"/> call run inside an
/// <see cref="RlsBypassScope"/> so the per-statement interceptor stamps
/// <c>app.is_platform_admin = 'true'</c>. Without this, the property count
/// would return the caller's OWN tenant's properties (the request-scoped
/// <c>CatalogDbContext</c> would stamp <c>app.tenant_id</c> = caller's id
/// and RLS would filter to that tenant) — the section 7.2 pitfall.</para>
/// </summary>
internal sealed class PlatformTenantStatsLookup(
    IdentityDbContext db,
    IPropertyCountByTenant propertyCount,
    ILogger<PlatformTenantStatsLookup> logger)
    : IPlatformTenantStatsLookup
{
    public async Task<PlatformTenantStats> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        logger.LogInformation(
            "RLS bypass open reason=platform-tenant-stats caller=PlatformTenantStatsLookup target_tenant={TargetTenantId}",
            tenantId);
        using var bypass = RlsBypassScope.Enter();

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
