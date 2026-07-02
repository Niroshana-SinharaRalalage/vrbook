using Microsoft.EntityFrameworkCore;
using VrBook.Modules.Identity.Domain;

namespace VrBook.Modules.Identity.Infrastructure.Persistence;

internal sealed class UserRepository(IdentityDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken ct = default) =>
        db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<IReadOnlyList<User>> SearchAsync(
        string? q, Guid? tenantId, int skip, int take, CancellationToken ct = default)
    {
        var query = ApplyScope(BuildQ(q), tenantId);
        return await query
            .OrderBy(u => u.DisplayName)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public Task<int> CountAsync(string? q, Guid? tenantId, CancellationToken ct = default) =>
        ApplyScope(BuildQ(q), tenantId).CountAsync(ct);

    private IQueryable<User> BuildQ(string? q)
    {
        var query = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim().ToLowerInvariant()}%";
            // The cast-to-object-then-string idiom is a Npgsql EF Core translator
            // workaround: HasConversion on Email prevents u.Email.Value from
            // being translated inside EF.Functions.ILike. See M.13.4 CI run
            // 28562876342 for the failure this fixes.
            query = query.Where(u =>
                EF.Functions.ILike(u.DisplayName, like) ||
                EF.Functions.ILike(((string)(object)u.Email), like));
        }
        return query;
    }

    /// <summary>
    /// OPS.M.10.2 C1 (#1 Critical) — tenant-membership filter. Returns only
    /// users with an active <c>tenant_memberships</c> row in
    /// <paramref name="tenantId"/>. A null tenantId means "platform-wide" —
    /// the caller (handler) is responsible for the PlatformAdmin role check.
    /// </summary>
    private IQueryable<User> ApplyScope(IQueryable<User> q, Guid? tenantId)
    {
        if (tenantId is null)
        {
            return q;
        }
        var scoped = tenantId.Value;
        return q.Where(u =>
            db.Set<TenantMembership>().Any(m =>
                m.UserId == u.Id && m.TenantId == scoped && m.DeletedAt == null));
    }

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await db.Users.AddAsync(user, ct);
}
