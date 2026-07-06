using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VrBook.Api.IntegrationTests.Auth;

/// <summary>
/// Slice OPS.M.14.1 — options carrier for <see cref="TestAuthHandler"/>.
/// Holds the persona lookup dictionary keyed by <c>X-Test-Persona</c> header
/// value.
/// </summary>
public sealed class TestAuthOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// Persona lookup — header value → snapshot. Fixture-owned; empty by
    /// default (any request is rejected as NoResult) so a fixture that
    /// forgets to set it fails loud rather than silently authenticating.
    /// Cannot be <c>required</c> — the <c>new()</c> constraint on
    /// <see cref="AuthenticationHandler{TOptions}"/> forbids it.
    /// </summary>
    public IReadOnlyDictionary<string, TestPersona> Personas { get; set; } =
        new Dictionary<string, TestPersona>();
}

/// <summary>
/// Slice OPS.M.14.1 — test-only replacement for the production JwtBearer
/// scheme. Registered via <c>ConfigureTestServices</c> under
/// <see cref="JwtBearerDefaults.AuthenticationScheme"/> so <c>[Authorize]</c>
/// decorators route through this handler exactly as production does. There
/// is no production-code seam: the type lives entirely in the integration-test
/// project.
///
/// <para>Behavior:</para>
/// <list type="bullet">
///   <item>No <c>Authorization: Bearer</c> header → <c>NoResult</c>
///     (pipeline turns this into 401 for <c>[Authorize]</c> endpoints,
///     200 for <c>[AllowAnonymous]</c>). Matches production behavior.</item>
///   <item><c>Bearer</c> header present + <c>X-Test-Persona</c> header
///     naming a persona in <see cref="TestAuthOptions.Personas"/> →
///     synthesize a <c>ClaimsPrincipal</c> shaped like an Entra External ID
///     access token and succeed.</item>
///   <item><c>Bearer</c> header present + unknown / missing persona →
///     <c>NoResult</c>. Prevents test bugs where a fixture registers a
///     bearer but forgets the persona header from silently authenticating.</item>
/// </list>
///
/// <para>The bearer token content is NOT validated — any non-empty
/// <c>Bearer ...</c> string is accepted. Content-validation is the
/// production JwtBearer handler's job; this test scheme replaces it, so
/// content-validation tests belong in a future JWT-minter setup, not here.</para>
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<TestAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<TestAuthOptions>(options, logger, encoder)
{
    public const string PersonaHeader = "X-Test-Persona";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var auth = Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var personaKey = Request.Headers[PersonaHeader].ToString();
        if (string.IsNullOrEmpty(personaKey)
            || !Options.Personas.TryGetValue(personaKey, out var p))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Emit the SAME claim shape production Entra tokens carry so
        // UserProvisioningMiddleware + HttpCurrentUser resolve identically
        // between test and production auth paths.
        //
        // Slice OPS.M.15.5 — the legacy `extension_isOwner` / `extension_isAdmin`
        // claim emissions were retired along with the `IsOwner` / `IsAdmin`
        // reader on ICurrentUser. Role claims come from TestPersona.Roles
        // (mirroring production Entra App Roles → JwtBearer → ClaimTypes.Role).
        var claims = new List<Claim>
        {
            new("oid", p.Oid),
            new(ClaimTypes.NameIdentifier, p.Oid),
            new(ClaimTypes.Name, p.DisplayName),
            new("name", p.DisplayName),
            new(ClaimTypes.Email, p.Email),
            new("emails", p.Email),
            new("email_verified", "true"),
        };
        if (p.Roles is not null)
        {
            foreach (var role in p.Roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var identity = new ClaimsIdentity(
            claims, "TestAuth", ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            JwtBearerDefaults.AuthenticationScheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
