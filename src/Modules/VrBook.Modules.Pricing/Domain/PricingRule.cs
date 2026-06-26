using VrBook.Contracts.Enums;
using VrBook.Domain.Common;

namespace VrBook.Modules.Pricing.Domain;

/// <summary>
/// Owner-defined adjustment attached to a pricing plan. The quote engine applies
/// enabled rules in <see cref="Priority"/> ascending order (lower number first).
/// See <c>docs/SLICE6_PLAN.md</c> §2.4 + §2.4.1 for the kind × adjustment matrix.
/// </summary>
public sealed class PricingRule : Entity
{
    /// <summary>
    /// Denormalised tenant id (inherits from PricingPlan.TenantId). Per
    /// OPS_M_3_PLAN §1, the denorm lives so RLS doesn't have to join pricing_plans.
    /// </summary>
    public Guid? TenantId { get; private set; }

    public Guid PricingPlanId { get; private set; }
    public PricingRuleKind Kind { get; private set; }
    public int Priority { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public int? DayOfWeekMask { get; private set; }
    public int? MinNights { get; private set; }
    public int? MaxNights { get; private set; }
    public int? DaysBeforeCheckin { get; private set; }
    public PricingAdjustmentKind AdjustmentKind { get; private set; }
    public decimal AdjustmentValue { get; private set; }
    public bool IsEnabled { get; private set; }

    private PricingRule() { } // EF

    internal PricingRule(
        Guid tenantId,
        Guid pricingPlanId,
        PricingRuleKind kind,
        int priority,
        DateOnly? startDate,
        DateOnly? endDate,
        int? dayOfWeekMask,
        int? minNights,
        int? maxNights,
        int? daysBeforeCheckin,
        PricingAdjustmentKind adjustmentKind,
        decimal adjustmentValue,
        bool isEnabled)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId required.", nameof(tenantId));
        }
        ValidateForKind(
            kind,
            startDate,
            endDate,
            minNights,
            maxNights,
            daysBeforeCheckin,
            adjustmentKind);

        Id = Guid.NewGuid();
        TenantId = tenantId;
        PricingPlanId = pricingPlanId;
        Kind = kind;
        Priority = priority;
        StartDate = startDate;
        EndDate = endDate;
        DayOfWeekMask = dayOfWeekMask;
        MinNights = minNights;
        MaxNights = maxNights;
        DaysBeforeCheckin = daysBeforeCheckin;
        AdjustmentKind = adjustmentKind;
        AdjustmentValue = adjustmentValue;
        IsEnabled = isEnabled;
    }

    internal void SetPriority(int priority) => Priority = priority;

    internal void SetEnabled(bool isEnabled) => IsEnabled = isEnabled;

    /// <summary>
    /// Per-kind invariant guards per §2.4.1 matrix. Two cells are rejected:
    /// <c>LastMinute × Override</c> and <c>LengthOfStay × Override</c> — a
    /// whole-stay "replace the rate" semantic flattens the weekend uplift and
    /// any prior priority's work, which is almost never what an owner wants.
    /// </summary>
    private static void ValidateForKind(
        PricingRuleKind kind,
        DateOnly? startDate,
        DateOnly? endDate,
        int? minNights,
        int? maxNights,
        int? daysBeforeCheckin,
        PricingAdjustmentKind adjustmentKind)
    {
        switch (kind)
        {
            case PricingRuleKind.DateRangeOverride:
                if (startDate is null || endDate is null)
                {
                    throw new ArgumentException(
                        "DateRangeOverride requires StartDate and EndDate.",
                        nameof(startDate));
                }
                if (startDate > endDate)
                {
                    throw new ArgumentException(
                        "StartDate must be on or before EndDate.",
                        nameof(startDate));
                }
                break;

            case PricingRuleKind.LastMinute:
                if (daysBeforeCheckin is null or < 1)
                {
                    throw new ArgumentException(
                        "LastMinute requires DaysBeforeCheckin >= 1.",
                        nameof(daysBeforeCheckin));
                }
                if (adjustmentKind == PricingAdjustmentKind.Override)
                {
                    throw new ArgumentException(
                        "quote.invalid_rule: LastMinute + Override is rejected (see SLICE6_PLAN §2.4.1).",
                        nameof(adjustmentKind));
                }
                break;

            case PricingRuleKind.LengthOfStay:
                if (minNights is null or < 1)
                {
                    throw new ArgumentException(
                        "LengthOfStay requires MinNights >= 1.",
                        nameof(minNights));
                }
                if (maxNights is not null && maxNights < minNights)
                {
                    throw new ArgumentException(
                        "MaxNights must be >= MinNights when set.",
                        nameof(maxNights));
                }
                if (adjustmentKind == PricingAdjustmentKind.Override)
                {
                    throw new ArgumentException(
                        "quote.invalid_rule: LengthOfStay + Override is rejected (see SLICE6_PLAN §2.4.1).",
                        nameof(adjustmentKind));
                }
                break;

            case PricingRuleKind.DayOfWeek:
            case PricingRuleKind.Base:
                throw new ArgumentException(
                    $"PricingRuleKind.{kind} is not supported in Slice 6.",
                    nameof(kind));

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown PricingRuleKind.");
        }
    }
}
