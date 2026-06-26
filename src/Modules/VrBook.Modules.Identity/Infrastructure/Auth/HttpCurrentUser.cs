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
    /// Claim type carrying the caller's primary-tenant id (per OPS.M.2 /
    /// `docs/OPS_M_2_PLAN.md` §2.5). String-formatted GUID, lowercase
    /// canonical (`d` format). Stamped by <c>UserProvisioningMiddleware</c>
    /// when the caller has an <c>IsPrimary=true</c> membership in
    /// <c>tenant_memberships</c>.
    /// </summary>
    public const string TenantIdClaimType = "app_tenant_id";

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

    public string? B2CObjectId =>
        accessor.HttpContext?.User.FindFirstValue("oid")
        ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? Email =>
        accessor.HttpContext?.User.FindFirstValue("emails")
        ?? accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
        ?? accessor.HttpContext?.User.FindFirstValue("email");

    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public bool IsOwner => ReadBoolClaim(OwnerClaim) || HasRole("Owner");
    public bool IsAdmin => ReadBoolClaim(AdminClaim) || HasRole("Admin");

    public Guid? TenantId
    {
        get
        {
            var raw = accessor.HttpContext?.User.FindFirstValue(TenantIdClaimType);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public bool HasTenantRole(Guid tenantId, string role)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(role))
        {
            return false;
        }
        var user = accessor.HttpContext?.User;
        if (user is null || user.Identity?.IsAuthenticated != true)
        {
            return false;
        }
        if (!HasRole(role))
        {
            return false;
        }
        var raw = user.FindFirstValue(TenantIdClaimType);
        return Guid.TryParse(raw, out var claimTenant) && claimTenant == tenantId;
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
