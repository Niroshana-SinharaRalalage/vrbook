using VrBook.Modules.Identity.Domain;

namespace VrBook.Modules.Identity.Infrastructure.Persistence;

public interface IUserRepository
{
    Task<User?> GetByB2CObjectIdAsync(string b2cObjectId, CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<User>> SearchAsync(string? q, int skip, int take, CancellationToken ct = default);
    Task<int> CountAsync(string? q, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
}
