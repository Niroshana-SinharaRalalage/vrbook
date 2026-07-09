namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Ambient access to the calling user inside MediatR handlers and pipeline behaviors.
/// Resolves null when called from a background worker or anonymous endpoint.
/// </summary>
public interface ICurrentUser
{
    /// <summary>App-side user id (NOT the identity-provider oid).</summary>
    Guid? UserId { get; }

    /// <summary>
    /// External identity provider's object id from the JWT <c>oid</c> claim.
    /// Today this is the Entra External ID oid (per ADR-0012); OPS.M.12 will
    /// add per-social-IdP oids resolved through <c>identity.user_identities</c>.
    /// </summary>
    string? ExternalObjectId { get; }

    /// <summary>
    /// Slice OPS.M.12 — the sign-in path that produced this token.
    /// <para>Source: JWT <c>idp</c> claim. For Entra-local sign-in, the claim
    /// is absent OR equal to the tenant's issuer host — both normalized to
    /// <c>"entra"</c> by the accessor. For social federation, the provider's
    /// OIDC issuer host (e.g. <c>"google.com"</c>, <c>"live.com"</c>,
    /// <c>"facebook.com"</c>, <c>"apple.com"</c>).</para>
    /// <para>Consumed by <c>AdminSocialIdpRejectionMiddleware</c>'s admin-vs-
    /// social rejection gate + by any handler that needs to differentiate
    /// assurance levels between admin-required flows and guest-optional ones.
    /// Null for anonymous requests.</para>
    /// </summary>
    string? IdentityProvider { get; }

    string? Email { get; }
    bool IsAuthenticated { get; }

    /// <summary>
    /// Slice OPS.M.22 §3 — the Entra CIAM user flow that minted this token.
    /// Source: JWT <c>tfp</c> claim first (Azure B2C legacy naming that Entra
    /// External ID inherited); <c>acr</c> second (newer CIAM); null when neither
    /// is present. Consumers compare the raw string against the configured
    /// <c>EntraExternalId:AdminFlowName</c> / <c>:GuestFlowName</c> values to
    /// decide whether the caller took the admin flow (<c>AdminSignUpSignIn</c>)
    /// or the guest flow (<c>GuestSignUpSignIn</c>).
    /// <para>Read by <c>UserProvisioningMiddleware</c>'s M.22.4 admin-gate:
    /// admin-flow tokens on unknown emails REFUSE with
    /// <c>AdminAccountNotProvisionedException</c>; guest-flow tokens on
    /// unknown emails follow the lazy-provision Branch 3 path unchanged.</para>
    /// <para>Null for anonymous requests. Also null for legacy single-flow
    /// tenants that don't emit tfp/acr at all — in which case the middleware
    /// treats the token as coming from the tenant-default flow (usually the
    /// guest flow) per plan §4 risk #1.</para>
    /// </summary>
    string? EntraFlow { get; }

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
    /// for non-SPA callers (curl, integration tests). Null for guests and any caller without a
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
