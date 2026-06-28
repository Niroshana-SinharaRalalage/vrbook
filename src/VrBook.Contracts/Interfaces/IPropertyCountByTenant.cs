namespace VrBook.Contracts.Interfaces;

/// <summary>
/// OPS.M.7 §4.2 — cross-module read used by Identity's
/// <c>GetMyTenantHandler</c> to answer "have you created your first
/// property yet?". Implemented in Catalog (single
/// <c>EF.Count</c> against <c>catalog.properties WHERE tenant_id = @tid</c>).
///
/// <para>Read-only by design — the wizard's "first property" check is a
/// derived UI state, not a domain event. A future Phase 4 multi-supplier
/// shape stays compatible because a supplier-tenant still has its own
/// property rows.</para>
/// </summary>
public interface IPropertyCountByTenant
{
    Task<int> GetCountAsync(Guid tenantId, CancellationToken ct = default);
}
