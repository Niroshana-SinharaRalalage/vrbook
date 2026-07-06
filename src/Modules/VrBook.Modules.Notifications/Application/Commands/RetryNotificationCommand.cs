using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Notifications.Domain;
using VrBook.Modules.Notifications.Infrastructure.Persistence;

namespace VrBook.Modules.Notifications.Application.Commands;

/// <summary>
/// Slice 4 C5: admin "Retry" button. Resets a Failed or DeadLetter row to
/// <see cref="NotificationStatus.Queued"/> so the next worker tick picks it up.
/// Throws <see cref="NotFoundException"/> on unknown id and
/// <see cref="BusinessRuleViolationException"/> if the row is not in a retryable state.
///
/// <para>
/// OPS.M.4 — admin-driven; controller stamps <c>TenantId</c> from
/// <c>currentUser.TenantId</c>. The behavior gates on the caller's tenant; the
/// per-row <c>notification_log.tenant_id</c> match is OPS.M.9 RLS concern.
/// </para>
/// </summary>
public sealed record RetryNotificationCommand(Guid Id, Guid TenantId) : IRequest<Unit>, ITenantScoped;

internal sealed class RetryNotificationHandler(
    NotificationsDbContext db,
    ICurrentUser currentUser,
    ILogger<RetryNotificationHandler> logger)
    : IRequestHandler<RetryNotificationCommand, Unit>
{
    public async Task<Unit> Handle(RetryNotificationCommand request, CancellationToken cancellationToken)
    {
        var row = await db.Logs.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("NotificationLog", request.Id);

        // Slice OPS.M.10.2 F7 (audit #15) — explicit row-level tenant
        // equality. The M.4 behavior gated cmd.TenantId == caller; that
        // bound the COMMAND's tenant. But the notification_log RLS policy
        // is the nullable variant (tenant_id IS NULL allowed), so a tenant
        // Admin could retry a guest's booking-confirmation/cancellation
        // email (which has tenant_id = NULL) and force a re-send to the
        // guest's inbox.
        //
        // Post-fix policy:
        //   * Tenant-stamped row (TenantId set): must equal cmd.TenantId.
        //   * NULL-tenant row (guest booking emails): PlatformAdmin only.
        if (row.TenantId is { } rowTenant)
        {
            if (rowTenant != request.TenantId)
            {
                throw new NotFoundException("NotificationLog", request.Id);
            }
            // Slice OPS.M.17 (M.15 follow-up B) — the controller-level
            // [Authorize(Roles="Admin")] gate was dropped in M.15.3; any
            // same-tenant authenticated user could otherwise reach this
            // handler. Notification retry is an admin action; require
            // tenant_admin in the row's tenant.
            if (!currentUser.HasTenantRole(rowTenant, "tenant_admin"))
            {
                throw new ForbiddenException(
                    "Notification retry requires tenant_admin role in the tenant.");
            }
        }
        else if (!currentUser.IsPlatformAdmin)
        {
            // NULL-tenant rows are platform-emitted (guest-flow emails); a
            // tenant Admin has no business retrying them.
            throw new ForbiddenException("Only PlatformAdmin may retry guest-flow notifications.");
        }

        try
        {
            row.Reset();
        }
        catch (InvalidOperationException ex)
        {
            throw new BusinessRuleViolationException("notification.retry_state", ex.Message);
        }
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Notification {LogId} reset to Queued by admin retry.",
            row.Id);
        return Unit.Value;
    }
}
