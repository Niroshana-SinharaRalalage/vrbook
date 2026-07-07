using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Identity.Domain;

namespace VrBook.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Slice 4.V2 — implementation of <see cref="ITenantSetupContextLookup"/>. Reads
/// the tenant row + counts the active <c>tenant_admin</c> memberships so
/// <c>TenantNotificationHandlers</c> can suppress welcome emails on membership
/// additions AFTER the founding tenant_admin (only the FIRST tenant_admin gets
/// welcomed per §7-Q1-A).
/// </summary>
internal sealed class TenantSetupContextLookup(IdentityDbContext db) : ITenantSetupContextLookup
{
    public async Task<TenantSetupContext?> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId && t.DeletedAt == null)
            .Select(t => new { t.Id, t.Slug, t.DisplayName })
            .FirstOrDefaultAsync(ct);
        if (tenant is null)
        {
            return null;
        }

        var adminCount = await db.Set<TenantMembership>()
            .AsNoTracking()
            .CountAsync(
                m => m.TenantId == tenantId
                     && m.Role == TenantMembership.RoleTenantAdmin
                     && m.DeletedAt == null,
                ct);

        return new TenantSetupContext(
            TenantId: tenant.Id,
            Slug: tenant.Slug,
            DisplayName: tenant.DisplayName,
            TenantAdminMembershipCount: adminCount);
    }
}
