using VrBook.Contracts.Common;
using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Dtos;

public sealed record PricingPlanDto(
    Guid Id,
    Guid PropertyId,
    decimal BaseNightlyRate,
    decimal WeekendRate,
    string Currency,
    int MinStayNights,
    int MaxStayNights,
    bool DynamicEnabled,
    IReadOnlyList<PricingRuleDto> Rules,
    IReadOnlyList<FeeDto> Fees);

public sealed record PricingRuleDto(
    Guid Id,
    PricingRuleKind Kind,
    int Priority,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? DayOfWeekMask,
    int? MinNights,
    int? MaxNights,
    int? DaysBeforeCheckin,
    PricingAdjustmentKind AdjustmentKind,
    decimal AdjustmentValue,
    bool IsEnabled);

public sealed record FeeDto(
    Guid Id,
    FeeKind Kind,
    decimal Amount,
    FeeBasis Basis,
    int? FreeThreshold,
    string Label);

public sealed record UpdatePricingPlanRequest(
    decimal BaseNightlyRate,
    decimal WeekendRate,
    string Currency,
    int MinStayNights,
    int MaxStayNights,
    bool DynamicEnabled,
    IReadOnlyList<FeeDto> Fees);

public sealed record CreatePricingRuleRequest(
    PricingRuleKind Kind,
    int Priority,
    DateOnly? StartDate,
    DateOnly? EndDate,
    int? DayOfWeekMask,
    int? MinNights,
    int? MaxNights,
    int? DaysBeforeCheckin,
    PricingAdjustmentKind AdjustmentKind,
    decimal AdjustmentValue,
    bool IsEnabled);

/// <summary>POST /properties/{id}/quotes request body.</summary>
public sealed record QuoteRequest(
    DateOnly Checkin,
    DateOnly Checkout,
    int Guests,
    bool ApplyLoyaltyDiscount);

/// <summary>Per-night and total breakdown returned by the pricing engine. See proposal §11.2.</summary>
public sealed record QuoteDto(
    Guid PropertyId,
    DateRange Range,
    int Guests,
    IReadOnlyList<NightlyLineDto> Nightly,
    IReadOnlyList<FeeLineDto> Fees,
    Money Subtotal,
    Money Discount,
    Money Taxes,
    Money Total,
    DateTimeOffset ExpiresAt);

public sealed record NightlyLineDto(DateOnly Date, Money Amount, string? RuleApplied);

public sealed record FeeLineDto(string Label, FeeKind Kind, Money Amount);
