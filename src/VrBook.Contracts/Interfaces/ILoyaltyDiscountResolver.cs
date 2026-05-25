using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Pricing → Loyalty boundary. Returns the discount percentage to apply to a quote
/// for a given user (proposal §11.3). Stub returns 0% until Loyalty module ships.
/// </summary>
public interface ILoyaltyDiscountResolver
{
    Task<LoyaltyDiscount> ResolveAsync(Guid? userId, CancellationToken ct = default);
}

public sealed record LoyaltyDiscount(
    LoyaltyTier Tier,
    decimal DiscountPct,
    bool IsEnabled);
