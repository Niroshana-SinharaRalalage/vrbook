using VrBook.Contracts.Enums;
using VrBook.Domain.Common;

namespace VrBook.Modules.Loyalty.Domain;

/// <summary>
/// One row per guest user. Tier is derived from <see cref="CompletedStayCount"/>
/// via the static thresholds in <see cref="TierDefinition.Resolve"/>; we still
/// store the latest tier value so admin queries don't need to recompute on every
/// projection.
///
/// Proposal §11.3 Phase-1 tiers:
///   Bronze: 1+ stays, 0% discount
///   Silver: 3+ stays, 5% discount
///   Gold:   6+ stays, 10% discount
///
/// Auto-created on first <c>BookingCompleted</c> event for a guest.
/// </summary>
public sealed class LoyaltyAccount : AggregateRoot
{
    public Guid UserId { get; private set; }
    public LoyaltyTier Tier { get; private set; }
    public int CompletedStayCount { get; private set; }
    public DateTimeOffset? LastEvaluatedAt { get; private set; }

    private LoyaltyAccount() { } // EF

    public static LoyaltyAccount OpenForUser(Guid userId)
    {
        return new LoyaltyAccount
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Tier = LoyaltyTier.Bronze,
            CompletedStayCount = 0,
        };
    }

    /// <summary>
    /// Called from <c>OnBookingCompletedHandler</c>. Increments the stay count
    /// and re-evaluates tier. Idempotency is delegated to the caller (which uses
    /// an outbox/event-id replay check).
    /// </summary>
    public void RecordCompletedStay()
    {
        CompletedStayCount++;
        Tier = TierDefinition.Resolve(CompletedStayCount);
        LastEvaluatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Static tier-resolution table. Phase 1 has hard-coded thresholds; if we
/// ever need to make them editable we replace this with a DbSet&lt;TierDefinition&gt;.</summary>
public static class TierDefinition
{
    private const int SilverThreshold = 3;
    private const int GoldThreshold = 6;

    public const decimal BronzeDiscountPct = 0m;
    public const decimal SilverDiscountPct = 5m;
    public const decimal GoldDiscountPct = 10m;

    public static LoyaltyTier Resolve(int completedStays)
    {
        if (completedStays >= GoldThreshold)
        {
            return LoyaltyTier.Gold;
        }
        if (completedStays >= SilverThreshold)
        {
            return LoyaltyTier.Silver;
        }
        return LoyaltyTier.Bronze;
    }

    public static decimal DiscountFor(LoyaltyTier tier) => tier switch
    {
        LoyaltyTier.Gold => GoldDiscountPct,
        LoyaltyTier.Silver => SilverDiscountPct,
        _ => BronzeDiscountPct,
    };

    public static (LoyaltyTier? NextTier, int? StaysUntilNext) NextTier(int completedStays)
    {
        if (completedStays < SilverThreshold)
        {
            return (LoyaltyTier.Silver, SilverThreshold - completedStays);
        }
        if (completedStays < GoldThreshold)
        {
            return (LoyaltyTier.Gold, GoldThreshold - completedStays);
        }
        return (null, null);
    }
}
