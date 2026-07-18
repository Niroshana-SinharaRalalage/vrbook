using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Modules.Catalog.Infrastructure;

/// <summary>
/// VRB-215 — resolves a property's cancellation policy from its host-selected
/// <c>CancellationModel</c> (on the Property aggregate) + the active platform tiers.
/// <c>Replace()</c>s the config-backed resolver (which returned Tiered for all).
/// Subtotal-free per the TL boundary: for RefundableUpgrade it carries the platform
/// <c>UpgradePricePct</c> and leaves the amount null — Booking's Place finalizes it.
/// Reuses Property's tenant RLS (the read is tenant-scoped by the caller's scope).
/// </summary>
public sealed class CatalogCancellationPolicyResolver(CatalogDbContext db, ICancellationTierProvider tiers)
    : ICancellationPolicyResolver
{
    public async Task<CancellationPolicySnapshot> ResolveAsync(Guid propertyId, Guid tenantId, CancellationToken ct = default)
    {
        var active = await tiers.GetActiveAsync(ct);
        var model = await db.Properties.AsNoTracking()
            .Where(p => p.Id == propertyId && p.TenantId == tenantId)
            .Select(p => p.CancellationModel)
            .FirstOrDefaultAsync(ct);

        return model == CancellationModel.RefundableUpgrade
            ? CancellationPolicySnapshot.RefundableUpgrade(active)
            : CancellationPolicySnapshot.Tiered(active); // null / unset ⇒ Tiered (the default)
    }
}
