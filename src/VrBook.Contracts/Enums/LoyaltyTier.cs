namespace VrBook.Contracts.Enums;

/// <summary>
/// Loyalty tiers ranked by completed stays. See proposal §11.3.
/// Thresholds live in <c>loyalty.tier_definitions</c> and are tunable per-deploy.
/// </summary>
public enum LoyaltyTier
{
    Bronze = 0,
    Silver = 1,
    Gold = 2,
}
