using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Infrastructure;

/// <summary>
/// VRB-212 — Identity-side implementation of <see cref="ITenantStripeReadinessLookup"/>.
/// Reads the tenant's persisted readiness from <see cref="IdentityDbContext.Tenants"/>.
///
/// <para>Reads via the ambient (request-scoped) <see cref="IdentityDbContext"/> — NOT an
/// RLS-bypass scope. <c>identity.tenants</c> is a carve-out from RLS (OPS.M.9 §3.2), so the
/// row is not tenant-filtered and a cross-tenant read (Catalog asks for the property owner's
/// tenant) resolves on the caller's own connection. This deliberately avoids opening a second
/// pooled DbContext per call: the lookup runs inside GET property-detail and the publish gate,
/// i.e. on the hot request path, where an extra bypass connection is unnecessary pressure.</para>
/// </summary>
internal sealed class TenantStripeReadinessLookup(IdentityDbContext db)
    : ITenantStripeReadinessLookup
{
    public async Task<TenantStripeReadiness?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new TenantStripeReadiness(t.Status, t.ChargesEnabled, t.PayoutsEnabled))
            .FirstOrDefaultAsync(ct);
}
