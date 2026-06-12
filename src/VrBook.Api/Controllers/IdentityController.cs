using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Identity.Application.Users.Commands;
using VrBook.Modules.Identity.Application.Users.Queries;
using VrBook.Modules.Identity.Infrastructure.Auth;

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
}

/// <summary>
/// DevAuth persona switcher. Active only when DevAuth:AllowAnonymous=true; the
/// production Entra path ignores the cookie entirely. Used in browser demos to
/// flip between Owner / Guest / Admin without restarting the API.
/// </summary>
[Route("api/v1/dev-auth")]
[AllowAnonymous]
[Tags("DevAuth")]
public sealed class DevAuthController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("personas")]
    public ActionResult<object> Personas()
    {
        if (!configuration.GetValue<bool>("DevAuth:AllowAnonymous"))
        {
            return NotFound();
        }
        var current = DevAuthPersonas.Resolve(Request.Cookies[DevAuthPersonas.CookieName]);
        return Ok(new
        {
            current = current.Persona.ToString(),
            options = new[]
            {
                new { value = "Owner", displayName = DevAuthPersonas.Owner.DisplayName, email = DevAuthPersonas.Owner.Email, roles = new[] { "Owner" } },
                new { value = "Guest", displayName = DevAuthPersonas.Guest.DisplayName, email = DevAuthPersonas.Guest.Email, roles = Array.Empty<string>() },
                new { value = "Admin", displayName = DevAuthPersonas.Admin.DisplayName, email = DevAuthPersonas.Admin.Email, roles = new[] { "Owner", "Admin" } },
            },
        });
    }

    [HttpPost("switch")]
    public IActionResult Switch([FromQuery] string persona)
    {
        if (!configuration.GetValue<bool>("DevAuth:AllowAnonymous"))
        {
            return NotFound();
        }
        if (!Enum.TryParse<DevAuthPersona>(persona, ignoreCase: true, out var parsed))
        {
            return BadRequest(new { detail = $"Unknown persona '{persona}'. Valid: Owner, Guest, Admin." });
        }
        var snapshot = DevAuthPersonas.Get(parsed);
        Response.Cookies.Append(DevAuthPersonas.CookieName, parsed.ToString(), new CookieOptions
        {
            HttpOnly = false,        // FE reads this to render the current label
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(30),
            Path = "/",
        });
        return Ok(new { persona = parsed.ToString(), displayName = snapshot.DisplayName, email = snapshot.Email });
    }
}
