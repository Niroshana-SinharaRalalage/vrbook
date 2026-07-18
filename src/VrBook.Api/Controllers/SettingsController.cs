using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Catalog.Application.Properties.Commands;
using VrBook.Modules.Identity.Application.Settings;

namespace VrBook.Api.Controllers;

/// <summary>
/// VRB-210/211/215 — the tenant-admin settings surface. Ships the shared "Recent
/// changes" panel (VRB-211) + per-property cancellation-model selection (VRB-215).
/// <c>[Authorize]</c> — any admin (Entra-local, ADR-0016); tenant-scoped writes carry
/// the caller's tenant (validated by TenantAuthorizationBehavior + RLS).
/// </summary>
[Route("api/v1/admin/settings")]
[Tags("Admin — Settings")]
[Authorize]
public sealed class SettingsController(IMediator mediator, ICurrentUser currentUser) : ControllerBase
{
    private Guid CallerTenantId() => currentUser.TenantId
        ?? throw new ForbiddenException("This action requires a tenant membership.");

    [HttpGet("changes")]
    [SwaggerOperation(Summary = "Recent settings changes (audit-log projection) for the 'Recent changes' panel.")]
    [ProducesResponseType(typeof(IReadOnlyList<SettingsChangeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SettingsChangeDto>>> Changes(
        [FromQuery] string? section,
        [FromQuery] Guid? propertyId,
        CancellationToken ct = default) =>
        Ok(await mediator.Send(new GetSettingsChangesQuery(section, propertyId), ct));

    [HttpGet("cancellation/{propertyId:guid}")]
    [SwaggerOperation(Summary = "Get a property's cancellation-model selection + the resolved platform tiers (tenant-admin).")]
    [ProducesResponseType(typeof(PropertyCancellationSettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PropertyCancellationSettingsDto>> GetCancellation(
        Guid propertyId, CancellationToken ct = default) =>
        Ok(await mediator.Send(new GetPropertyCancellationSettingsQuery(CallerTenantId(), propertyId), ct));

    [HttpPut("cancellation/{propertyId:guid}")]
    [SwaggerOperation(Summary = "Set a property's cancellation model (tenant-admin). Price/tiers are platform-set.")]
    [ProducesResponseType(typeof(PropertyCancellationSettingsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PropertyCancellationSettingsDto>> SetCancellation(
        Guid propertyId, [FromBody] SetPropertyCancellationRequest body, CancellationToken ct = default) =>
        Ok(await mediator.Send(new SetPropertyCancellationModelCommand(CallerTenantId(), propertyId, body.Model), ct));
}

/// <summary>Body for the per-property cancellation-model selection (property id is the route param).</summary>
public sealed record SetPropertyCancellationRequest([property: JsonRequired] CancellationModel Model);
