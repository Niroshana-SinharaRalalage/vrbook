using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VrBook.Modules.Identity.Infrastructure.Auth;

namespace VrBook.Api.IntegrationTests.Multitenancy;

/// <summary>
/// Slice OPS.M.10 Wave 2 §4.1 (D1) — test-only authentication handler
/// that resolves the persona cookie to one of two seeded tenant-owner
/// users. Replaces the production <c>DevAuthHandler</c> inside the
/// <c>TwoTenantApiFixture</c> so the existing Owner/Guest/Admin
/// personas don't shadow the OwnerA / OwnerB / PlatformAdmin matrix the
/// cross-tenant tests need.
///
/// <para>The handler reads the same cookie name as the production handler
/// (<c>vrbook-dev-persona</c>) but maps four logical values:</para>
/// <list type="bullet">
///   <item><c>OwnerA</c> → seeded TenantA Owner</item>
///   <item><c>OwnerB</c> → seeded TenantB Owner</item>
///   <item><c>PlatformAdmin</c> → seeded PlatformAdmin user (no tenant
///         membership; deferred in this Wave 2 partial)</item>
///   <item>anything else / null → anonymous (no auth)</item>
/// </list>
/// </summary>
public sealed class TwoTenantDevAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevAuth"; // same scheme as production so [Authorize] matches

    /// <summary>OID assigned to the seeded TenantA owner. Stable across runs.</summary>
    public const string OwnerAOid = "test-owner-tenant-a";
    /// <summary>OID assigned to the seeded TenantB owner. Stable across runs.</summary>
    public const string OwnerBOid = "test-owner-tenant-b";
    /// <summary>OID assigned to the seeded PlatformAdmin user.</summary>
    public const string PlatformAdminOid = "test-platform-admin";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cookie = Request.Cookies[DevAuthPersonas.CookieName];
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var (oid, email, displayName) = cookie switch
        {
            "OwnerA" => (OwnerAOid, "owner-a@vrbook.test", "Owner A"),
            "OwnerB" => (OwnerBOid, "owner-b@vrbook.test", "Owner B"),
            "PlatformAdmin" => (PlatformAdminOid, "platform-admin@vrbook.test", "Platform Admin"),
            _ => (string.Empty, string.Empty, string.Empty),
        };
        if (string.IsNullOrEmpty(oid))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("oid", oid),
            new(ClaimTypes.NameIdentifier, oid),
            new(ClaimTypes.Name, displayName),
            new("name", displayName),
            new(ClaimTypes.Email, email),
            new("emails", email),
            new("email_verified", "true"),
            new("extension_isOwner", "true"),
            new("extension_isAdmin", "true"),
            new("dev_persona", cookie),
        };
        // The Role + TenantId claims are added by UserProvisioningMiddleware
        // (M.2 enrichment) based on the seeded tenant_memberships rows.
        claims.Add(new Claim(ClaimTypes.Role, "Owner"));
        claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
