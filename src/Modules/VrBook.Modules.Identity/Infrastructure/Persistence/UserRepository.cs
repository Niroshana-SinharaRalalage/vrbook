using Microsoft.EntityFrameworkCore;
using VrBook.Modules.Identity.Domain;

namespace VrBook.Modules.Identity.Infrastructure.Persistence;

internal sealed class UserRepository(IdentityDbContext db) : IUserRepository
{
    public Task<User?> GetByB2CObjectIdAsync(string b2cObjectId, CancellationToken ct = default) =>
        db.Users.FirstOrDefaultAsync(u => u.B2CObjectId == b2cObjectId, ct);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken ct = default) =>
        db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<IReadOnlyList<User>> SearchAsync(
        string? q, int skip, int take, CancellationToken ct = default)
    {
        var query = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim().ToLowerInvariant()}%";
            query = query.Where(u =>
                EF.Functions.ILike(u.DisplayName, like) ||
                EF.Functions.ILike(((string)(object)u.Email), like));
        }
        return await query
            .OrderBy(u => u.DisplayName)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public Task<int> CountAsync(string? q, CancellationToken ct = default)
    {
        var query = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim().ToLowerInvariant()}%";
            query = query.Where(u =>
                EF.Functions.ILike(u.DisplayName, like) ||
                EF.Functions.ILike(((string)(object)u.Email), like));
        }
        return query.CountAsync(ct);
    }

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await db.Users.AddAsync(user, ct);
}
