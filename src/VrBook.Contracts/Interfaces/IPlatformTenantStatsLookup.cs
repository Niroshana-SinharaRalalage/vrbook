namespace VrBook.Contracts.Interfaces;

/// <summary>
/// OPS.M.8 §3.11 (D11) — cross-module read used by the platform-admin
/// tenant detail page. Returns per-tenant aggregate counts that live in
/// modules outside Identity (Catalog, Booking, Payment). Read-only;
/// computed lazily so the list page (which only needs property count)
/// doesn't pay for the heavier joins.
/// </summary>
public interface IPlatformTenantStatsLookup
{
    /// <summary>
    /// Read all five stats for one tenant in a single round trip per module.
    /// Returns zeroes for tenants with no rows; never throws on a missing
    /// tenant id (read-side is permissive).
    /// </summary>
    Task<PlatformTenantStats> GetAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// OPS.M.8 §3.11 — stats projection. The platform-admin detail page
/// surfaces these alongside the existing tenant fields; numbers feed the
/// operator's "is this tenant active?" judgment without exposing the
/// underlying schemas.
/// </summary>
public sealed record PlatformTenantStats(
    int PropertyCount,
    int ActiveBookingCount,
    int TotalBookingCount,
    decimal LifetimeGrossRevenue,
    string DefaultCurrency);
