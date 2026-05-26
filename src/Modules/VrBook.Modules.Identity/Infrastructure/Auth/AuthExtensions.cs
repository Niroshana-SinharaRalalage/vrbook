using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace VrBook.Modules.Identity.Infrastructure.Auth;

public static class AuthExtensions
{
    /// <summary>
    /// Wires authentication for the API host. If <c>EntraExternalId:TenantId</c> is set,
    /// registers JWT bearer validation against the External tenant. If
    /// <c>DevAuth:AllowAnonymous</c> is true, registers the dev synthetic scheme — every
    /// request authenticates as a synthetic owner. Both can coexist; <c>DevAuth</c> wins
    /// when enabled. See ADR-0012 for the pivot from AD B2C to Entra External ID.
    /// </summary>
    public static IServiceCollection AddVrBookAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Entra External ID — issuer/authority pattern:
        //   https://{tenantSubdomain}.ciamlogin.com/{tenantId}/v2.0
        // where tenantSubdomain is the bit before .onmicrosoft.com (e.g., 'vrbookcid'),
        // and tenantId is the GUID of the External tenant.
        var entraInstance = configuration["EntraExternalId:Instance"];     // e.g. https://vrbookcid.ciamlogin.com
        var entraTenantId = configuration["EntraExternalId:TenantId"];     // GUID of the External tenant
        var entraClientId = configuration["EntraExternalId:ClientId"];     // vrbook-api app registration id (audience)

        var devAuthEnabled = configuration.GetValue<bool>("DevAuth:AllowAnonymous");
        var defaultScheme = devAuthEnabled
            ? DevAuthHandler.SchemeName
            : JwtBearerDefaults.AuthenticationScheme;

        var auth = services.AddAuthentication(defaultScheme);

        if (!string.IsNullOrWhiteSpace(entraInstance) &&
            !string.IsNullOrWhiteSpace(entraTenantId) &&
            !string.IsNullOrWhiteSpace(entraClientId))
        {
            var authority = $"{entraInstance.TrimEnd('/')}/{entraTenantId}/v2.0";

            auth.AddJwtBearer(opts =>
            {
                opts.Authority = authority;
                opts.Audience = entraClientId;
                opts.RequireHttpsMetadata = true;
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    ValidateAudience = true,
                    ValidAudience = entraClientId,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2),
                };
            });
        }

        if (devAuthEnabled)
        {
            auth.AddScheme<DevAuthOptions, DevAuthHandler>(DevAuthHandler.SchemeName, opts =>
            {
                opts.Enabled = true;
                opts.FakeOid = configuration["DevAuth:FakeOid"] ?? opts.FakeOid;
                opts.FakeEmail = configuration["DevAuth:FakeEmail"] ?? opts.FakeEmail;
                opts.FakeDisplayName = configuration["DevAuth:FakeDisplayName"] ?? opts.FakeDisplayName;
                opts.IsOwner = configuration.GetValue("DevAuth:IsOwner", true);
                opts.IsAdmin = configuration.GetValue("DevAuth:IsAdmin", true);
            });
        }

        services.AddAuthorization(opts =>
        {
            opts.AddPolicy("OwnerOrAdmin", p => p.RequireRole("Owner", "Admin"));
            opts.AddPolicy("Admin", p => p.RequireRole("Admin"));
        });

        return services;
    }
}
