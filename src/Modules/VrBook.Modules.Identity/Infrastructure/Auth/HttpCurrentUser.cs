using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Identity.Infrastructure.Auth;

/// <summary>
/// HTTP-aware implementation that reads the request's <see cref="ClaimsPrincipal"/>.
/// Replaces the <c>AnonymousCurrentUser</c> stub from A0 when registered scoped.
/// </summary>
/// <remarks>
/// AD B2C populates a custom <c>extension_</c> claim for owner / admin role flags;
/// the OIDC middleware does not map those to <see cref="ClaimTypes.Role"/> by default,
/// so we look at both forms. <c>UserId</c> is the app-side user id stamped by
/// <see cref="UserProvisioningMiddleware"/>, NOT the B2C oid.
/// </remarks>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string AppUserIdItemKey = "VrBook:UserId";
    public const string OwnerClaim = "extension_isOwner";
    public const string AdminClaim = "extension_isAdmin";
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

    public string? Email =>
        accessor.HttpContext?.User.FindFirstValue("emails")
        ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
        ?? accessor.HttpContext?.User.FindFirstValue("email");

    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public bool IsOwner => ReadBoolClaim(OwnerClaim) || HasRole("Owner");
    public bool IsAdmin => ReadBoolClaim(AdminClaim) || HasRole("Admin");

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

    private bool ReadBoolClaim(string type)
    {
        var v = accessor.HttpContext?.User.FindFirstValue(type);
        return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }
}
