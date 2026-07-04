using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Identity.Application.Tenants.Queries;
using VrBook.Modules.Identity.Application.Users.Commands;
using VrBook.Modules.Identity.Application.Users.Queries;

namespace VrBook.Api.Controllers;

/// <summary>Identity — proposal §6.2. Owned by Agent A1.</summary>
[Route("api/v1/me")]
[Authorize]
[Tags("Identity")]
public sealed class IdentityController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "Get the current user's profile.")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> Get(CancellationToken ct) =>
        Ok(await mediator.Send(new GetMeQuery(), ct));

    [HttpPut]
    [SwaggerOperation(Summary = "Update the current user's profile.")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserDto>> Update(
        [FromBody] UpdateProfileRequest request, CancellationToken ct) =>
        Ok(await mediator.Send(new UpdateProfileCommand(request.DisplayName, request.Phone), ct));

    [HttpDelete]
    [SwaggerOperation(Summary = "Self-deactivate (GDPR-ready). Soft-deletes the profile.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(CancellationToken ct)
    {
        await mediator.Send(new DeactivateMeCommand(), ct);
        return NoContent();
    }

    /// <summary>
    /// OPS.M.7 §3.2 (D2) — read-side projection of the caller's tenant for
    /// the onboarding wizard. Onboarding-progress is server-derived; the
    /// web client never re-computes <c>NextStep</c>. <c>Cache-Control: no-store</c>
    /// per §3.2 so the polling loop after Stripe return sees fresh state.
    /// </summary>
    [HttpGet("tenant")]
    [Authorize(Roles = "Owner,Admin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [SwaggerOperation(Summary = "Get the caller's tenant + onboarding progress (OPS.M.7).")]
    [ProducesResponseType(typeof(MeTenantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MeTenantDto>> GetTenant(CancellationToken ct) =>
        Ok(await mediator.Send(new GetMyTenantQuery(), ct));

    /// <summary>
    /// Slice OPS.M.13.5 — list every tenant the caller has active membership in.
    /// The SPA's post-sign-in callback calls this to route based on membership
    /// count (0/1/N) per <c>docs/OPS_M_13_IDENTITY_REDESIGN_PLAN.md</c> §3.2.
    /// Not <c>[Authorize(Roles = "Owner,Admin")]</c> — the picker needs to
    /// answer "which tenants CAN I sign into" for any authenticated human,
    /// including PlatformAdmins with zero tenant memberships.
    /// </summary>
    [HttpGet("tenants")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [SwaggerOperation(Summary = "List every tenant the caller has active membership in (OPS.M.13.5).")]
    [ProducesResponseType(typeof(MyTenantsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MyTenantsResponse>> GetTenants(CancellationToken ct) =>
        Ok(await mediator.Send(new GetMyTenantsQuery(), ct));
}

