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
    /// The tenant the caller is currently acting as.
    ///
    /// <para>Slice OPS.M.13.6 — sourced from <c>X-Active-Tenant</c> HTTP header
    /// (SPA-injected from sessionStorage per the tenant picker in M.13.5) if
    /// present and matching an active membership; falls back to the caller's
    /// <c>IsPrimary=true</c> membership stamped by <c>UserProvisioningMiddleware</c>
    /// for DevAuth + non-SPA callers. Null for guests and any caller without a
    /// resolvable membership.</para>
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>
    /// Slice OPS.M.13.6 — DB-authoritative per-tenant role dictionary
    /// materialized by <c>UserProvisioningMiddleware</c> from
    /// <c>identity.tenant_memberships</c>. The key is a tenant id; the value
    /// is the set of role strings the caller holds for that tenant. Empty
    /// dictionary for guests + callers without memberships.
    ///
    /// <para>This is the shape <see cref="HasTenantRole"/> is now built on
    /// so per-tenant role checks are scoped to the active tenant instead of
    /// leaking across tenants (fix for the pre-M.13 cross-tenant claim
    /// hazard flagged in OPS_M_13_ARCHITECTURAL_REVIEW.md Ev-A).</para>
    /// </summary>
    IReadOnlyDictionary<Guid, IReadOnlySet<string>> MembershipRoles { get; }

    bool HasRole(string role);

    /// <summary>
    /// True iff the caller has the given per-tenant role for the given tenant.
    ///
    /// <para>Slice OPS.M.13.6 — implemented against <see cref="MembershipRoles"/>
    /// instead of <c>ClaimTypes.Role</c> so a tenant_admin membership in tenant
    /// B cannot satisfy a role check against tenant A. Foundation for OPS.M.4's
    /// <c>TenantAuthorizationBehavior</c>.</para>
    /// </summary>
    bool HasTenantRole(Guid tenantId, string role);
}
