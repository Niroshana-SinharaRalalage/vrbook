using MediatR;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Infrastructure.Persistence;

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

        // OPS.M.6 §3.1 (D1) — background-worker commands have no ICurrentUser.
        // The worker stamps TenantId from the row it's processing;
        // BackgroundCommandTenantScopeBehavior asserts non-empty downstream.
        if (request is IBackgroundCommand)
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

        // Slice OPS.M.10.2 F11.7.5.1 — BackgroundTenantScope fallback.
        // Some handlers (notably CancelBookingHandler at TransitionHandlers.cs)
        // open a BackgroundTenantScope with the row-resolved tenant id BEFORE
        // dispatching sub-commands. The classic case: a guest cancels their
        // booking. The guest is tenant-less (ICurrentUser.TenantId is null),
        // but the booking has a tenant. Without this bypass the sub-dispatch
        // of RefundForBookingCommand (ITenantScoped, stamped with
        // booking.TenantId) is rejected by the equality check below, even
        // though the handler has already declared the scope it's operating
        // in. The scope is the authoritative answer to "what tenant is this
        // operation against" and matches the M.4 gate's intent without
        // widening to anonymous bypass.
        var bgScope = BackgroundTenantScope.CurrentTenantId;
        if (currentUser.TenantId is null && bgScope is not null)
        {
            if (bgScope.Value != scoped.TenantId)
            {
                logger.LogWarning(
                    "Cross-tenant write rejected for {RequestType} (background scope): attempted={Attempted} scope={Scope}",
                    typeof(TRequest).Name, scoped.TenantId, bgScope);
                throw new CrossTenantAccessException(scoped.TenantId, bgScope);
            }
            logger.LogInformation(
                "BackgroundTenantScope bypass for {RequestType} on tenant {TenantId}",
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
    /// Slice OPS.M.8 §3.3 (D3) — bypass surface. Reads
    /// <see cref="ICurrentUser.IsPlatformAdmin"/>, which the
    /// <c>UserProvisioningMiddleware</c> materializes from
    /// <c>identity.users.is_platform_admin</c> per ADR-0014 DB-wins
    /// precedence. The bypass covers every <c>ITenantScoped</c> command —
    /// the operator can write across any tenant — and is audited by the
    /// existing <c>AuditLogBehavior</c> per §3.6 (D6).
    /// </summary>
    private static bool IsPlatformAdmin(ICurrentUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.IsPlatformAdmin;
    }
}
