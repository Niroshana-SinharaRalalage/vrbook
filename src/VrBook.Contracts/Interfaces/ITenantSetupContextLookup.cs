namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Slice 4.V2 — cross-module read used by
/// <c>TenantNotificationHandlers</c> to build the payload for
/// <c>tenant.welcome</c>. Returns the tenant's Slug + DisplayName +
/// the current count of active <c>tenant_admin</c> memberships, so the
/// handler can suppress welcome emails on later membership additions
/// (only the FIRST tenant_admin gets welcomed per §7-Q1-A locked).
///
/// <para>The implementation lives in the Identity module; the interface
/// keeps Notifications free of an Identity dependency, mirroring
/// <see cref="IUserEmailLookup"/> and <see cref="IPropertyOwnerLookup"/>.</para>
/// </summary>
public interface ITenantSetupContextLookup
{
    Task<TenantSetupContext?> GetAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed record TenantSetupContext(
    Guid TenantId,
    string Slug,
    string DisplayName,
    int TenantAdminMembershipCount);
