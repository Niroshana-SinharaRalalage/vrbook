using Microsoft.Extensions.Options;
using VrBook.Modules.Catalog.Options;
using VrBook.Modules.Identity.Options;
using VrBook.Modules.Notifications.Options;
using VrBook.Modules.Payment.Application;
using VrBook.Modules.Payment.Infrastructure.Stripe;

namespace VrBook.Api.Configuration;

/// <summary>
/// VRB-200 (gap G5) — binds every required config section to a strongly-typed
/// options class with fail-fast startup validation. Each section is wired with
/// <c>AddOptions&lt;T&gt;().Bind(section).ValidateDataAnnotations().ValidateOnStart()</c>
/// plus a cross-field <see cref="IValidateOptions{T}"/> that names the exact
/// <c>Section:Key</c> on failure. A misconfigured Staging/Production deploy now
/// crashes the host (failing its readiness probe) instead of silently booting
/// degraded — most importantly, it can never boot with JwtBearer unwired.
///
/// <para>Environment-aware carve-out: <c>EntraExternalId</c> is required in
/// Staging/Production but optional in Development (dev-loopback), where the
/// <see cref="ConfigValidationReporter"/> logs a single Warning instead.</para>
/// </summary>
public static class ConfigValidationExtensions
{
    public static IServiceCollection AddValidatedConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var validated = new List<(Type OptionsType, string Section)>();
        var skipped = new List<string>();

        // Entra — security-critical presence gate. Required in Staging/Production;
        // Development boots without it (warned) so local loopback needs no tenant.
        var entra = services
            .AddOptions<EntraExternalIdOptions>()
            .Bind(configuration.GetSection(EntraExternalIdOptions.SectionName));
        if (environment.IsDevelopment())
        {
            skipped.Add(EntraExternalIdOptions.SectionName);
        }
        else
        {
            entra.ValidateDataAnnotations().ValidateOnStart();
            services.AddSingleton<IValidateOptions<EntraExternalIdOptions>, EntraExternalIdOptionsValidator>();
            validated.Add((typeof(EntraExternalIdOptions), EntraExternalIdOptions.SectionName));
        }

        // CORS — required in Staging/Production (an API allowing no origins can't serve
        // its SPA); Development uses the appsettings localhost default (same carve-out).
        var cors = services
            .AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName));
        if (environment.IsDevelopment())
        {
            skipped.Add(CorsOptions.SectionName);
        }
        else
        {
            cors.ValidateDataAnnotations().ValidateOnStart();
            services.AddSingleton<IValidateOptions<CorsOptions>, CorsOptionsValidator>();
            validated.Add((typeof(CorsOptions), CorsOptions.SectionName));
        }

        AddValidated<StripeOptions, StripeOptionsValidator>(services, configuration, StripeOptions.SectionName, validated);
        AddValidated<RefundOptions, RefundOptionsValidator>(services, configuration, RefundOptions.SectionName, validated);
        AddValidated<AcsOptions, AcsOptionsValidator>(services, configuration, AcsOptions.SectionName, validated);
        AddValidated<BlobOptions, BlobOptionsValidator>(services, configuration, BlobOptions.SectionName, validated);
        AddValidated<CatalogImageOptions, CatalogImageOptionsValidator>(services, configuration, CatalogImageOptions.SectionName, validated);

        services.AddSingleton<IHostedService>(sp => new ConfigValidationReporter(
            sp,
            sp.GetRequiredService<ILogger<ConfigValidationReporter>>(),
            validated,
            skipped));

        return services;
    }

    private static void AddValidated<TOptions, TValidator>(
        IServiceCollection services,
        IConfiguration configuration,
        string section,
        List<(Type, string)> validated)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        services
            .AddOptions<TOptions>()
            .Bind(configuration.GetSection(section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TOptions>, TValidator>();
        validated.Add((typeof(TOptions), section));
    }
}
