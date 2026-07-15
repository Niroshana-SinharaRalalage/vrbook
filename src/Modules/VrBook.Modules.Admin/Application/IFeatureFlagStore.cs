namespace VrBook.Modules.Admin.Application;

/// <summary>
/// VRB-203 — reads the platform feature-flag override table (<c>admin.feature_flags</c>).
/// Returns the persisted override for a flag key, or <c>null</c> when no override row
/// exists (so the resolver falls back to config, then the built-in default).
/// </summary>
public interface IFeatureFlagStore
{
    Task<bool?> GetOverrideAsync(string key, CancellationToken ct = default);

    /// <summary>All override rows, for the admin list endpoint.</summary>
    Task<IReadOnlyList<FeatureFlagOverride>> ListAsync(CancellationToken ct = default);
}

/// <summary>A persisted feature-flag override row (projection for the admin UI/API).</summary>
public sealed record FeatureFlagOverride(string Key, bool Enabled, Guid UpdatedByUserId, DateTimeOffset UpdatedAt);
