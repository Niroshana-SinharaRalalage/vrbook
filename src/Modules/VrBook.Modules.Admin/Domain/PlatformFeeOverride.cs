namespace VrBook.Modules.Admin.Domain;

/// <summary>
/// VRB-216 — a per-tenant platform-fee override (<c>admin.platform_fee_overrides</c>),
/// platform-admin set. Absent ⇒ the tenant's canonical <c>identity.tenants.platform_fee_bps</c>
/// applies. Platform-scoped: no RLS. <see cref="TenantId"/> is the primary key.
/// </summary>
public sealed class PlatformFeeOverride
{
    public Guid TenantId { get; private set; }
    public int PlatformFeeBps { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private PlatformFeeOverride() { } // EF

    public static PlatformFeeOverride Create(Guid tenantId, int platformFeeBps, Guid updatedByUserId, DateTimeOffset updatedAt) =>
        new()
        {
            TenantId = tenantId,
            PlatformFeeBps = platformFeeBps,
            UpdatedByUserId = updatedByUserId,
            UpdatedAt = updatedAt,
        };

    public void Set(int platformFeeBps, Guid updatedByUserId, DateTimeOffset updatedAt)
    {
        PlatformFeeBps = platformFeeBps;
        UpdatedByUserId = updatedByUserId;
        UpdatedAt = updatedAt;
    }
}
