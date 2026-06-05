using Microsoft.EntityFrameworkCore;
using VrBook.Modules.Catalog.Domain;

namespace VrBook.Modules.Catalog.Infrastructure.Persistence;

internal sealed class PropertyRepository(CatalogDbContext db) : IPropertyRepository
{
    public Task<Property?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Properties
            .Include(p => p.Images)
            .Include(p => p.HouseRules)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Property?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        db.Properties
            .Include(p => p.Images)
            .Include(p => p.HouseRules)
            .FirstOrDefaultAsync(p => p.Slug == slug, ct);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) =>
        db.Properties.AnyAsync(p => p.Slug == slug, ct);

    public Task AddAsync(Property property, CancellationToken ct = default)
    {
        db.Properties.Add(property);
        return Task.CompletedTask;
    }
}
