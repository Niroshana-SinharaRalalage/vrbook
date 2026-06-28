using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Infrastructure;

/// <summary>
/// OPS.M.5 §3.7 + §3.8 — Identity-side implementation of
/// <see cref="IConnectAccountReadinessUpdater"/>. Loads the tenant by Stripe
/// account id, applies the readiness state machine via
/// <c>Tenant.UpdateStripeAccountReadiness</c>, and saves.
/// </summary>
internal sealed class ConnectAccountReadinessUpdater(IdentityDbContext db)
    : IConnectAccountReadinessUpdater
{
    public async Task<bool> UpdateAsync(
        string stripeAccountId, bool chargesEnabled, bool payoutsEnabled, CancellationToken ct = default)
    {
        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.StripeAccountId == stripeAccountId, ct);
        if (tenant is null)
        {
            return false;
        }
        tenant.UpdateStripeAccountReadiness(chargesEnabled, payoutsEnabled);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
