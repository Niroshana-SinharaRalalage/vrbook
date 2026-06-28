using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Infrastructure;

/// <summary>
/// OPS.M.5 §3.4 (D4) Step 4 GREEN — Identity-side implementation of
/// <see cref="ITenantStripeContextLookup"/>. Reads from
/// <see cref="IdentityDbContext.Tenants"/> via <c>AsNoTracking</c>; returns
/// <c>null</c> when the tenant id is unknown.
///
/// <para>OPS.M.9 §4.6 (D6) — both query paths now open a
/// <see cref="IRlsBypassDbContextFactory{TContext}"/> scope. The
/// <c>identity.tenants</c> table itself is carved out of RLS per §3.2 row
/// 2, so the bypass is a no-op for the SELECT today; the wrapper documents
/// that the lookup is cross-tenant by design and survives any future
/// policy addition.</para>
/// </summary>
internal sealed class TenantStripeContextLookup(IRlsBypassDbContextFactory<IdentityDbContext> bypassFactory)
    : ITenantStripeContextLookup
{
    public async Task<TenantStripeContext?> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var bypass = await bypassFactory.CreateForBypassAsync(
            "tenant-stripe-context-lookup.by-tenant-id", ct);
        return await bypass.Db.Tenants
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
        await using var bypass = await bypassFactory.CreateForBypassAsync(
            "tenant-stripe-context-lookup.by-stripe-account-id", ct);
        return await bypass.Db.Tenants
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
