using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Catalog.Infrastructure.Persistence;

internal sealed class PropertyOwnerLookup(CatalogDbContext db) : IPropertyOwnerLookup
{
    public async Task<PropertyOwnerSnapshot?> GetAsync(Guid propertyId, CancellationToken ct = default) =>
        await db.Properties
            .AsNoTracking()
            .Where(p => p.Id == propertyId)
            .Select(p => new PropertyOwnerSnapshot(p.Id, p.OwnerUserId, p.Title))
            .FirstOrDefaultAsync(ct);
}
