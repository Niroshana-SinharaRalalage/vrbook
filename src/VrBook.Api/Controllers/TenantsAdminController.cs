using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Identity.Application.Tenants.Commands;

namespace VrBook.Api.Controllers;

/// <summary>
/// OPS.M.5 §3.3 + §3.12 + §3.16 — tenant-admin operations on the caller's own
/// tenant. The <c>{tenantId}</c> route segment is gated by
/// <c>TenantAuthorizationBehavior</c> (OPS.M.4): a tenant-A admin cannot hit
/// tenant-B's endpoints.
///
/// <para>The <c>SetTenantPlatformFeeBps</c> endpoint is Super-Admin-only per
/// §3.16; it ships dormant in M.5 — until Slice OPS.M.8 lights up the
/// <c>IsPlatformAdmin</c> claim-based bypass, the only callers who pass the
/// behavior are tenant Owners adjusting their own rate (which is acceptable
/// for staging operator scripts; Slice OPS.M.8 ships the real UI).</para>
/// </summary>
[Route("api/v1/admin/tenants/{tenantId:guid}")]
[Tags("Tenant — Admin")]
[Authorize(Roles = "Owner,Admin")]
public sealed class TenantsAdminController(IMediator mediator, ICurrentUser currentUser) : ControllerBase
{
    private Guid CallerTenantId() => currentUser.TenantId
        ?? throw new ForbiddenException("Tenant admin action requires a tenant membership.");

    [HttpPost("stripe/onboard")]
    [SwaggerOperation(Summary = "Create the tenant's Stripe Connect Express account (idempotent on tenant id).")]
    [ProducesResponseType(typeof(OnboardTenantStripeResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<OnboardTenantStripeResult>> Onboard(
        Guid tenantId, [FromBody] OnboardTenantStripeRequest body, CancellationToken cancellationToken)
    {
        _ = tenantId; // route value; the behavior gates the caller's tenant scope.
        var result = await mediator.Send(
            new OnboardTenantStripeCommand(CallerTenantId(), body.Country ?? "US"),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("stripe/account-link")]
    [SwaggerOperation(Summary = "Generate a fresh 5-minute Stripe-hosted onboarding URL.")]
    [ProducesResponseType(typeof(GenerateStripeAccountLinkResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<GenerateStripeAccountLinkResult>> AccountLink(
        Guid tenantId, CancellationToken cancellationToken)
    {
        _ = tenantId;
        var result = await mediator.Send(
            new GenerateStripeAccountLinkCommand(CallerTenantId()),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("stripe/login-link")]
    [SwaggerOperation(Summary = "Magic-link to the tenant's Stripe Express dashboard.")]
    [ProducesResponseType(typeof(OpenStripeLoginLinkResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpenStripeLoginLinkResult>> LoginLink(
        Guid tenantId, CancellationToken cancellationToken)
    {
        _ = tenantId;
        var result = await mediator.Send(
            new OpenStripeLoginLinkCommand(CallerTenantId()),
            cancellationToken);
        return Ok(result);
    }

    // OPS.M.8 §4.4 — the dormant /platform-fee endpoint moved to
    // TenantsPlatformController under the PlatformAdmin role gate. The
    // Owner-self-set path is intentionally removed (Owners cannot adjust
    // the platform fee they pay).
}

public sealed record OnboardTenantStripeRequest(string? Country);

public sealed record SetPlatformFeeRequest(
    [property: System.Text.Json.Serialization.JsonRequired] int Bps);
