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
        // guest emails of every other tenant's bookings - audit #16.
        //
        // Post-fix: non-PlatformAdmin callers see only their own
        // tenant's rows (no NULL-tenant orphans). PlatformAdmin sees
        // everything (RLS already permits via the GUC fallback).
        //
        // Follow-up note: the RLS policy itself could be tightened to
        // gate NULL rows on is_platform_admin; deferred because the
        // policy change has cross-module implications (every nullable-
        // tenant table) and needs a product decision per audit #16.b.
        var q = db.Logs.AsNoTracking();
        if (!currentUser.IsPlatformAdmin)
        {
            var callerTenant = currentUser.TenantId
                ?? throw new ForbiddenException("Notification log requires a tenant membership.");
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
