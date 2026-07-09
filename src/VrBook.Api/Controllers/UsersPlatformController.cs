using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Identity.Application.Users.Commands;

namespace VrBook.Api.Controllers;

/// <summary>
/// Slice OPS.M.22 §3 — cross-tenant operator surface for pre-seeding
/// admin <c>identity.users</c> rows BEFORE the admin's first sign-in.
/// Companion to <see cref="TenantsPlatformController"/> — that one owns
/// tenant lifecycle + membership add for EXISTING users; this one owns
/// pre-first-sign-in admin provisioning (the chicken-and-egg case).
///
/// <para><c>[Authorize(Roles="PlatformAdmin")]</c> is the auth gate; the
/// underlying handler defense-in-depths on <c>ICurrentUser.IsPlatformAdmin</c>
/// via <c>TenantAuthorizationBehavior</c> (bypass for platform-scoped
/// commands per OPS.M.8 §7).</para>
///
/// <para>The very-first platform admin bootstrap (nothing to authenticate
/// against yet) does NOT go through this endpoint — it uses the
/// <c>vrbook-admin --first</c> PowerShell cmdlet + direct SQL via
/// managed identity per plan §6.</para>
/// </summary>
[Route("api/v1/admin/platform/users")]
[Tags("Platform — Super Admin")]
[Authorize(Roles = "PlatformAdmin")]
public sealed class UsersPlatformController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Pre-create an <c>identity.users</c> row for an admin BEFORE first sign-in.
    /// Idempotent on normalized email: 201 for a fresh insert, 200 for an
    /// idempotent re-hit with merged memberships, 409 if the email is
    /// already linked to a non-pre-seeded (guest) row.
    /// </summary>
    [HttpPost("seed")]
    [SwaggerOperation(Summary = "Pre-seed an admin's identity.users row before first sign-in (PlatformAdmin only).")]
    [ProducesResponseType(typeof(SeedAdminUserResult), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(SeedAdminUserResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SeedAdminUserResult>> Seed(
        [FromBody] SeedAdminUserRequest body, CancellationToken cancellationToken)
    {
        var memberships = body.TenantMemberships ?? Array.Empty<SeedAdminUserTenantMembership>();
        var result = await mediator.Send(
            new SeedAdminUserCommand(
                body.Email,
                body.DisplayName,
                body.IsPlatformAdmin,
                memberships),
            cancellationToken);
        return result.Created
            ? StatusCode(StatusCodes.Status201Created, result)
            : Ok(result);
    }
}
