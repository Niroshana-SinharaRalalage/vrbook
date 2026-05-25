namespace VrBook.Contracts.Enums;

/// <summary>
/// Five rule kinds resolved in priority order. See proposal §11.2.
/// Lower priority number wins ties.
/// </summary>
public enum PricingRuleKind
{
    DateRangeOverride = 0,
    LastMinute = 1,
    LengthOfStay = 2,
    DayOfWeek = 3,
    Base = 4,
}

public enum PricingAdjustmentKind
{
    Absolute = 0,
    Multiplier = 1,
    Override = 2,
}

public enum FeeKind
{
    Cleaning = 0,
    ExtraGuest = 1,
    Deposit = 2,
    Tax = 3,
}

public enum FeeBasis
{
    PerStay = 0,
    PerNight = 1,
    PerGuest = 2,
    Percentage = 3,
}
