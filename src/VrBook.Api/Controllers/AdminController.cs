using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Api.Common;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Admin.Application.FeatureFlags.Commands;
using VrBook.Modules.Admin.Application.FeatureFlags.Queries;
using VrBook.Modules.Identity.Application.Users.Queries;

namespace VrBook.Api.Controllers;

/// <summary>Admin — proposal §6.2.</summary>
[Route("api/v1/admin/users")]
[Tags("Admin — Users")]
[Authorize]
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

/// <summary>
/// VRB-203 (gap G13) — real feature-flag admin surface, PlatformAdmin only. Replaces
/// the A0 501 stubs: lists flags with effective values and sets global overrides that
/// take effect without a redeploy (DB override → resolver cache invalidation).
/// </summary>
[Route("api/v1/admin/toggles")]
[Tags("Admin — Feature toggles")]
[Authorize(Roles = "PlatformAdmin")]
public sealed class TogglesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ExemptFromCrossTenantMatrix("Global platform feature flags — PlatformAdmin-gated, no per-tenant dimension. Auth shape covered by TogglesContractTests.")]
    [SwaggerOperation(Summary = "List feature flags with their effective values (PlatformAdmin only).")]
    [ProducesResponseType(typeof(IReadOnlyList<FeatureToggleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FeatureToggleDto>>> List(CancellationToken ct) =>
        Ok(await mediator.Send(new ListFeatureFlagsQuery(), ct));

    [HttpPut("{key}")]
    [ExemptFromCrossTenantMatrix("Global platform feature flags — PlatformAdmin-gated, no per-tenant dimension. Auth shape covered by TogglesContractTests.")]
    [SwaggerOperation(Summary = "Set a global feature-flag override (PlatformAdmin only).")]
    [ProducesResponseType(typeof(FeatureToggleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FeatureToggleDto>> Update(
        string key, [FromBody] UpdateFeatureToggleRequest request, CancellationToken ct) =>
        Ok(await mediator.Send(new SetFeatureFlagCommand(key, request.Scope, request.Enabled), ct));
}

[Route("api/v1/admin/alerts")]
[Tags("Admin — Alerts")]
[Authorize]
public sealed class AlertsController : StubController
{
    [HttpGet]
    [SwaggerOperation(Summary = "Dashboard banner alerts (Sev3): sync stale, dispute opened, conflict pending.")]
    [ProducesResponseType(typeof(IReadOnlyList<AlertDto>), StatusCodes.Status200OK)]
    public IActionResult List() => NotImplementedYet("O1");

    [HttpPost("{id:guid}/dismiss")]
    public IActionResult Dismiss(Guid id) => NotImplementedYet("O1");
}
