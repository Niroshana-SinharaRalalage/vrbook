namespace VrBook.Contracts.Interfaces;

/// <summary>
/// VRB-216 — resolves the effective platform-fee basis points for a tenant
/// (platform default → per-tenant override). This is the read used by the settings
/// UI to display "platform fee N% — your net" to hosts (Q4: fee shown to hosts).
///
/// <para>NOTE: the booking-time fee read stays <c>TenantStripeContext.PlatformFeeBps</c>
/// (the override is folded into <c>TenantStripeContextLookup</c>), so PAY's single fee
/// read never changes. This resolver exists for the settings/payout display only.</para>
/// </summary>
public interface IPlatformFeeResolver
{
    /// <summary>Effective fee in basis points (0–10000). Default 1500 (15%) unless a
    /// per-tenant override is set.</summary>
    Task<int> GetFeeBpsAsync(Guid tenantId, CancellationToken ct = default);
}
