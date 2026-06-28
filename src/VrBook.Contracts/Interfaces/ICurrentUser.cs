namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Ambient access to the calling user inside MediatR handlers and pipeline behaviors.
/// Resolves null when called from a background worker or anonymous endpoint.
/// </summary>
public interface ICurrentUser
{
    /// <summary>App-side user id (NOT the B2C object id).</summary>
    Guid? UserId { get; }

    /// <summary>B2C object id from the JWT <c>oid</c> claim.</summary>
    string? B2CObjectId { get; }

    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsOwner { get; }
    bool IsAdmin { get; }

    /// <summary>
    /// OPS.M.8 §3.1 (D1) + §3.2 (D2) — DB-authoritative platform-admin flag.
    /// Source: <c>identity.users.is_platform_admin</c>, materialized by
    /// <c>UserProvisioningMiddleware</c> per ADR-0014's DB-wins precedence.
    /// Reads <c>true</c> only for explicit operator promotions (see
    /// <c>User.GrantPlatformAdmin</c>); never trusts Entra app-role claims
    /// alone.
    ///
    /// <para>Consumed by <c>TenantAuthorizationBehavior</c> for the
    /// cross-tenant bypass on any <c>ITenantScoped</c> command, and by the
    /// platform-admin GET endpoints' <c>[Authorize(Roles="PlatformAdmin")]</c>
    /// gate.</para>
    /// </summary>
    bool IsPlatformAdmin { get; }

    /// <summary>
    /// The tenant the caller is currently acting as. Read from the
    /// <c>app_tenant_id</c> claim stamped by the OPS.M.2 middleware
    /// enrichment (UserProvisioningMiddleware reads the caller's
    /// <c>tenant_memberships</c> row where <c>IsPrimary=true</c> and adds the
    /// claim). Null for guests and any caller without a primary membership.
    /// </summary>
    Guid? TenantId { get; }

    bool HasRole(string role);

    /// <summary>
    /// True iff the caller has the given per-tenant role for the given tenant.
    /// Reads <c>ClaimTypes.Role</c> for the role match AND verifies
    /// <c>app_tenant_id</c> equals <paramref name="tenantId"/>. The plain
    /// <see cref="HasRole"/> alone cannot answer "WHICH tenant" — that's why
    /// this method exists. Foundation for OPS.M.4's
    /// <c>TenantAuthorizationBehavior</c>.
    /// </summary>
    bool HasTenantRole(Guid tenantId, string role);
}
