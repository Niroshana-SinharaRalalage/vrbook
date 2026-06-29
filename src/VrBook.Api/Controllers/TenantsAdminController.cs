using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
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
public sealed class TenantsAdminController(IMediator mediator) : ControllerBase
{
    // OPS.M.10.2 F-residual (audit follow-up): pass the ROUTE tenantId into
    // the command instead of `CallerTenantId()`. Previously the controller
    // substituted the caller's own tenant — so when OwnerB POSTed to
    // /api/v1/admin/tenants/{tenantA}/stripe/onboard, the dispatched command
    // carried tenantId = B, the M.4 TenantAuthorizationBehavior compared
    // currentUser.TenantId (B) == cmd.TenantId (B) and PASSED, allowing the
    // handler to operate against the wrong tenant. Latent cross-tenant
    // write hole, not just a test-shape gap.

    [HttpPost("stripe/onboard")]
    [SwaggerOperation(Summary = "Create the tenant's Stripe Connect Express account (idempotent on tenant id).")]
    [ProducesResponseType(typeof(OnboardTenantStripeResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<OnboardTenantStripeResult>> Onboard(
        Guid tenantId, [FromBody] OnboardTenantStripeRequest body, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new OnboardTenantStripeCommand(tenantId, body.Country ?? "US"),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("stripe/account-link")]
    [SwaggerOperation(Summary = "Generate a fresh 5-minute Stripe-hosted onboarding URL.")]
    [ProducesResponseType(typeof(GenerateStripeAccountLinkResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<GenerateStripeAccountLinkResult>> AccountLink(
        Guid tenantId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GenerateStripeAccountLinkCommand(tenantId),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("stripe/login-link")]
    [SwaggerOperation(Summary = "Magic-link to the tenant's Stripe Express dashboard.")]
    [ProducesResponseType(typeof(OpenStripeLoginLinkResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpenStripeLoginLinkResult>> LoginLink(
        Guid tenantId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new OpenStripeLoginLinkCommand(tenantId),
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Slice OPS.M.10.2 F11.4 — re-pull the tenant's Stripe
    /// charges_enabled + payouts_enabled flags and re-run the domain
    /// state machine. Use when the account.updated webhook is delayed.
    /// </summary>
    [HttpPost("stripe/refresh-readiness")]
    [SwaggerOperation(Summary = "Re-pull Stripe Connect readiness flags + re-run state machine.")]
    [ProducesResponseType(typeof(RefreshStripeReadinessResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<RefreshStripeReadinessResult>> RefreshStripeReadiness(
        Guid tenantId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new RefreshStripeReadinessCommand(tenantId),
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
