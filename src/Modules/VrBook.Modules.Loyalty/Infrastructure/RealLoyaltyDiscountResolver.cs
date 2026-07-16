using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Loyalty.Domain;
using VrBook.Modules.Loyalty.Infrastructure.Persistence;

namespace VrBook.Modules.Loyalty.Infrastructure;

/// <summary>
/// Replaces the A0 StubLoyaltyDiscountResolver. Looks up the user's
/// <see cref="LoyaltyAccount"/> (returns Bronze 0% if none) and applies the
/// percent discount from <see cref="TierDefinition.DiscountFor"/>.
///
/// Respects the global <c>Features:Loyalty.Enabled</c> feature flag (VRB-203 — renamed
/// from the legacy <c>Loyalty:Enabled</c> and now resolved through <see cref="IFeatureToggle"/>
/// so a platform admin can toggle it live via <c>/admin/toggles</c> without a redeploy).
/// When false, all users see 0% — useful for incident response or staged rollouts.
/// </summary>
internal sealed class RealLoyaltyDiscountResolver(
    LoyaltyDbContext db,
    IFeatureToggle featureToggle) : ILoyaltyDiscountResolver
{
    public async Task<LoyaltyDiscount> ResolveAsync(Guid? userId, CancellationToken ct = default)
    {
        // Key literal (not the Admin FeatureFlagKeys constant) to avoid a Loyalty→Admin
        // module reference; the two must stay in sync (Features:Loyalty.Enabled).
        var enabled = await featureToggle.IsEnabledAsync(
            "Features:Loyalty.Enabled", defaultValue: true, ct: ct);
        if (!userId.HasValue)
        {
            return new LoyaltyDiscount(LoyaltyTier.Bronze, 0m, enabled);
        }

        var account = await db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == userId.Value, ct);
        if (account is null)
        {
            return new LoyaltyDiscount(LoyaltyTier.Bronze, 0m, enabled);
        }

        var pct = enabled ? TierDefinition.DiscountFor(account.Tier) : 0m;
        return new LoyaltyDiscount(account.Tier, pct, enabled);
    }
}
