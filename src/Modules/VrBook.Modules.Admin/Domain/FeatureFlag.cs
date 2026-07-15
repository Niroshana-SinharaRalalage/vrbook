namespace VrBook.Modules.Admin.Domain;

/// <summary>
/// VRB-203 — a persisted global feature-flag override row (<c>admin.feature_flags</c>).
/// Platform-scoped: no <c>tenant_id</c>, no RLS. The <see cref="Key"/> follows the
/// <c>Features:&lt;Area&gt;.&lt;Capability&gt;</c> convention and is the primary key.
/// Kept a plain entity (not an <c>AggregateRoot</c>) — the who/when audit lives in
/// <c>identity.audit_log</c> via the settings command's <c>IAuditable</c> path; this
/// row only carries the last-writer stamp for display.
/// </summary>
public sealed class FeatureFlag
{
    public string Key { get; private set; }
    public bool Enabled { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private FeatureFlag() => Key = string.Empty; // EF

    public static FeatureFlag Create(string key, bool enabled, Guid updatedByUserId, DateTimeOffset updatedAt) =>
        new()
        {
            Key = key,
            Enabled = enabled,
            UpdatedByUserId = updatedByUserId,
            UpdatedAt = updatedAt,
        };

    public void Set(bool enabled, Guid updatedByUserId, DateTimeOffset updatedAt)
    {
        Enabled = enabled;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = updatedAt;
    }
}
