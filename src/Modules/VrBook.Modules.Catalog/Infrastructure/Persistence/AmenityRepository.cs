using Microsoft.EntityFrameworkCore;
using VrBook.Modules.Catalog.Domain;

namespace VrBook.Modules.Catalog.Infrastructure.Persistence;

internal sealed class AmenityRepository(CatalogDbContext db) : IAmenityRepository
{
    public async Task<IReadOnlyList<Amenity>> ListAsync(CancellationToken ct = default) =>
        await db.Amenities.OrderBy(a => a.Category).ThenBy(a => a.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<Amenity>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var arr = ids.Distinct().ToArray();
        return await db.Amenities.Where(a => arr.Contains(a.Id)).ToListAsync(ct);
    }
}
