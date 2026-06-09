using VrBook.Modules.Catalog.Domain;

namespace VrBook.Modules.Catalog.Infrastructure.Persistence;

public interface IAmenityRepository
{
    /// <summary>Public-facing list — excludes <c>IsActive = false</c> rows.</summary>
    Task<IReadOnlyList<Amenity>> ListAsync(CancellationToken ct = default);

    /// <summary>Admin-facing list — includes disabled rows.</summary>
    Task<IReadOnlyList<Amenity>> ListAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Amenity>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
}
