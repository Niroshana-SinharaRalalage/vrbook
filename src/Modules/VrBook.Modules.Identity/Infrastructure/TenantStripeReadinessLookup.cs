using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Infrastructure;

/// <summary>
/// VRB-212 — Identity-side implementation of <see cref="ITenantStripeReadinessLookup"/>.
/// Reads the tenant's persisted readiness from <see cref="IdentityDbContext.Tenants"/>
/// via an RLS-bypass scope (the lookup is cross-tenant by design — Catalog calls it for
/// any tenant that owns the property being activated). Mirrors
/// <see cref="TenantStripeContextLookup"/>.
/// </summary>
internal sealed class TenantStripeReadinessLookup(IRlsBypassDbContextFactory<IdentityDbContext> bypassFactory)
    : ITenantStripeReadinessLookup
{
    public async Task<TenantStripeReadiness?> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var bypass = await bypassFactory.CreateForBypassAsync(
            "tenant-stripe-readiness-lookup.by-tenant-id", ct);
        return await bypass.Db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new TenantStripeReadiness(t.Status, t.ChargesEnabled, t.PayoutsEnabled))
            .FirstOrDefaultAsync(ct);
    }
}
