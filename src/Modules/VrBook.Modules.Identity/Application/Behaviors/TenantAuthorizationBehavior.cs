using MediatR;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;

namespace VrBook.Modules.Identity.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that gates write-side commands by tenant equality.
/// Rejects any <see cref="ITenantScoped"/> request whose
/// <see cref="ITenantScoped.TenantId"/> does not match
/// <see cref="ICurrentUser.TenantId"/>.
///
/// <para>
/// Read-side scoping (cross-tenant query filtering) is OPS.M.9's RLS layer and
/// is explicitly out of scope here.
/// </para>
///
/// <para>
/// Pipeline order: <c>Validation → TenantAuthorization → AuditLog → handler</c>.
/// <see cref="AuditLogBehavior{TRequest,TResponse}"/> already writes <c>.failed</c>
/// audit rows on exception, so a cross-tenant rejection becomes an audited
/// <c>&lt;action&gt;.failed</c> entry with the <see cref="CrossTenantAccessException"/>
/// message in the <c>before_json</c> column. Per OPS_M_4_PLAN section 3.4.
/// </para>
///
/// <para>
/// Super Admin bypass per section 3.5: <see cref="IsPlatformAdmin"/> returns
/// <c>false</c> until Slice OPS.M.8 lights up the real claim source. <c>IsAdmin</c>
/// does NOT bypass per the user's Q1 resolution.
/// </para>
/// </summary>
public sealed class TenantAuthorizationBehavior<TRequest, TResponse>(
    ICurrentUser currentUser,
    ILogger<TenantAuthorizationBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ITenantScoped scoped)
        {
            return await next();
        }

        if (!currentUser.IsAuthenticated)
        {
            throw new ForbiddenException("Sign-in required.");
        }

        if (IsPlatformAdmin(currentUser))
        {
            logger.LogInformation(
                "PlatformAdmin bypass for {RequestType} on tenant {TenantId}",
                typeof(TRequest).Name, scoped.TenantId);
            return await next();
        }

        if (currentUser.TenantId is null || currentUser.TenantId.Value != scoped.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant write rejected for {RequestType}: attempted={Attempted} actual={Actual}",
                typeof(TRequest).Name, scoped.TenantId, currentUser.TenantId);
            throw new CrossTenantAccessException(scoped.TenantId, currentUser.TenantId);
        }

        return await next();
    }

    /// <summary>
    /// Slice OPS.M.4 ships this seam dormant per Q1 resolution. Slice OPS.M.8
    /// will replace this body with a real claim check (<c>user.HasRole("PlatformAdmin")</c>)
    /// once <c>users.is_platform_admin</c> flows into <see cref="ICurrentUser"/>.
    /// Until then no user bypasses; the parameter is referenced so the contract
    /// is locked at the call site.
    /// </summary>
    private static bool IsPlatformAdmin(ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return false;
    }
}
