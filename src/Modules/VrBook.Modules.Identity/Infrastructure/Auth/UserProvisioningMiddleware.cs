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
/// caller's B2C <c>oid</c>. First-login provisions the row; subsequent calls refresh
/// LastLoginAt + DisplayName + EmailVerified from the latest token. Stamps
/// <c>HttpCurrentUser.AppUserIdItemKey</c> on <see cref="HttpContext.Items"/> so downstream
/// handlers can read <see cref="VrBook.Contracts.Interfaces.ICurrentUser.UserId"/>.
///
/// OPS.M.2: after provisioning, reads the caller's <c>tenant_memberships</c> rows and
/// stamps <c>ClaimTypes.Role</c> (one per membership) + <c>app_tenant_id</c> (from the
/// <c>IsPrimary=true</c> row) onto the request's <see cref="ClaimsPrincipal"/>. DB is the
/// sole source of truth per `docs/OPS_M_2_PLAN.md` §2.7 (DB-wins precedence) — no
/// claim-already-present guard; the DB read always runs.
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

                    var isOwner = string.Equals(
                        ctx.User.FindFirstValue(HttpCurrentUser.OwnerClaim), "true",
                        StringComparison.OrdinalIgnoreCase)
                        || ctx.User.IsInRole("Owner");

                    var isAdmin = string.Equals(
                        ctx.User.FindFirstValue(HttpCurrentUser.AdminClaim), "true",
                        StringComparison.OrdinalIgnoreCase)
                        || ctx.User.IsInRole("Admin");

                    var userId = await mediator.Send(new ProvisionUserCommand(
                        oid, email, displayName, emailVerified, isOwner, isAdmin));

                    ctx.Items[HttpCurrentUser.AppUserIdItemKey] = userId;

                    // OPS.M.2 — DB-wins per-tenant claim enrichment.
                    // OPS.M.8 §3.2 (D2) — also read the user's is_platform_admin
                    // bit on the same round-trip; stamp it onto HttpContext.Items
                    // and add a "PlatformAdmin" role claim so [Authorize] works.
                    var memberships = await db.Set<TenantMembership>()
                        .Where(m => m.UserId == userId && m.DeletedAt == null)
                        .Select(m => new { m.TenantId, m.Role, m.IsPrimary })
                        .ToListAsync(ctx.RequestAborted);

                    var isPlatformAdmin = await db.Users
                        .Where(u => u.Id == userId)
                        .Select(u => u.IsPlatformAdmin)
                        .FirstOrDefaultAsync(ctx.RequestAborted);

                    ctx.Items[HttpCurrentUser.PlatformAdminItemKey] = isPlatformAdmin;

                    if ((memberships.Count > 0 || isPlatformAdmin)
                        && ctx.User.Identity is ClaimsIdentity primaryIdentity)
                    {
                        foreach (var m in memberships)
                        {
                            // Duplicate role-name claims across tenants are OK — IsInRole is set membership.
                            primaryIdentity.AddClaim(new Claim(ClaimTypes.Role, m.Role));
                        }

                        if (isPlatformAdmin)
                        {
                            primaryIdentity.AddClaim(new Claim(
                                ClaimTypes.Role, HttpCurrentUser.PlatformAdminRole));
                        }

                        var primary = memberships.FirstOrDefault(m => m.IsPrimary);
                        if (primary is not null)
                        {
                            primaryIdentity.AddClaim(new Claim(
                                HttpCurrentUser.TenantIdClaimType, primary.TenantId.ToString()));
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
