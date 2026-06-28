using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Modules.Catalog.Infrastructure;

/// <summary>
/// OPS.M.7 §4.2 — Catalog-side implementation of
/// <see cref="IPropertyCountByTenant"/>. Single tenant-scoped Count
/// against <c>catalog.properties</c>; soft-delete is not currently
/// modelled on the Property aggregate so we count all rows the tenant
/// owns. If a Phase-2 soft-delete column lands, add the filter here.
/// </summary>
internal sealed class PropertyCountByTenant(CatalogDbContext db) : IPropertyCountByTenant
{
    public Task<int> GetCountAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Properties.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .CountAsync(ct);
}
