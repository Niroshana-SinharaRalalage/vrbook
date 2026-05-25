using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VrBook.Modules.Identity.Infrastructure.Auth;

/// <summary>
/// Authentication handler used ONLY when <c>DevAuth:AllowAnonymous</c> is true (intended
/// for local dev when no AD B2C tenant is configured). Authenticates every request as a
/// synthetic "Dev Owner" so the API surface is reachable end-to-end without a real token.
/// MUST be disabled in non-Development environments.
/// </summary>
public sealed class DevAuthOptions : AuthenticationSchemeOptions
{
    /// <summary>Defaults to false. Set true via <c>DevAuth:AllowAnonymous</c>.</summary>
    public bool Enabled { get; set; }
    public string FakeOid { get; set; } = "dev-owner-00000000";
    public string FakeEmail { get; set; } = "dev@vrbook.local";
    public string FakeDisplayName { get; set; } = "Dev Owner";
    public bool IsOwner { get; set; } = true;
    public bool IsAdmin { get; set; } = true;
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

        var claims = new List<Claim>
        {
            new("oid", Options.FakeOid),
            new(ClaimTypes.NameIdentifier, Options.FakeOid),
            new(ClaimTypes.Name, Options.FakeDisplayName),
            new("name", Options.FakeDisplayName),
            new(ClaimTypes.Email, Options.FakeEmail),
            new("emails", Options.FakeEmail),
            new("email_verified", "true"),
            new("extension_isOwner", Options.IsOwner ? "true" : "false"),
            new("extension_isAdmin", Options.IsAdmin ? "true" : "false"),
        };

        if (Options.IsOwner)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Owner"));
        }

        if (Options.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
