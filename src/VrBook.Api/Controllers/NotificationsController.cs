using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VrBook.Modules.Notifications.Application.Queries;
using VrBook.Modules.Notifications.Domain;

namespace VrBook.Api.Controllers;

/// <summary>A9 v1 — admin view of the notification log. Actual ACS email
/// dispatch (A9.2/A9.3) is deferred until the resource is provisioned in
/// Bicep; rows are persisted in <c>Queued</c> state for the worker to drain.</summary>
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
}
