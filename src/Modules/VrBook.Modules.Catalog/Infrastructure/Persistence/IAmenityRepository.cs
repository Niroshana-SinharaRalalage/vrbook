using VrBook.Modules.Catalog.Domain;

namespace VrBook.Modules.Catalog.Infrastructure.Persistence;

public interface IAmenityRepository
{
    Task<IReadOnlyList<Amenity>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Amenity>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
}
