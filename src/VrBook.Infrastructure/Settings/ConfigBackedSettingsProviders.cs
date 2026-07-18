using Microsoft.Extensions.Configuration;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Settings;

// VRB-216 Phase A — config-backed default implementations of the §3 settings
// contract. They satisfy VRB-102's config AC (Cancellation:Tiers:*) and give PAY a
// working, stable boundary to build against NOW. VRB-216 later `Replace()`s each
// with a DB-backed impl (admin.* tables, platform-admin editable) seeded from these
// same defaults — invisible to consumers, which depend only on the interfaces.

/// <summary>Reads <c>Cancellation:Tiers:{FirstTierDays,SecondTierDays,MiddleTierRefundPct,FinalCutoffHours}</c>;
/// falls back to the 7/2/50/48 seed defaults. Version is 0 for config-backed.</summary>
public sealed class ConfigCancellationTierProvider(IConfiguration configuration) : ICancellationTierProvider
{
    public Task<GlobalCancellationTiers> GetActiveAsync(CancellationToken ct = default)
    {
        var d = GlobalCancellationTiers.Default;
        var tiers = new GlobalCancellationTiers(
            FirstTierDays: configuration.GetValue("Cancellation:Tiers:FirstTierDays", d.FirstTierDays),
            SecondTierDays: configuration.GetValue("Cancellation:Tiers:SecondTierDays", d.SecondTierDays),
            MiddleTierRefundPct: configuration.GetValue("Cancellation:Tiers:MiddleTierRefundPct", d.MiddleTierRefundPct),
            FinalCutoffHours: configuration.GetValue("Cancellation:Tiers:FinalCutoffHours", d.FinalCutoffHours),
            UpgradePricePct: configuration.GetValue("Cancellation:Tiers:UpgradePricePct", d.UpgradePricePct),
            Version: 0);
        return Task.FromResult(tiers);
    }
}

/// <summary>Config-backed resolver: every property resolves to the global tiered
/// schedule. VRB-215 replaces this with the per-property (Catalog) model selection.</summary>
public sealed class ConfigCancellationPolicyResolver(ICancellationTierProvider tiers) : ICancellationPolicyResolver
{
    public async Task<CancellationPolicySnapshot> ResolveAsync(Guid propertyId, Guid tenantId, CancellationToken ct = default)
    {
        var active = await tiers.GetActiveAsync(ct);
        return CancellationPolicySnapshot.Tiered(active);
    }
}

/// <summary>Config-backed tax posture: facilitator active, empty per-state roster.
/// VRB-216 replaces with the <c>admin.tax_posture</c>-backed impl.</summary>
public sealed class ConfigTaxPostureProvider : ITaxPostureProvider
{
    public Task<TaxPosture> GetAsync(CancellationToken ct = default) => Task.FromResult(TaxPosture.Default);
}
