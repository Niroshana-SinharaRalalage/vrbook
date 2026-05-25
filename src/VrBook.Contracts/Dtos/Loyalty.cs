using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Dtos;

public sealed record LoyaltyAccountDto(
    Guid UserId,
    LoyaltyTier Tier,
    int CompletedStayCount,
    decimal CurrentDiscountPct,
    LoyaltyTier? NextTier,
    int? StaysUntilNextTier);

public sealed record TierDefinitionDto(
    LoyaltyTier Tier,
    int MinStays,
    int? MaxStays,
    decimal DiscountPct,
    bool IsEnabled);
