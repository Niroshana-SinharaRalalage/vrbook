using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Identity.Application.Tenants.Commands;
using VrBook.Modules.Identity.Application.Tenants.Queries;

namespace VrBook.Api.Controllers;

/// <summary>
/// OPS.M.8 §3.4 + §3.5 (D4/D5) — cross-tenant operator surface.
/// <c>[Authorize(Roles="PlatformAdmin")]</c> is the auth gate; every handler
/// also runs a defense-in-depth <c>IsPlatformAdmin</c> check per §7.
///
/// <para>The <c>{tenantId}</c> route segment is trusted as the operation's
/// <em>target</em> (not a tenant-scope-of-self gate); this is the ONE place
/// in the codebase where the URL's tenant id flows into a MediatR command,
/// and is safe only because the role gate is paired.</para>
/// </summary>
[Route("api/v1/admin/platform/tenants")]
[Tags("Platform — Super Admin")]
[Authorize(Roles = "PlatformAdmin")]
public sealed class TenantsPlatformController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "List every tenant (PlatformAdmin only).")]
    [ProducesResponseType(typeof(PlatformTenantListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PlatformTenantListResponse>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        Ok(await mediator.Send(
            new ListPlatformTenantsQuery(page, pageSize, status, search),
            cancellationToken));

    [HttpGet("{tenantId:guid}")]
    [SwaggerOperation(Summary = "Detail view of a single tenant (PlatformAdmin only).")]
    [ProducesResponseType(typeof(PlatformTenantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlatformTenantDto>> Get(
        Guid tenantId, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetPlatformTenantQuery(tenantId), cancellationToken));

    [HttpPost("{tenantId:guid}/suspend")]
    [SwaggerOperation(Summary = "Suspend a tenant. Active → Suspended.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Suspend(
        Guid tenantId, [FromBody] SuspendTenantRequest body, CancellationToken cancellationToken)
    {
        await mediator.Send(
            new SuspendTenantCommand(tenantId, body.Reason),
            cancellationToken);
        return NoContent();
    }

    [HttpPost("{tenantId:guid}/reactivate")]
    [SwaggerOperation(Summary = "Reactivate a suspended tenant. Suspended → Active.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reactivate(Guid tenantId, CancellationToken cancellationToken)
    {
        await mediator.Send(new ReactivateTenantCommand(tenantId), cancellationToken);
        return NoContent();
    }

    [HttpPut("{tenantId:guid}/platform-fee")]
    [SwaggerOperation(Summary = "Override the tenant's platform fee (basis points).")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPlatformFee(
        Guid tenantId, [FromBody] SetPlatformFeeRequest body, CancellationToken cancellationToken)
    {
        await mediator.Send(
            new SetTenantPlatformFeeBpsCommand(tenantId, body.Bps),
            cancellationToken);
        return NoContent();
    }
}

/// <summary>OPS.M.8 §4.1 — Suspend request body. Reason is required.</summary>
public sealed record SuspendTenantRequest(
    [property: System.Text.Json.Serialization.JsonRequired] string Reason);
