using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Admin.Application.Settings;

namespace VrBook.Api.Controllers;

/// <summary>
/// VRB-216 — platform-global product settings (PlatformAdmin only, per ADR-0016 +
/// M.15): cancellation tiers, platform-fee overrides, and tax posture. All writes are
/// audited via the settings command's <c>IAuditable</c> path (<c>settings.*</c>).
/// </summary>
[Route("api/v1/admin/platform/settings")]
[Tags("Admin — Platform settings")]
[Authorize(Roles = "PlatformAdmin")]
public sealed class PlatformSettingsController(IMediator mediator) : ControllerBase
{
    [HttpGet("cancellation-tiers")]
    [SwaggerOperation(Summary = "Get the platform-global cancellation tier schedule (PlatformAdmin).")]
    [ProducesResponseType(typeof(GlobalCancellationTiersDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<GlobalCancellationTiersDto>> GetTiers(CancellationToken ct) =>
        Ok(await mediator.Send(new GetGlobalTiersQuery(), ct));

    [HttpPut("cancellation-tiers")]
    [SwaggerOperation(Summary = "Set the platform-global cancellation tier schedule (PlatformAdmin).")]
    [ProducesResponseType(typeof(GlobalCancellationTiersDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<GlobalCancellationTiersDto>> SetTiers(
        [FromBody] SetGlobalTiersCommand command, CancellationToken ct) =>
        Ok(await mediator.Send(command, ct));

    // NOTE: the per-tenant platform fee is NOT a setting here — it's the single
    // source of truth on identity.tenants.PlatformFeeBps, set via the existing
    // PUT /api/v1/admin/platform/tenants/{tenantId}/platform-fee (TenantsPlatformController).
    // VRB-216 deliberately does not duplicate it.

    [HttpGet("tax-posture")]
    [SwaggerOperation(Summary = "Get the platform tax posture (PlatformAdmin).")]
    [ProducesResponseType(typeof(TaxPostureDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TaxPostureDto>> GetTax(CancellationToken ct) =>
        Ok(await mediator.Send(new GetTaxPostureQuery(), ct));

    [HttpPut("tax-posture")]
    [SwaggerOperation(Summary = "Set the platform tax posture (PlatformAdmin).")]
    [ProducesResponseType(typeof(TaxPostureDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TaxPostureDto>> SetTax(
        [FromBody] SetTaxPostureCommand command, CancellationToken ct) =>
        Ok(await mediator.Send(command, ct));
}
