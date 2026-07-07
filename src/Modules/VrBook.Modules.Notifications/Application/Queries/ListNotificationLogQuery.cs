using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Notifications.Domain;
using VrBook.Modules.Notifications.Infrastructure.Persistence;

namespace VrBook.Modules.Notifications.Application.Queries;

public sealed record ListNotificationLogQuery(NotificationStatus? Status, int Limit = 100)
    : IRequest<IReadOnlyList<NotificationLogDto>>;

public sealed record NotificationLogDto(
    Guid Id,
    NotificationKind Kind,
    NotificationStatus Status,
    Guid RecipientUserId,
    string RecipientEmail,
    string Subject,
    int RetryCount,
    string? LastError,
    DateTimeOffset? SentAt,
    DateTimeOffset CreatedAt);

internal sealed class ListNotificationLogHandler(
    NotificationsDbContext db,
    ICurrentUser currentUser)
    : IRequestHandler<ListNotificationLogQuery, IReadOnlyList<NotificationLogDto>>
{
    public async Task<IReadOnlyList<NotificationLogDto>> Handle(ListNotificationLogQuery request, CancellationToken cancellationToken)
    {
        // Slice OPS.M.10.2 F7 (audit #16) — explicit tenant scope on the
        // listing. The notification_log RLS policy is the nullable
        // variant (tenant_id IS NULL allowed) which is intentional for
        // guest-flow emails (Slice 4 sends booking confirmations to guest
        // inboxes BEFORE the guest has a tenant claim). But that means
        // every tenant Admin's /admin/notifications page was leaking the
        // guest emails of every other tenant's bookings — audit #16.
        //
        // Slice 4.V2.3 (M.17 parity) — post-M.15.3 the controller carries
        // plain `[Authorize]`; without an explicit handler-level check any
        // authenticated same-tenant caller (guest with a membership) could
        // enumerate the notification log. Mirror the RetryNotificationHandler
        // two-branch pattern: PlatformAdmin sees everything; tenant_admin
        // sees their tenant only; anyone else → 403.
        //
        // §7-Q3-A locked: keep the app-layer filter as the enforcement
        // point. RLS tighten deferred to a dedicated OPS.M.10.1 slice
        // (audit #16.b).
        var q = db.Logs.AsNoTracking();
        if (!currentUser.IsPlatformAdmin)
        {
            if (currentUser.TenantId is not { } callerTenant)
            {
                throw new ForbiddenException("Notification log requires a tenant membership.");
            }
            if (!currentUser.HasTenantRole(callerTenant, "tenant_admin"))
            {
                throw new ForbiddenException(
                    "Notification log requires tenant_admin role in the tenant.");
            }
            q = q.Where(x => x.TenantId == callerTenant);
        }
        if (request.Status.HasValue)
        {
            var s = request.Status.Value;
            q = q.Where(x => x.Status == s);
        }
        var rows = await q
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(request.Limit, 1, 500))
            .ToListAsync(cancellationToken);
        return rows.Select(x => new NotificationLogDto(
            x.Id, x.Kind, x.Status, x.RecipientUserId, x.RecipientEmail,
            x.Subject, x.RetryCount, x.LastError, x.SentAt, x.CreatedAt)).ToArray();
    }
}
