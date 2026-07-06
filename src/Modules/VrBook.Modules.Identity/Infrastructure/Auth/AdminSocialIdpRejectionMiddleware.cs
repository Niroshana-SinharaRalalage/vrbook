using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;

namespace VrBook.Modules.Identity.Infrastructure.Auth;

/// <summary>
/// Slice OPS.M.12 — layer 2 admin-vs-social rejection gate. Runs AFTER
/// <see cref="UserProvisioningMiddleware"/> so <see cref="ICurrentUser.IsPlatformAdmin"/>
/// + <see cref="ICurrentUser.MembershipRoles"/> + <see cref="ICurrentUser.IdentityProvider"/>
/// are all stamped when this middleware runs.
///
/// <para>Predicate (both conjuncts required):</para>
/// <list type="number">
///   <item><c>IdentityProvider</c> is in <see cref="HttpCurrentUser.SocialIdpValues"/>.</item>
///   <item><c>IsPlatformAdmin == true</c> OR any active tenant membership.</item>
/// </list>
///
/// <para>On reject: throws <see cref="AdminSocialIdpRejectedException"/> which
/// the ProblemDetails mapper turns into 403 with problem type
/// <c>ProblemTypes.AdminSocialIdpRejected</c> + rule
/// <c>admin_authority_requires_entra_local</c>.</para>
///
/// <para>Whitelist (path exempt from the gate — SPA needs these to render the
/// error page after a rejection):</para>
/// <list type="bullet">
///   <item><c>GET /api/v1/me</c></item>
///   <item><c>GET /api/v1/me/tenants</c></item>
/// </list>
///
/// <para>Anonymous requests short-circuit at the top; no gate on the guest
/// browse path.</para>
///
/// <para>Layer 1 (<c>ProvisionOrLinkUserHandler</c> refuse-at-provisioning
/// per OPS_M_12 §2.2) prevents admin users from EVER carrying a social
/// identity — this middleware is defence in depth for the case where a
/// data-heal race, direct SQL, or config drift bypasses layer 1.</para>
/// </summary>
public sealed class AdminSocialIdpRejectionMiddleware(
    RequestDelegate next,
    ILogger<AdminSocialIdpRejectionMiddleware> logger)
{
    // Hard-coded whitelist; config-driven whitelisting is auth-critical drift
    // risk (M.14 DevAuth analogy — the class of failure DevAuth taught us to
    // fear). Any addition requires code review + arch test update.
    private static readonly PathString MePath = new("/api/v1/me");
    private static readonly PathString MeTenantsPath = new("/api/v1/me/tenants");

    public async Task InvokeAsync(HttpContext ctx, ICurrentUser currentUser)
    {
        if (ctx.User?.Identity?.IsAuthenticated != true)
        {
            await next(ctx);
            return;
        }

        // Whitelist path: SPA needs /me + /me/tenants to render the error
        // page (isPlatformAdmin field + memberships list drive the copy).
        var path = ctx.Request.Path;
        if (path.StartsWithSegments(MeTenantsPath, StringComparison.OrdinalIgnoreCase)
            || path.Equals(MePath, StringComparison.OrdinalIgnoreCase))
        {
            await next(ctx);
            return;
        }

        var idp = currentUser.IdentityProvider;
        if (idp is null || !HttpCurrentUser.SocialIdpValues.Contains(idp))
        {
            // Entra-local sign-in — no gate.
            await next(ctx);
            return;
        }

        var hasAdminAuthority = currentUser.IsPlatformAdmin
            || currentUser.MembershipRoles.Count > 0;

        if (!hasAdminAuthority)
        {
            // Social IdP + no admin authority = normal guest.
            await next(ctx);
            return;
        }

        // Gate fires — log the rejection with the specific tenants + oid so
        // Log Analytics has a queryable trail. The exception's rule + type
        // become the client-visible 403 payload.
        logger.LogWarning(
            "AdminSocialIdpRejection fired. Oid={Oid} Provider={Provider} IsPlatformAdmin={PA} MembershipCount={Count} TenantIds={TenantIds} Path={Path}",
            currentUser.ExternalObjectId,
            idp,
            currentUser.IsPlatformAdmin,
            currentUser.MembershipRoles.Count,
            string.Join(',', currentUser.MembershipRoles.Keys),
            ctx.Request.Path.Value);

        throw new AdminSocialIdpRejectedException(
            identityProvider: idp,
            isPlatformAdmin: currentUser.IsPlatformAdmin,
            attemptedTenantIds: currentUser.MembershipRoles.Keys.ToArray());
    }
}
