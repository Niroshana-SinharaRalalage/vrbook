using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Domain.Common;
using VrBook.Modules.Notifications.Domain;
using VrBook.Modules.Notifications.Infrastructure.Persistence;

namespace VrBook.Modules.Notifications.Application.Commands;

/// <summary>
/// Slice 4 C5: admin "Retry" button. Resets a Failed or DeadLetter row to
/// <see cref="NotificationStatus.Queued"/> so the next worker tick picks it up.
/// Throws <see cref="NotFoundException"/> on unknown id and
/// <see cref="BusinessRuleViolationException"/> if the row is not in a retryable state.
/// </summary>
public sealed record RetryNotificationCommand(Guid Id) : IRequest<Unit>;

internal sealed class RetryNotificationHandler(
    NotificationsDbContext db,
    ILogger<RetryNotificationHandler> logger)
    : IRequestHandler<RetryNotificationCommand, Unit>
{
    public async Task<Unit> Handle(RetryNotificationCommand request, CancellationToken cancellationToken)
    {
        var row = await db.Logs.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("NotificationLog", request.Id);
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
