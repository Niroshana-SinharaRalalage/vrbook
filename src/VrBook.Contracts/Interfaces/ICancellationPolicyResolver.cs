using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Interfaces;

/// <summary>
/// VRB-215 — resolves the effective, fully-resolved cancellation policy for a
/// property at booking Place time. The returned <see cref="CancellationPolicySnapshot"/>
/// is copied onto the booking line so later global-tier or per-property changes never
/// mutate an in-flight booking (immutability guarantee). PAY VRB-102 reads the snapshot
/// off the booking to compute refunds.
///
/// <para>Phase A config-backed impl returns the global-tiered default for every property;
/// VRB-215 replaces it with the per-property (Catalog) selection.</para>
/// </summary>
public interface ICancellationPolicyResolver
{
    Task<CancellationPolicySnapshot> ResolveAsync(Guid propertyId, Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// The resolved, point-in-time policy for a booking line. For <see cref="CancellationModel.Tiered"/>
/// the resolved tier numbers are copied in (with <see cref="TierVersion"/> provenance); for
/// <see cref="CancellationModel.RefundableUpgrade"/> the upgrade purchase + price are recorded.
/// </summary>
public sealed record CancellationPolicySnapshot(
    CancellationModel Model,
    int? FirstTierDays,
    int? SecondTierDays,
    int? MiddleTierRefundPct,
    int? FinalCutoffHours,
    int? TierVersion,
    bool RefundableUpgradePurchased,
    decimal? RefundableUpgradePriceAmount,
    string? RefundableUpgradePriceCurrency)
{
    /// <summary>Builds a Tiered snapshot from the active global tiers.</summary>
    public static CancellationPolicySnapshot Tiered(GlobalCancellationTiers t) => new(
        CancellationModel.Tiered,
        t.FirstTierDays, t.SecondTierDays, t.MiddleTierRefundPct, t.FinalCutoffHours, t.Version,
        RefundableUpgradePurchased: false,
        RefundableUpgradePriceAmount: null,
        RefundableUpgradePriceCurrency: null);
}
