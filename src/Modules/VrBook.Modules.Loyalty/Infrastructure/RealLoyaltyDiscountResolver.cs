using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
/// Respects the global <c>Loyalty:Enabled</c> config flag (proposal §11.3
/// toggle A8.1.7). When false, all users see 0% — useful for incident response
/// or staged rollouts.
/// </summary>
internal sealed class RealLoyaltyDiscountResolver(
    LoyaltyDbContext db,
    IConfiguration configuration) : ILoyaltyDiscountResolver
{
    public async Task<LoyaltyDiscount> ResolveAsync(Guid? userId, CancellationToken ct = default)
    {
        var enabled = configuration.GetValue("Loyalty:Enabled", true);
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
