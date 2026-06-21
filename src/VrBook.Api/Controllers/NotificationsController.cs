using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VrBook.Modules.Notifications.Application.Commands;
using VrBook.Modules.Notifications.Application.Queries;
using VrBook.Modules.Notifications.Domain;

namespace VrBook.Api.Controllers;

/// <summary>
/// Slice 4 — admin view + retry endpoint for the notification log. The worker
/// (cron <c>*/2 * * * *</c>) drains Queued rows; admins use this surface to
/// inspect Failed/DeadLetter rows and re-queue them.
/// </summary>
[Route("api/v1/admin/notifications")]
[Tags("Notifications — Admin")]
[Authorize(Roles = "Admin")]
public sealed class AdminNotificationsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationLogDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<NotificationLogDto>>> List(
        [FromQuery] NotificationStatus? status,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(new ListNotificationLogQuery(status, limit), cancellationToken));

    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RetryNotificationCommand(id), cancellationToken);
        return NoContent();
    }
}
