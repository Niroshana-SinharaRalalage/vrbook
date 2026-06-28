using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Outbox;
using VrBook.Modules.Identity.Application.Behaviors;
using VrBook.Modules.Identity.Infrastructure;
using VrBook.Modules.Identity.Infrastructure.Auth;
using VrBook.Modules.Identity.Infrastructure.Persistence;

namespace VrBook.Modules.Identity;

public sealed class IdentityModule : IModuleRegistration
{
    public string Name => "identity";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // DbContext — module owns its schema.
        services.AddDbContext<IdentityDbContext>((sp, opts) =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.SchemaName))
                .UseOutbox(sp));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserEmailLookup, UserEmailLookup>();
        // OPS.M.5 §3.4 (D4) — cross-module lookup for Stripe Connect routing.
        services.AddScoped<ITenantStripeContextLookup, TenantStripeContextLookup>();
        // OPS.M.5 §3.7 + §3.8 — Payment-module webhook handler invokes this
        // to apply Tenant.UpdateStripeAccountReadiness on account.updated events.
        services.AddScoped<IConnectAccountReadinessUpdater, ConnectAccountReadinessUpdater>();
        // OPS.M.8 §3.11 (D11) — composed stats lookup for the platform-admin
        // detail page; delegates property count to the OPS.M.7 lookup.
        services.AddScoped<IPlatformTenantStatsLookup, PlatformTenantStatsLookup>();

        // The DbContext doubles as the module's IUnitOfWork.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<IdentityDbContext>());

        // Replace the AnonymousCurrentUser stub from A0 with the HTTP-aware reader.
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUser, HttpCurrentUser>());

        // MediatR handlers + FluentValidation validators (assembly scan).
        services.AddModuleAssembly(typeof(IdentityModule).Assembly);

        // Pipeline order: Validation (Common) → TenantAuthorization → AuditLog → handler.
        // Per OPS_M_4_PLAN section 3.4: tenant gate runs BEFORE audit so cross-tenant
        // attempts become "<action>.failed" audit rows via AuditLogBehavior's
        // exception path. Registration order = pipeline order in MediatR.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TenantAuthorizationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditLogBehavior<,>));

        return services;
    }
}

public static class IdentityModuleRegistration
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new IdentityModule().AddModule(services, configuration);

    /// <summary>
    /// Variant used by VrBook.Migrator. Registers only what's needed to apply migrations.
    /// </summary>
    public static IServiceCollection AddIdentityDbContextForMigrator(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>(opts =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.SchemaName)));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<IdentityDbContext>());

        // Migrator never serves requests, but BaseDbContext requires these dependencies.
        services.AddSingleton<IDateTimeProvider, VrBook.Infrastructure.Common.SystemClock>();
        services.AddSingleton<ICurrentUser, VrBook.Infrastructure.Common.AnonymousCurrentUser>();
        return services;
    }

    /// <summary>
    /// Map Identity-owned middleware. MUST run AFTER UseAuthentication().
    /// </summary>
    public static IApplicationBuilder UseIdentityModule(this IApplicationBuilder app) =>
        app.UseMiddleware<UserProvisioningMiddleware>();
}
