using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VrBook.Modules.Identity.Infrastructure.Auth;

/// <summary>
/// Authentication handler used ONLY when <c>DevAuth:AllowAnonymous</c> is true (intended
/// for local dev when no AD B2C tenant is configured). Authenticates every request with
/// one of a small fixed set of personas (<see cref="DevAuthPersona"/>); the active persona
/// is selected by the <c>vrbook-dev-persona</c> cookie. Without a cookie the handler
/// defaults to Owner so historical demo scripts still work.
/// MUST be disabled in non-Development environments.
/// </summary>
public sealed class DevAuthOptions : AuthenticationSchemeOptions
{
    /// <summary>Defaults to false. Set true via <c>DevAuth:AllowAnonymous</c>.</summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Built-in DevAuth personas. The OIDs are stable so seeded properties / bookings
/// can be re-attributed across persona switches without DB reshuffling.
/// </summary>
public enum DevAuthPersona
{
    Owner = 0,
    Guest = 1,
    Admin = 2,
}

public static class DevAuthPersonas
{
    public sealed record Snapshot(
        DevAuthPersona Persona,
        string Oid,
        string Email,
        string DisplayName,
        bool IsOwner,
        bool IsAdmin);

    public static readonly Snapshot Owner = new(
        DevAuthPersona.Owner,
        "e98aa66c-06c4-4e5f-83c9-523e211afd9b",
        "dev-owner@vrbook.local",
        "Dev Owner",
        IsOwner: true,
        IsAdmin: false);

    public static readonly Snapshot Guest = new(
        DevAuthPersona.Guest,
        "11111111-1111-1111-1111-111111111111",
        "dev-guest@vrbook.local",
        "Dev Guest",
        IsOwner: false,
        IsAdmin: false);

    public static readonly Snapshot Admin = new(
        DevAuthPersona.Admin,
        "22222222-2222-2222-2222-222222222222",
        "dev-admin@vrbook.local",
        "Dev Admin",
        IsOwner: true,  // Admin sees everything, also acts as owner of admin-created properties
        IsAdmin: true);

    public const string CookieName = "vrbook-dev-persona";

    public static Snapshot Get(DevAuthPersona persona) => persona switch
    {
        DevAuthPersona.Guest => Guest,
        DevAuthPersona.Admin => Admin,
        _ => Owner,
    };

    public static Snapshot Resolve(string? cookieValue) =>
        Enum.TryParse<DevAuthPersona>(cookieValue, ignoreCase: true, out var p) ? Get(p) : Owner;
}

public sealed class DevAuthHandler(
    IOptionsMonitor<DevAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<DevAuthOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Options.Enabled)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var cookie = Request.Cookies[DevAuthPersonas.CookieName];
        var persona = DevAuthPersonas.Resolve(cookie);

        var claims = new List<Claim>
        {
            new("oid", persona.Oid),
            new(ClaimTypes.NameIdentifier, persona.Oid),
            new(ClaimTypes.Name, persona.DisplayName),
            new("name", persona.DisplayName),
            new(ClaimTypes.Email, persona.Email),
            new("emails", persona.Email),
            new("email_verified", "true"),
            new("extension_isOwner", persona.IsOwner ? "true" : "false"),
            new("extension_isAdmin", persona.IsAdmin ? "true" : "false"),
            new("dev_persona", persona.Persona.ToString()),
        };

        if (persona.IsOwner)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Owner"));
        }

        if (persona.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
