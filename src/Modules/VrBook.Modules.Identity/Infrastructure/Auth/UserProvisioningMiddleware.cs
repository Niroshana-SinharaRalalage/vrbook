using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using VrBook.Modules.Identity.Application.Users.Commands;

namespace VrBook.Modules.Identity.Infrastructure.Auth;

/// <summary>
/// On every authenticated request, ensures a row exists in <c>identity.users</c> for the
/// caller's B2C <c>oid</c>. First-login provisions the row; subsequent calls refresh
/// LastLoginAt + DisplayName + EmailVerified from the latest token. Stamps
/// <c>HttpCurrentUser.AppUserIdItemKey</c> on <see cref="HttpContext.Items"/> so downstream
/// handlers can read <see cref="VrBook.Contracts.Interfaces.ICurrentUser.UserId"/>.
/// </summary>
public sealed class UserProvisioningMiddleware(RequestDelegate next, ILogger<UserProvisioningMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx, IMediator mediator)
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
