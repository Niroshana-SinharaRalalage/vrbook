using VrBook.Modules.Identity.Domain;

namespace VrBook.Modules.Identity.Infrastructure.Persistence;

public interface IUserRepository
{
    Task<User?> GetByB2CObjectIdAsync(string b2cObjectId, CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Slice OPS.M.10.2 C1 (#1 Critical) — Tenant-scoped user search.
    /// <c>tenantId == null</c> performs a platform-wide search (PlatformAdmin only
    /// — the caller is responsible for the role check upstream). Non-null filters
    /// to users with an active <c>tenant_memberships</c> row for that tenant.
    ///
    /// <para>The previous unscoped overload was the root cause of the
    /// `SearchUsersQuery_OwnerA_must_not_enumerate_OwnerB_user` leak in
    /// `CarveOutAppLayerTests`.</para>
    /// </summary>
    Task<IReadOnlyList<User>> SearchAsync(
        string? q, Guid? tenantId, int skip, int take, CancellationToken ct = default);

    /// <summary>OPS.M.10.2 C1 — paired count with the same tenant scoping.</summary>
    Task<int> CountAsync(string? q, Guid? tenantId, CancellationToken ct = default);

    Task AddAsync(User user, CancellationToken ct = default);
}
