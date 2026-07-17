using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Admin.Domain;
using VrBook.Modules.Admin.Infrastructure.Persistence;

namespace VrBook.Modules.Admin.Infrastructure;

// VRB-216 Phase B — DB-backed implementations of the §3 settings contract. Registered
// via Replace() over the config-backed defaults (VrBook.Infrastructure/Settings); the
// swap is invisible to consumers (PAY, the policy resolver) which depend on the interfaces.

/// <summary>Reads the active <c>admin.cancellation_tiers</c> singleton; falls back to the
/// seed defaults if the row is somehow absent (defensive — the migration seeds it).</summary>
public sealed class DbCancellationTierProvider(AdminDbContext db) : ICancellationTierProvider
{
    public async Task<GlobalCancellationTiers> GetActiveAsync(CancellationToken ct = default)
    {
        var row = await db.CancellationTiers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == Domain.CancellationTiers.SingletonId, ct);
        return row is null
            ? GlobalCancellationTiers.Default
            : new GlobalCancellationTiers(
                row.FirstTierDays, row.SecondTierDays, row.MiddleTierRefundPct,
                row.FinalCutoffHours, row.UpgradePricePct, row.Version);
    }
}

/// <summary>Reads a per-tenant override from <c>admin.platform_fee_overrides</c>; falls back
/// to the platform default (<c>Payment:PlatformFeeBps</c>, 1500). The booking-time fee read
/// stays <c>TenantStripeContext.PlatformFeeBps</c> — this resolver is for the settings display.</summary>
public sealed class DbPlatformFeeResolver(AdminDbContext db, IConfiguration configuration) : IPlatformFeeResolver
{
    public async Task<int> GetFeeBpsAsync(Guid tenantId, CancellationToken ct = default)
    {
        var over = await db.PlatformFeeOverrides.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, ct);
        return over?.PlatformFeeBps ?? configuration.GetValue("Payment:PlatformFeeBps", 1500);
    }
}

/// <summary>Reads the <c>admin.tax_posture</c> singleton and parses the per-state JSON roster.</summary>
public sealed class DbTaxPostureProvider(AdminDbContext db) : ITaxPostureProvider
{
    public async Task<TaxPosture> GetAsync(CancellationToken ct = default)
    {
        var row = await db.TaxPosture.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == TaxPostureRow.SingletonId, ct);
        if (row is null)
        {
            return TaxPosture.Default;
        }
        var roster = string.IsNullOrWhiteSpace(row.PerStateJson)
            ? new Dictionary<string, bool>()
            : JsonSerializer.Deserialize<Dictionary<string, bool>>(row.PerStateJson) ?? new Dictionary<string, bool>();
        return new TaxPosture(row.FacilitatorActive, roster);
    }
}
