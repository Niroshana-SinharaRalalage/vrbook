using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Events;

public sealed record TierPromoted(
    Guid UserId,
    LoyaltyTier FromTier,
    LoyaltyTier ToTier,
    int CompletedStayCount) : DomainEvent;

/// <summary>Reserved for Phase 1 (we don't demote yet). Kept on the contract for forward compatibility.</summary>
public sealed record TierDemoted(
    Guid UserId,
    LoyaltyTier FromTier,
    LoyaltyTier ToTier) : DomainEvent;
