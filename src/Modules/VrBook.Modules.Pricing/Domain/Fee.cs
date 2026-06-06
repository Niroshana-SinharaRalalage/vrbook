using VrBook.Contracts.Enums;
using VrBook.Domain.Common;

namespace VrBook.Modules.Pricing.Domain;

/// <summary>
/// Owner-defined fee attached to a pricing plan. The pricing engine adds these
/// to a quote based on Basis (per-stay, per-night, per-guest, percentage of subtotal).
/// </summary>
public sealed class Fee : Entity
{
    public Guid PricingPlanId { get; private set; }
    public FeeKind Kind { get; private set; }
    public decimal Amount { get; private set; }
    public FeeBasis Basis { get; private set; }
    public int? FreeThreshold { get; private set; }
    public string Label { get; private set; } = default!;

    private Fee() { } // EF

    internal Fee(Guid pricingPlanId, FeeKind kind, decimal amount, FeeBasis basis, int? freeThreshold, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Id = Guid.NewGuid();
        PricingPlanId = pricingPlanId;
        Kind = kind;
        Amount = amount;
        Basis = basis;
        FreeThreshold = freeThreshold;
        Label = label.Trim();
    }
}
