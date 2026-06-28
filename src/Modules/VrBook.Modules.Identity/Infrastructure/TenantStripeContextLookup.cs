using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Infrastructure;

/// <summary>
/// OPS.M.5 §3.4 (D4) Step 4 GREEN — Identity-side implementation of
/// <see cref="ITenantStripeContextLookup"/>. Reads from
/// <see cref="IdentityDbContext.Tenants"/> via <c>AsNoTracking</c>; returns
/// <c>null</c> when the tenant id is unknown.
/// </summary>
internal sealed class TenantStripeContextLookup(IdentityDbContext db) : ITenantStripeContextLookup
{
    public async Task<TenantStripeContext?> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new TenantStripeContext(
                t.Id,
                t.StripeAccountId,
                t.PlatformFeeBps,
                t.DefaultCurrency))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<TenantStripeContext?> GetByStripeAccountAsync(
        string stripeAccountId, CancellationToken ct = default)
    {
        return await db.Tenants
            .AsNoTracking()
            .Where(t => t.StripeAccountId == stripeAccountId)
            .Select(t => new TenantStripeContext(
                t.Id,
                t.StripeAccountId,
                t.PlatformFeeBps,
                t.DefaultCurrency))
            .FirstOrDefaultAsync(ct);
    }
}
