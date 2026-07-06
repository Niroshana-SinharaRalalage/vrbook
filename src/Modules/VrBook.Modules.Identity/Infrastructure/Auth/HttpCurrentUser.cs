using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Identity.Infrastructure.Auth;

/// <summary>
/// HTTP-aware implementation that reads the request's <see cref="ClaimsPrincipal"/>.
/// Replaces the <c>AnonymousCurrentUser</c> stub from A0 when registered scoped.
/// </summary>
/// <remarks>
/// Role claims resolve through the ASP.NET-standard <see cref="ClaimTypes.Role"/>
/// mapping populated by JwtBearer from the token's <c>roles</c> claim (Entra
/// App Roles per ADR-0014), or synthesized by <c>UserProvisioningMiddleware</c>
/// for <c>PlatformAdmin</c>. The pre-ADR-0014 <c>extension_isOwner</c> /
/// <c>extension_isAdmin</c> readers were retired in OPS.M.15.2. <c>UserId</c>
/// is the app-side user id stamped by <see cref="UserProvisioningMiddleware"/>,
/// NOT the Entra oid.
/// </remarks>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string AppUserIdItemKey = "VrBook:UserId";
    /// <summary>
    /// OPS.M.8 §3.2 (D2) — <c>HttpContext.Items</c> key holding the
    /// DB-resolved <c>is_platform_admin</c> bit. Stamped by
    /// <c>UserProvisioningMiddleware</c> on first hit per request; nullable
    /// because anonymous + worker requests don't carry the flag.
    /// </summary>
    public const string PlatformAdminItemKey = "VrBook:IsPlatformAdmin";
    /// <summary>
    /// OPS.M.8 §3.2 — role string used for the platform-admin role-claim
    /// shape (<c>[Authorize(Roles="PlatformAdmin")]</c>). The middleware also
    /// adds this claim so <c>HasRole("PlatformAdmin")</c> works for free.
    /// </summary>
    public const string PlatformAdminRole = "PlatformAdmin";

    /// <summary>
    /// Claim type carrying the caller's active-tenant id. Post-M.13.6 the
    /// canonical source is <see cref="ActiveTenantItemKey"/> in
    /// <c>HttpContext.Items</c> (populated from the <c>X-Active-Tenant</c>
    /// header or the primary-membership fallback); this claim is kept as a
    /// legacy accessor so anything still reading claim shape keeps working.
    /// </summary>
    public const string TenantIdClaimType = "app_tenant_id";

    /// <summary>
    /// Slice OPS.M.13.6 — <c>HttpContext.Items</c> key holding the resolved
    /// active tenant id (Guid). Set by <c>UserProvisioningMiddleware</c>
    /// from the <c>X-Active-Tenant</c> header when that header names a
    /// valid membership; falls back to the caller's <c>IsPrimary=true</c>
    /// membership so non-SPA callers (curl, tests) keep resolving.
    /// </summary>
    public const string ActiveTenantItemKey = "VrBook:ActiveTenantId";

    /// <summary>
    /// Slice OPS.M.13.6 — <c>HttpContext.Items</c> key holding the caller's
    /// per-tenant role dictionary
    /// (<c>IReadOnlyDictionary&lt;Guid, IReadOnlySet&lt;string&gt;&gt;</c>).
    /// Materialized by <c>UserProvisioningMiddleware</c> from
    /// <c>identity.tenant_memberships</c>. <see cref="HasTenantRole"/>
    /// reads through this key; the cross-tenant claim hazard from
    /// pre-M.13 is closed because the role check is now correlated with
    /// the tenant id in one lookup.
    /// </summary>
    public const string MembershipRolesItemKey = "VrBook:MembershipRoles";

    /// <summary>
    /// Slice OPS.M.13.6 — the HTTP header the SPA sets on every
    /// non-anonymous request. Populated in the api client from
    /// <c>getActiveTenantId()</c> (per-tab sessionStorage). Blank / missing
    /// for direct API callers (curl, tests) — middleware falls back to
    /// primary membership.
    /// </summary>
    public const string ActiveTenantHeader = "X-Active-Tenant";

    /// <summary>
    /// Slice OPS.M.12 — JWT claim name for the identity provider that
    /// authenticated the token. Entra External ID emits this claim on
    /// federated sign-ins as the provider's OIDC issuer host
    /// (<c>"google.com"</c>, <c>"live.com"</c>, <c>"facebook.com"</c>,
    /// <c>"apple.com"</c>). For Entra-local sign-ins the claim is absent
    /// or equals the tenant issuer host. The application-claims list on
    /// the Entra user flow MUST include <c>idp</c> for social flows
    /// (documented in <c>docs/runbooks/social_idp_setup.md</c>).
    /// </summary>
    public const string IdpClaim = "idp";

    /// <summary>
    /// Slice OPS.M.12 — canonical string used in
    /// <c>identity.user_identities.provider</c> for Entra-local
    /// identities. Constant kept here so producers and readers agree.
    /// </summary>
    public const string ProviderEntraLocal = "entra";

    /// <summary>
    /// Slice OPS.M.12 — canonical strings for each supported social IdP.
    /// Match the DB CHECK constraint on
    /// <c>identity.user_identities.provider</c>.
    /// </summary>
    public const string ProviderGoogle = "google";
    public const string ProviderMicrosoft = "microsoft";
    public const string ProviderFacebook = "facebook";
    public const string ProviderApple = "apple";

    /// <summary>
    /// Slice OPS.M.12 — closed set of <c>idp</c>-claim values recognized as
    /// social federation. The values are Entra External ID's canonical
    /// per-provider host strings, NOT the normalized
    /// <c>user_identities.provider</c> tokens. Comparison is case-
    /// insensitive.
    /// <para>Read by <c>AdminSocialIdpRejectionMiddleware</c> to decide
    /// whether the current token's <c>idp</c> triggers the gate. Extends
    /// naturally as new social IdPs are added; each addition also updates
    /// <c>IdentityProviderClassifier</c> to map the host to a provider
    /// token.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> SocialIdpValues =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "google.com",
            "live.com",
            "facebook.com",
            "apple.com",
            "linkedin.com",
            "twitter.com",
            "amazon.com",
        };

    /// <summary>
    /// Slice OPS.M.12 — canonical <c>user_identities.provider</c> values
    /// recognized as social federation. Used by
    /// <c>ProvisionOrLinkUserHandler</c> to determine if a Branch 2 link
    /// on an admin-authority user should be refused.
    /// </summary>
    public static readonly IReadOnlySet<string> SocialProviderKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ProviderGoogle,
            ProviderMicrosoft,
            ProviderFacebook,
            ProviderApple,
        };

    public Guid? UserId
    {
        get
        {
            var ctx = accessor.HttpContext;
            if (ctx is null)
            {
                return null;
            }

            if (ctx.Items.TryGetValue(AppUserIdItemKey, out var v) && v is Guid g)
            {
                return g;
            }

            return null;
        }
    }

    public string? ExternalObjectId =>
        accessor.HttpContext?.User.FindFirstValue("oid")
        ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// Slice OPS.M.12 — returns the raw <c>idp</c> claim when present,
    /// normalized to <c>"entra"</c> when the claim is absent (Entra-local
    /// sign-in default) or when the value equals the tenant's issuer host
    /// (Entra-local sign-in that emits <c>idp</c> anyway). Values in
    /// <see cref="SocialIdpValues"/> are returned verbatim so consumers
    /// can compare directly. Unknown IdP shapes pass through unchanged so
    /// downstream classification (via
    /// <c>IdentityProviderClassifier</c>) can decide policy.
    /// </summary>
    public string? IdentityProvider
    {
        get
        {
            var ctx = accessor.HttpContext;
            if (ctx is null)
            {
                return null;
            }

            var idp = ctx.User.FindFirstValue(IdpClaim);
            if (string.IsNullOrWhiteSpace(idp))
            {
                return ProviderEntraLocal;
            }

            // If the tenant issuer host is configured, treat it as
            // Entra-local. Config is optional; when missing this branch
            // is skipped and idp propagates verbatim.
            var tenantIssuerHost = ctx.RequestServices
                .GetService<IConfiguration>()
                ?["EntraExternalId:TenantIssuerHost"];
            if (!string.IsNullOrWhiteSpace(tenantIssuerHost)
                && string.Equals(idp, tenantIssuerHost, StringComparison.OrdinalIgnoreCase))
            {
                return ProviderEntraLocal;
            }

            return idp;
        }
    }

    public string? Email =>
        accessor.HttpContext?.User.FindFirstValue("emails")
        ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
        ?? accessor.HttpContext?.User.FindFirstValue("email");

    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public bool IsOwner => HasRole("Owner");
    public bool IsAdmin => HasRole("Admin");

    /// <summary>
    /// OPS.M.8 §3.2 — DB-authoritative read. Prefers the
    /// <c>HttpContext.Items</c> bit stamped by the middleware (one DB query
    /// per request); falls back to the role claim for the legacy /
    /// pre-middleware path. Never reads the Entra app-role claim alone.
    /// </summary>
    public bool IsPlatformAdmin
    {
        get
        {
            var ctx = accessor.HttpContext;
            if (ctx is null)
            {
                return false;
            }
            if (ctx.Items.TryGetValue(PlatformAdminItemKey, out var v) && v is bool b)
            {
                return b;
            }
            return HasRole(PlatformAdminRole);
        }
    }

    public Guid? TenantId
    {
        get
        {
            var ctx = accessor.HttpContext;
            if (ctx is null)
            {
                return null;
            }
            // Slice OPS.M.13.6 — Items key is the canonical source (populated
            // from X-Active-Tenant OR primary-membership fallback). Claim
            // stays as a legacy accessor.
            if (ctx.Items.TryGetValue(ActiveTenantItemKey, out var v) && v is Guid g)
            {
                return g;
            }
            var raw = ctx.User.FindFirstValue(TenantIdClaimType);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public IReadOnlyDictionary<Guid, IReadOnlySet<string>> MembershipRoles
    {
        get
        {
            var ctx = accessor.HttpContext;
            if (ctx is null)
            {
                return EmptyMembershipRoles;
            }
            if (ctx.Items.TryGetValue(MembershipRolesItemKey, out var v)
                && v is IReadOnlyDictionary<Guid, IReadOnlySet<string>> dict)
            {
                return dict;
            }
            return EmptyMembershipRoles;
        }
    }

    private static readonly IReadOnlyDictionary<Guid, IReadOnlySet<string>> EmptyMembershipRoles =
        new Dictionary<Guid, IReadOnlySet<string>>();

    public bool HasTenantRole(Guid tenantId, string role)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(role))
        {
            return false;
        }
        // Slice OPS.M.13.6 — direct dictionary lookup: the caller's role
        // set for the specific tenant. Pre-M.13.6 this went through
        // ClaimTypes.Role + app_tenant_id claim, which allowed a
        // tenant_admin role in tenant B to satisfy a HasTenantRole(A, ...)
        // check. See docs/OPS_M_10_2_F11_ARCHITECTURAL_REVIEW.md Ev-A.
        return MembershipRoles.TryGetValue(tenantId, out var roles)
            && roles.Contains(role);
    }

    public bool HasRole(string role)
    {
        var user = accessor.HttpContext?.User;
        if (user is null)
        {
            return false;
        }

        return user.IsInRole(role) || user.HasClaim(c =>
            (c.Type == ClaimTypes.Role || c.Type == "roles") &&
            string.Equals(c.Value, role, StringComparison.OrdinalIgnoreCase));
    }
}
