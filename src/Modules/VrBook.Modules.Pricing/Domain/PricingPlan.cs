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
    public Guid PropertyId { get; private set; }
    public decimal BaseNightlyRate { get; private set; }
    public decimal WeekendRate { get; private set; }
    public string Currency { get; private set; } = "USD";
    public int MinStayNights { get; private set; } = 1;
    public int MaxStayNights { get; private set; } = 30;
    public bool DynamicEnabled { get; private set; }

    private readonly List<Fee> _fees = new();
    public IReadOnlyList<Fee> Fees => _fees;

    private PricingPlan() { } // EF

    public static PricingPlan Create(Guid propertyId, decimal baseRate, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentOutOfRangeException.ThrowIfNegative(baseRate);
        var p = new PricingPlan
        {
            Id = Guid.NewGuid(),
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
}
