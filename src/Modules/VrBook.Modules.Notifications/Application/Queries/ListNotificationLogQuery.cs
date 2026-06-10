using MediatR;
using Microsoft.EntityFrameworkCore;
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

internal sealed class ListNotificationLogHandler(NotificationsDbContext db)
    : IRequestHandler<ListNotificationLogQuery, IReadOnlyList<NotificationLogDto>>
{
    public async Task<IReadOnlyList<NotificationLogDto>> Handle(ListNotificationLogQuery request, CancellationToken cancellationToken)
    {
        var q = db.Logs.AsNoTracking();
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
