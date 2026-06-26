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
        // OPS.M.2: Owner + Admin personas are seeded to the default tenant by
        // Slice5b_DevAuth_Default_Tenant_Membership; Guest is tenant-less by
        // design (per docs/MULTI_TENANCY_OPS_PLAN.md §1). Surface the tenantId
        // alongside the persona for the future tenant-switcher UX (OPS.M.7).
        const string defaultTenantId = "00000000-0000-0000-0000-000000000001";
        return Ok(new
        {
            current = current.Persona.ToString(),
            options = new[]
            {
                new { value = "Owner", displayName = DevAuthPersonas.Owner.DisplayName, email = DevAuthPersonas.Owner.Email, roles = new[] { "Owner", "tenant_admin" }, tenantId = (string?)defaultTenantId },
                new { value = "Guest", displayName = DevAuthPersonas.Guest.DisplayName, email = DevAuthPersonas.Guest.Email, roles = Array.Empty<string>(), tenantId = (string?)null },
                new { value = "Admin", displayName = DevAuthPersonas.Admin.DisplayName, email = DevAuthPersonas.Admin.Email, roles = new[] { "Owner", "Admin", "tenant_admin" }, tenantId = (string?)defaultTenantId },
            },
        });
    }

    [HttpGet("switch")]
    [HttpPost("switch")]
    public IActionResult Switch([FromQuery] string persona, [FromQuery] string? redirect)
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

        // Optional same-origin redirect so the handoff convention can be a single
        // URL: /api/v1/dev-auth/switch?persona=Guest&redirect=/properties/beach-villa
        // Web base URL is config; reject absolute external redirects defensively.
        if (!string.IsNullOrWhiteSpace(redirect))
        {
            var webBase = configuration["DevAuth:WebBaseUrl"]?.TrimEnd('/');
            string target;
            if (redirect.StartsWith('/'))
            {
                target = string.IsNullOrEmpty(webBase) ? redirect : webBase + redirect;
            }
            else if (!string.IsNullOrEmpty(webBase) &&
                     redirect.StartsWith(webBase, StringComparison.OrdinalIgnoreCase))
            {
                target = redirect;
            }
            else
            {
                return BadRequest(new { detail = "redirect must be a same-origin path starting with '/'." });
            }
            return Redirect(target);
        }

        return Ok(new { persona = parsed.ToString(), displayName = snapshot.DisplayName, email = snapshot.Email });
    }

    /// <summary>
    /// Slice 5 dev bridge: backdate a booking's CheckedOutAt so the daily
    /// completion sweep (predicate <c>CheckedOutAt &lt;= NOW() - 24h</c>) can
    /// fire on it during a same-day verification walk. DevAuth-only.
    /// </summary>
    [HttpPost("backdate-checked-out-at")]
    public async Task<IActionResult> BackdateCheckedOutAt(
        [FromQuery] Guid bookingId,
        [FromQuery] int hoursAgo,
        [FromServices] IConfiguration cfg,
        [FromServices] Npgsql.NpgsqlDataSource? dataSource,
        CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("DevAuth:AllowAnonymous"))
        {
            return NotFound();
        }
        if (hoursAgo < 1 || hoursAgo > 168)
        {
            return BadRequest(new { detail = "hoursAgo must be between 1 and 168 (one week)." });
        }
        var conn = cfg.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Postgres connection string not configured.");
        await using var c = new Npgsql.NpgsqlConnection(conn);
        await c.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        // BookingConfiguration maps Status with HasConversion<string>(), so the
        // column is character varying and must be compared to the enum NAME,
        // not the int value.
        cmd.CommandText = """
            UPDATE booking.bookings
            SET checked_out_at = NOW() - make_interval(hours => @hours)
            WHERE "Id" = @id
              AND status = 'CheckedOut'
            """;
        cmd.Parameters.AddWithValue("@id", bookingId);
        cmd.Parameters.AddWithValue("@hours", hoursAgo);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        if (rows == 0)
        {
            return NotFound(new { detail = "Booking not found OR not in CheckedOut state." });
        }
        return Ok(new { bookingId, checkedOutAtHoursAgo = hoursAgo });
    }

    /// <summary>
    /// Slice 4 dev bridge: repoint a DevAuth persona's User row at a real
    /// inbox. Future bookings placed by that persona land in the real mailbox
    /// because the notification handler resolves the email via IUserEmailLookup
    /// at queue time. DevAuth-only.
    /// </summary>
    [HttpPost("persona-email")]
    public async Task<IActionResult> SetPersonaEmail(
        [FromQuery] string persona,
        [FromQuery] string email,
        [FromServices] IMediator mediator,
        CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("DevAuth:AllowAnonymous"))
        {
            return NotFound();
        }
        if (!Enum.TryParse<DevAuthPersona>(persona, ignoreCase: true, out var parsed))
        {
            return BadRequest(new { detail = $"Unknown persona '{persona}'." });
        }
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            return BadRequest(new { detail = "email must look like an email." });
        }
        var snapshot = DevAuthPersonas.Get(parsed);
        await mediator.Send(new SetPersonaEmailCommand(snapshot.Oid, email.Trim()), ct);
        return Ok(new { persona = parsed.ToString(), email = email.Trim() });
    }
}
