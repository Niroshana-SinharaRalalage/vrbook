using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace VrBook.Modules.Identity.Infrastructure.Auth;

public static class AuthExtensions
{
    /// <summary>
    /// Wires authentication for the API host. If <c>AzureAdB2C:Instance</c> is set, registers
    /// JWT bearer validation against B2C. If <c>DevAuth:AllowAnonymous</c> is true, also
    /// registers the dev synthetic scheme as the default — so every request is authenticated
    /// as a synthetic owner. Both can coexist; <c>DevAuth</c> wins when enabled.
    /// </summary>
    public static IServiceCollection AddVrBookAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        var b2cInstance = configuration["AzureAdB2C:Instance"];
        var b2cDomain = configuration["AzureAdB2C:Domain"];
        var b2cTenant = configuration["AzureAdB2C:TenantId"];
        var b2cClient = configuration["AzureAdB2C:ClientId"];
        var b2cPolicy = configuration["AzureAdB2C:SignUpSignInPolicyId"];

        var devAuthEnabled = configuration.GetValue<bool>("DevAuth:AllowAnonymous");
        var defaultScheme = devAuthEnabled
            ? DevAuthHandler.SchemeName
            : JwtBearerDefaults.AuthenticationScheme;

        var auth = services.AddAuthentication(defaultScheme);

        if (!string.IsNullOrWhiteSpace(b2cInstance) &&
            !string.IsNullOrWhiteSpace(b2cDomain) &&
            !string.IsNullOrWhiteSpace(b2cPolicy))
        {
            var authority = $"{b2cInstance.TrimEnd('/')}/{b2cDomain}/{b2cPolicy}/v2.0";

            auth.AddJwtBearer(opts =>
            {
                opts.Authority = authority;
                opts.Audience = b2cClient;
                opts.RequireHttpsMetadata = true;
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = $"{b2cInstance.TrimEnd('/')}/{b2cTenant ?? string.Empty}/v2.0/",
                    ValidateAudience = true,
                    ValidAudience = b2cClient,
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
