using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Identity.Application.Users.Queries;

namespace VrBook.Api.Controllers;

/// <summary>Admin — proposal §6.2.</summary>
[Route("api/v1/admin/users")]
[Tags("Admin — Users")]
[Authorize(Roles = "Owner,Admin")]
public sealed class AdminUsersController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "Search guests by display name or email.")]
    [ProducesResponseType(typeof(OffsetPagedResult<UserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<OffsetPagedResult<UserDto>>> Search(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default) =>
        Ok(await mediator.Send(new SearchUsersQuery(q, page, size), ct));
}

[Route("api/v1/admin/toggles")]
[Tags("Admin — Feature toggles")]
[Authorize(Roles = "Owner,Admin")]
public sealed class TogglesController : StubController
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FeatureToggleDto>), StatusCodes.Status200OK)]
    public IActionResult List() => NotImplementedYet("O1");

    [HttpPut("{key}")]
    [ProducesResponseType(typeof(FeatureToggleDto), StatusCodes.Status200OK)]
    public IActionResult Update(string key, [FromBody] UpdateFeatureToggleRequest request) =>
        NotImplementedYet("O1");
}

[Route("api/v1/admin/alerts")]
[Tags("Admin — Alerts")]
[Authorize(Roles = "Owner,Admin")]
public sealed class AlertsController : StubController
{
    [HttpGet]
    [SwaggerOperation(Summary = "Dashboard banner alerts (Sev3): sync stale, dispute opened, conflict pending.")]
    [ProducesResponseType(typeof(IReadOnlyList<AlertDto>), StatusCodes.Status200OK)]
    public IActionResult List() => NotImplementedYet("O1");

    [HttpPost("{id:guid}/dismiss")]
    public IActionResult Dismiss(Guid id) => NotImplementedYet("O1");
}
