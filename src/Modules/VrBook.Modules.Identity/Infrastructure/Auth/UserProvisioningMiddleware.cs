using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Modules.Identity.Application.Users.Commands;
using VrBook.Modules.Identity.Domain;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity.Infrastructure.Auth;

/// <summary>
/// On every authenticated request, ensures a row exists in <c>identity.users</c> for the
/// caller's external identity (Entra oid + provider) via
/// <c>ProvisionOrLinkUserCommand</c>. Also stamps <c>HttpContext.Items</c> with the
/// app-side user id + platform-admin flag + resolved active tenant + per-tenant
/// role dictionary. Downstream handlers read these through <c>ICurrentUser</c>.
///
/// <para>Slice OPS.M.13.6 — active-tenant resolution flipped from
/// "always the caller's IsPrimary membership" to "X-Active-Tenant header if
/// present and valid, else IsPrimary membership fallback". The tenant-picker
/// SPA sets the header from sessionStorage on every non-anonymous request;
/// DevAuth cookies + curl callers hit the fallback and behave as before.</para>
///
/// <para>Slice OPS.M.13.6 — the per-membership <c>ClaimTypes.Role</c> loop
/// was dropped. Downstream tenant-role checks read
/// <c>ICurrentUser.MembershipRoles</c> which is stamped as a
/// <c>Dictionary&lt;Guid, IReadOnlySet&lt;string&gt;&gt;</c> on
/// <c>HttpContext.Items</c>. The <c>PlatformAdmin</c> role claim IS still
/// written because <c>[Authorize(Roles="PlatformAdmin")]</c> reads through
/// <c>ClaimsPrincipal.IsInRole</c>.</para>
/// </summary>
public sealed class UserProvisioningMiddleware(RequestDelegate next, ILogger<UserProvisioningMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx, IMediator mediator, IdentityDbContext db)
    {
        if (ctx.User?.Identity?.IsAuthenticated == true)
        {
            var oid = ctx.User.FindFirstValue("oid") ?? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(oid))
            {
                try
                {
                    var email = ctx.User.FindFirstValue("emails")
                                ?? ctx.User.FindFirstValue(ClaimTypes.Email)
                                ?? ctx.User.FindFirstValue("email")
                                ?? $"{oid}@unknown.local";

                    var displayName = ctx.User.FindFirstValue("name")
                                      ?? ctx.User.FindFirstValue(ClaimTypes.Name)
                                      ?? "User";

                    var emailVerified = string.Equals(
                        ctx.User.FindFirstValue("email_verified"), "true",
                        StringComparison.OrdinalIgnoreCase);

                    var userId = await mediator.Send(new ProvisionOrLinkUserCommand(
                        Provider: "entra",
                        ExternalId: oid,
                        Email: email,
                        EmailVerified: emailVerified,
                        DisplayName: displayName));

                    ctx.Items[HttpCurrentUser.AppUserIdItemKey] = userId;

                    var memberships = await db.Set<TenantMembership>()
                        .Where(m => m.UserId == userId && m.DeletedAt == null)
                        .Select(m => new { m.TenantId, m.Role, m.IsPrimary })
                        .ToListAsync(ctx.RequestAborted);

                    var isPlatformAdmin = await db.Users
                        .Where(u => u.Id == userId)
                        .Select(u => u.IsPlatformAdmin)
                        .FirstOrDefaultAsync(ctx.RequestAborted);

                    ctx.Items[HttpCurrentUser.PlatformAdminItemKey] = isPlatformAdmin;

                    // Slice OPS.M.13.6 — build the per-tenant role dictionary and stamp
                    // Items[MembershipRoles]. Multiple memberships in the same tenant
                    // (rare — shouldn't happen given the (user_id, tenant_id) partial
                    // UNIQUE — but defense in depth) get their role sets merged.
                    var membershipRoles = memberships
                        .GroupBy(m => m.TenantId)
                        .ToDictionary<IGrouping<Guid, dynamic>, Guid, IReadOnlySet<string>>(
                            g => g.Key,
                            g => (IReadOnlySet<string>)new HashSet<string>(
                                g.Select(m => (string)m.Role),
                                StringComparer.Ordinal));
                    ctx.Items[HttpCurrentUser.MembershipRolesItemKey] = membershipRoles;

                    // Slice OPS.M.13.6 — active tenant resolution.
                    // Priority: X-Active-Tenant header (SPA) → IsPrimary fallback (DevAuth + tests).
                    Guid? activeTenantId = null;
                    var headerValue = ctx.Request.Headers[HttpCurrentUser.ActiveTenantHeader]
                        .ToString();
                    if (!string.IsNullOrWhiteSpace(headerValue)
                        && Guid.TryParse(headerValue, out var headerTenantId)
                        && memberships.Any(m => m.TenantId == headerTenantId))
                    {
                        activeTenantId = headerTenantId;
                    }
                    else
                    {
                        var primary = memberships.FirstOrDefault(m => m.IsPrimary);
                        if (primary is not null)
                        {
                            activeTenantId = primary.TenantId;
                        }
                    }

                    if (activeTenantId.HasValue)
                    {
                        ctx.Items[HttpCurrentUser.ActiveTenantItemKey] = activeTenantId.Value;
                    }

                    // Slice OPS.M.13.6 — write ONLY the PlatformAdmin role claim +
                    // the legacy app_tenant_id claim (still consumed by pre-M.13
                    // code paths + any external audit). Per-membership role claims
                    // are gone; HasTenantRole reads MembershipRoles from Items.
                    if ((activeTenantId.HasValue || isPlatformAdmin)
                        && ctx.User.Identity is ClaimsIdentity primaryIdentity)
                    {
                        if (isPlatformAdmin)
                        {
                            primaryIdentity.AddClaim(new Claim(
                                ClaimTypes.Role, HttpCurrentUser.PlatformAdminRole));
                        }

                        if (activeTenantId.HasValue)
                        {
                            primaryIdentity.AddClaim(new Claim(
                                HttpCurrentUser.TenantIdClaimType, activeTenantId.Value.ToString()));
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "User provisioning failed for oid {Oid}. Request continues without app-user id.", oid);
                }
            }
        }

        await next(ctx);
    }
}
