using Microsoft.EntityFrameworkCore;
using VrBook.Modules.Admin.Application;
using VrBook.Modules.Admin.Infrastructure.Persistence;

namespace VrBook.Modules.Admin.Infrastructure;

/// <summary>VRB-203 — reads flag overrides from <c>admin.feature_flags</c>.</summary>
internal sealed class AdminDbFeatureFlagStore : IFeatureFlagStore
{
    private readonly AdminDbContext _db;

    public AdminDbFeatureFlagStore(AdminDbContext db) => _db = db;

    public async Task<bool?> GetOverrideAsync(string key, CancellationToken ct = default)
    {
        var row = await _db.FeatureFlags.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Key == key, ct);
        return row?.Enabled;
    }

    public async Task<IReadOnlyList<FeatureFlagOverride>> ListAsync(CancellationToken ct = default) =>
        await _db.FeatureFlags.AsNoTracking()
            .OrderBy(f => f.Key)
            .Select(f => new FeatureFlagOverride(f.Key, f.Enabled, f.UpdatedByUserId, f.UpdatedAt))
            .ToListAsync(ct);
}
