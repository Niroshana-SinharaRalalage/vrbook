using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Identity.Application.Settings;

namespace VrBook.Api.Controllers;

/// <summary>
/// VRB-210/211 — the admin settings surface. This slice ships the shared "Recent
/// changes" panel (VRB-211) that every settings screen renders; the per-property
/// cancellation-model endpoints land with VRB-215. <c>[Authorize]</c> — any admin
/// (Entra-local, ADR-0016); the section filter scopes what's shown.
/// </summary>
[Route("api/v1/admin/settings")]
[Tags("Admin — Settings")]
[Authorize]
public sealed class SettingsController(IMediator mediator) : ControllerBase
{
    [HttpGet("changes")]
    [SwaggerOperation(Summary = "Recent settings changes (audit-log projection) for the 'Recent changes' panel.")]
    [ProducesResponseType(typeof(IReadOnlyList<SettingsChangeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SettingsChangeDto>>> Changes(
        [FromQuery] string? section,
        [FromQuery] Guid? propertyId,
        CancellationToken ct = default) =>
        Ok(await mediator.Send(new GetSettingsChangesQuery(section, propertyId), ct));
}
