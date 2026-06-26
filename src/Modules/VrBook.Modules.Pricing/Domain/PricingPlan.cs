using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Domain.Common;

namespace VrBook.Modules.Pricing.Domain;

/// <summary>
/// PricingPlan aggregate root. One per Property (FK property_user_id) — created
/// on demand when an owner first sets a price. Holds the base + weekend rates,
/// min/max stay, dynamic toggle, and a collection of fees.
/// </summary>
public sealed class PricingPlan : AggregateRoot
{
    /// <summary>
    /// Tenant the pricing plan belongs to (inherits from the property's owner).
    /// Per OPS_M_3_PLAN §3.1 — `Guid?` during 3a/3b; flips to `Guid` in 3c.
    /// </summary>
    public Guid? TenantId { get; private set; }

    public Guid PropertyId { get; private set; }
    public decimal BaseNightlyRate { get; private set; }
    public decimal WeekendRate { get; private set; }
    public string Currency { get; private set; } = "USD";
    public int MinStayNights { get; private set; } = 1;
    public int MaxStayNights { get; private set; } = 30;
    public bool DynamicEnabled { get; private set; }

    private readonly List<Fee> _fees = new();
    public IReadOnlyList<Fee> Fees => _fees;

    private readonly List<PricingRule> _rules = new();
    public IReadOnlyList<PricingRule> Rules => _rules;

    private PricingPlan() { } // EF

    public static PricingPlan Create(Guid tenantId, Guid propertyId, decimal baseRate, string currency)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId required.", nameof(tenantId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentOutOfRangeException.ThrowIfNegative(baseRate);
        var p = new PricingPlan
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PropertyId = propertyId,
            BaseNightlyRate = baseRate,
            WeekendRate = baseRate,
            Currency = currency.ToUpperInvariant(),
        };
        p.Raise(new PricingPlanUpdated(p.Id, propertyId));
        return p;
    }

    public void Replace(
        decimal baseRate,
        decimal weekendRate,
        string currency,
        int minStay,
        int maxStay,
        bool dynamicEnabled,
        IEnumerable<(FeeKind kind, decimal amount, FeeBasis basis, int? freeThreshold, string label)> fees)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentOutOfRangeException.ThrowIfNegative(baseRate);
        ArgumentOutOfRangeException.ThrowIfNegative(weekendRate);
        ArgumentOutOfRangeException.ThrowIfLessThan(minStay, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxStay, minStay);

        BaseNightlyRate = baseRate;
        WeekendRate = weekendRate;
        Currency = currency.ToUpperInvariant();
        MinStayNights = minStay;
        MaxStayNights = maxStay;
        DynamicEnabled = dynamicEnabled;

        _fees.Clear();
        foreach (var (k, a, b, ft, l) in fees)
        {
            _fees.Add(new Fee(Id, k, a, b, ft, l));
        }

        Raise(new PricingPlanUpdated(Id, PropertyId));
    }

    /// <summary>
    /// Append a new rule. Priority defaults to <c>max(existing) + 1</c> when
    /// the caller passes <see langword="null"/> — keeps the new rule lowest-
    /// priority (last to apply) so it can't surprise an existing stack.
    /// </summary>
    public PricingRule AddRule(
        PricingRuleKind kind,
        int? priority,
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
        var resolvedPriority = priority ?? (_rules.Count == 0 ? 0 : _rules.Max(r => r.Priority) + 1);
        var ruleTenantId = TenantId ?? throw new InvalidOperationException(
            "PricingPlan has no TenantId; cannot add rule. Aggregate invariant violated.");
        var rule = new PricingRule(
            ruleTenantId,
            Id,
            kind,
            resolvedPriority,
            startDate,
            endDate,
            dayOfWeekMask,
            minNights,
            maxNights,
            daysBeforeCheckin,
            adjustmentKind,
            adjustmentValue,
            isEnabled);
        _rules.Add(rule);
        Raise(new PricingRuleAdded(Id, rule.Id));
        return rule;
    }

    /// <summary>Remove a rule by id. No-op on unknown id (idempotent).</summary>
    public void RemoveRule(Guid ruleId)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is null)
        {
            return;
        }
        _rules.Remove(rule);
        Raise(new PricingRuleRemoved(Id, ruleId));
    }

    /// <summary>
    /// Rewrite all priorities to <c>0..N-1</c> in the order given. The id list
    /// must be a permutation of this plan's current rule ids; unknown ids and
    /// missing ids are rejected (caller has the wrong view of the plan).
    /// Last-write-wins under concurrent drag (see SLICE6_PLAN §2.5).
    /// </summary>
    public void ReorderRules(IReadOnlyList<Guid> orderedIds)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        if (orderedIds.Count != _rules.Count)
        {
            throw new ArgumentException(
                $"orderedIds count ({orderedIds.Count}) does not match current rule count ({_rules.Count}).",
                nameof(orderedIds));
        }
        var byId = _rules.ToDictionary(r => r.Id);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            if (!byId.TryGetValue(orderedIds[i], out var rule))
            {
                throw new ArgumentException(
                    $"Rule {orderedIds[i]} is not part of this plan.",
                    nameof(orderedIds));
            }
            rule.SetPriority(i);
        }
    }

    /// <summary>
    /// Toggle a rule's enabled flag. Does NOT raise an event — see SLICE6_PLAN
    /// §2.6 (PATCH .../enabled is semantically a flag flip, not a structural
    /// change, so re-emitting <see cref="PricingRuleAdded"/> would be noisy).
    /// </summary>
    public void SetRuleEnabled(Guid ruleId, bool isEnabled)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == ruleId)
            ?? throw new ArgumentException(
                $"Rule {ruleId} is not part of this plan.",
                nameof(ruleId));
        rule.SetEnabled(isEnabled);
    }
}
