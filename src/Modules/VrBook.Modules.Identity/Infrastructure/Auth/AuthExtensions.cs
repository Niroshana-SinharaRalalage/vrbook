using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            // Authority for OIDC discovery — the friendly-subdomain URL works for the
            // discovery doc fetch, but the issuer Entra actually stamps in tokens is
            // https://{tenantId}.ciamlogin.com/{tenantId}/v2.0 (tenant id in the host,
            // verified empirically against the OIDC discovery endpoint). We list both
            // forms in ValidIssuers and let JwtBearer match whichever shows up.
            var discoveryAuthority = $"{entraInstance.TrimEnd('/')}/{entraTenantId}/v2.0";
            var actualIssuer = $"https://{entraTenantId}.ciamlogin.com/{entraTenantId}/v2.0";

            auth.AddJwtBearer(opts =>
            {
                opts.Authority = discoveryAuthority;
                opts.Audience = entraClientId;
                opts.RequireHttpsMetadata = true;
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers = new[] { discoveryAuthority, actualIssuer },
                    ValidateAudience = true,
                    ValidAudience = entraClientId,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2),
                };

                // Slice OPS.M.13.6 diagnostic — JwtBearer's default 401 path is
                // silent (no LA trace of WHY validation failed). These handlers
                // surface the actual reason so evidence-based RCA is possible.
                opts.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("JwtBearer.AuthEvents");
                        logger.LogWarning(ctx.Exception,
                            "JWT authentication FAILED on {Path}. Type={ExType} Message={ExMsg}",
                            ctx.HttpContext.Request.Path,
                            ctx.Exception?.GetType().FullName ?? "<none>",
                            ctx.Exception?.Message ?? "<none>");
                        return Task.CompletedTask;
                    },
                    OnChallenge = ctx =>
                    {
                        // Log every challenge on /api/v1/* so we can distinguish
                        // "no token attached" (Error/Desc empty, AuthorizationHeader missing)
                        // from "token rejected" (Error/Desc populated with reason).
                        // Anonymous public routes don't hit this path with a bearer at all
                        // so we won't spam the log unless someone hits an authed route.
                        var path = ctx.HttpContext.Request.Path.Value ?? "";
                        if (path.StartsWith("/api/v1/", StringComparison.Ordinal))
                        {
                            var authHeader = ctx.HttpContext.Request.Headers["Authorization"].ToString();
                            var hasBearer = !string.IsNullOrEmpty(authHeader)
                                && authHeader.StartsWith("Bearer ", StringComparison.Ordinal);
                            var logger = ctx.HttpContext.RequestServices
                                .GetRequiredService<ILoggerFactory>()
                                .CreateLogger("JwtBearer.AuthEvents");
                            logger.LogWarning(
                                "JWT challenge on {Path}. HasBearer={HasBearer} Error={Err} Desc={Desc} FailureType={FT}",
                                path,
                                hasBearer,
                                ctx.Error ?? "<none>",
                                ctx.ErrorDescription ?? "<none>",
                                ctx.AuthenticateFailure?.GetType().FullName ?? "<none>");
                        }
                        return Task.CompletedTask;
                    },
                };
            });
        }

        if (devAuthEnabled)
        {
            // Persona claims are now resolved per-request from the
            // vrbook-dev-persona cookie inside DevAuthHandler (Owner default).
            auth.AddScheme<DevAuthOptions, DevAuthHandler>(DevAuthHandler.SchemeName, opts =>
            {
                opts.Enabled = true;
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
