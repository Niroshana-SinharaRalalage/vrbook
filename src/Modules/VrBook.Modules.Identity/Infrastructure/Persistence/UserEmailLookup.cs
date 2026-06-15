using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Identity.Infrastructure.Persistence;

internal sealed class UserEmailLookup(IdentityDbContext db) : IUserEmailLookup
{
    public async Task<UserEmailSnapshot?> GetAsync(Guid userId, CancellationToken ct = default) =>
        await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.DeletedAt == null)
            .Select(u => new UserEmailSnapshot(
                u.Id,
                u.Email.Value,
                u.DisplayName,
                // Phase 1: no per-user locale column. Phase 2 populates from
                // identity.users.locale once it exists; the interface stays.
                null))
            .FirstOrDefaultAsync(ct);
}
