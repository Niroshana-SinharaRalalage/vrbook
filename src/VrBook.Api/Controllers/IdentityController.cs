using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Identity.Application.Tenants.Queries;
using VrBook.Modules.Identity.Application.Users.Commands;
using VrBook.Modules.Identity.Application.Users.Queries;
using VrBook.Modules.Identity.Infrastructure.Auth;
using VrBook.Modules.Identity.Infrastructure.Persistence;

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

    /// <summary>
    /// OPS.M.2 — diagnostic helper that reports the calling principal's resolved
    /// <see cref="ICurrentUser.TenantId"/> and <see cref="ICurrentUser.HasTenantRole"/>
    /// answers for the default tenant + a known-bad tenant id. Gated by the same
    /// <c>DevAuth:AllowAnonymous</c> flag as the rest of this controller so it 404s
    /// in production. Primary consumer is the OPS.M.2 integration test pack
    /// (<c>TenantClaimWiringTests.cs</c>) which uses this to assert the middleware
    /// enrichment + DB-wins precedence without having to spin up a MediatR handler.
    /// </summary>
    [HttpGet("current-tenant")]
    public ActionResult<object> CurrentTenant([FromServices] ICurrentUser currentUser)
    {
        if (!configuration.GetValue<bool>("DevAuth:AllowAnonymous"))
        {
            return NotFound();
        }
        var defaultTenant = new Guid("00000000-0000-0000-0000-000000000001");
        var randomTenant = new Guid("99999999-9999-9999-9999-999999999999");
        return Ok(new
        {
            tenantId = currentUser.TenantId,
            isTenantAdminOfDefault = currentUser.HasTenantRole(defaultTenant, "tenant_admin"),
            isTenantAdminOfRandom = currentUser.HasTenantRole(randomTenant, "tenant_admin"),
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
        [FromServices] Microsoft.Extensions.Hosting.IHostEnvironment hostEnv,
        [FromServices] Npgsql.NpgsqlDataSource? dataSource,
        CancellationToken ct)
    {
        // Slice OPS.M.10.2 F8 (audit #21) — defense-in-depth prod guard.
        // This raw-SQL UPDATE on booking.bookings bypasses EF entirely
        // (no GUC, no domain events). Same risk class as #20: a Production
        // config flip on DevAuth:AllowAnonymous would expose it.
        if (hostEnv.IsProduction())
        {
            return NotFound();
        }
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

    /// <summary>
    /// Slice OPS.M.10.2 F11.2 dev bridge — stub a tenant's Stripe Connect
    /// readiness without running the real sandbox onboarding flow. Used by
    /// staging operators when the Stripe-hosted form flakes OR when an
    /// air-gapped local dev environment needs a payment-ready tenant.
    ///
    /// <para><b>THREE GUARDS</b> (each must pass; any missing = 404):</para>
    /// <list type="number">
    ///   <item><c>IHostEnvironment.IsProduction()</c> false.</item>
    ///   <item><c>DevAuth:AllowAnonymous = true</c> (the wider DevAuth gate).</item>
    ///   <item><c>DevAuth:AllowStripeStub = true</c> (opt-in per-call flag;
    ///         defaults <c>false</c>, never set via Bicep, only set by
    ///         <c>az containerapp update --set-env-vars</c> for the duration
    ///         of an operator walk).</item>
    /// </list>
    ///
    /// <para>What it does: looks up the tenant under
    /// <c>RlsBypassScope.Enter()</c> (dev-bridge has no caller-tenant
    /// claim), calls <c>Tenant.SetStripeAccount</c> with a stub id of the
    /// form <c>acct_stub_{tenantId:N}</c> if no real account is present,
    /// then invokes <c>IConnectAccountReadinessUpdater.UpdateAsync</c>
    /// (the same surface the Stripe <c>account.updated</c> webhook
    /// invokes — see <c>HandleStripeWebhookCommand.cs:20</c>) so the
    /// tenant transitions <c>PendingOnboarding → Active</c> via the
    /// domain state machine.</para>
    /// </summary>
    [HttpPost("stub-stripe-readiness")]
    public async Task<IActionResult> StubStripeReadiness(
        [FromBody] StubStripeReadinessRequest body,
        [FromServices] IdentityDbContext db,
        [FromServices] IConnectAccountReadinessUpdater readinessUpdater,
        [FromServices] Microsoft.Extensions.Hosting.IHostEnvironment hostEnv,
        CancellationToken ct)
    {
        if (hostEnv.IsProduction())
        {
            return NotFound();
        }
        if (!configuration.GetValue<bool>("DevAuth:AllowAnonymous"))
        {
            return NotFound();
        }
        if (!configuration.GetValue<bool>("DevAuth:AllowStripeStub"))
        {
            return NotFound();
        }
        if (body is null || body.TenantId == Guid.Empty)
        {
            return BadRequest(new { detail = "tenantId required." });
        }

        // The dev-bridge has no caller-tenant claim; open the bypass
        // scope inline. RlsBypassCallSiteAllowlistTests is constructor-
        // injection-scoped and is unaffected by static AsyncLocal entry.
        using var bypass = RlsBypassScope.Enter();

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == body.TenantId, ct);
        if (tenant is null)
        {
            return NotFound(new { detail = $"Tenant '{body.TenantId}' not found." });
        }

        var stripeAccountId = body.StripeAccountId is { Length: > 0 } supplied
            ? supplied.Trim()
            : $"acct_stub_{body.TenantId:N}";

        if (string.IsNullOrEmpty(tenant.StripeAccountId))
        {
            tenant.SetStripeAccount(stripeAccountId);
            await db.SaveChangesAsync(ct);
        }
        else
        {
            // Tenant already has an account (real or prior stub). Use the
            // existing one for the readiness update so the lookup matches.
            stripeAccountId = tenant.StripeAccountId;
        }

        var matched = await readinessUpdater.UpdateAsync(
            stripeAccountId,
            chargesEnabled: body.ChargesEnabled,
            payoutsEnabled: body.PayoutsEnabled,
            ct);

        return Ok(new
        {
            tenantId = body.TenantId,
            stripeAccountId,
            chargesEnabled = body.ChargesEnabled,
            payoutsEnabled = body.PayoutsEnabled,
            readinessUpdated = matched,
        });
    }
}

/// <summary>
/// Slice OPS.M.10.2 F11.2 — request body for <c>POST /api/v1/dev-auth/stub-stripe-readiness</c>.
/// </summary>
public sealed record StubStripeReadinessRequest(
    [property: System.Text.Json.Serialization.JsonRequired] Guid TenantId,
    string? StripeAccountId,
    bool ChargesEnabled = true,
    bool PayoutsEnabled = true);
