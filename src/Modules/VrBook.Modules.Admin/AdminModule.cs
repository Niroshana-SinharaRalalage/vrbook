using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Admin.Application;
using VrBook.Modules.Admin.Infrastructure;
using VrBook.Modules.Admin.Infrastructure.Persistence;

namespace VrBook.Modules.Admin;

/// <summary>
/// Module bootstrap for the <c>Admin</c> bounded context. VRB-203 lands the first real
/// slice: the global feature-flag override table (<c>admin.feature_flags</c>) + the
/// runtime <see cref="IFeatureToggle"/> that replaces the no-op stub (gap G13).
/// </summary>
public sealed class AdminModule : IModuleRegistration
{
    public string Name => "admin";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // Plain, NON-tenant-scoped DbContext: feature flags are platform-global, so the
        // tenant-GUC RLS interceptor is deliberately NOT attached (contrast the other
        // modules' AddTenantScopedDbContext). No RLS policy on admin.feature_flags.
        services.AddDbContext<AdminDbContext>(opts =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", AdminDbContext.SchemaName)));

        services.AddMemoryCache();
        services.AddScoped<IFeatureFlagStore, AdminDbFeatureFlagStore>();

        // Replace the A0 StubFeatureToggle (registered by AddInfrastructureCore) with the
        // real DB-backed resolver. Scoped because it reads the scoped AdminDbContext.
        services.Replace(ServiceDescriptor.Scoped<IFeatureToggle, DbFeatureToggle>());

        services.AddModuleAssembly(typeof(AdminModule).Assembly);
        return services;
    }
}

public static class AdminModuleRegistration
{
    public static IServiceCollection AddAdminModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new AdminModule().AddModule(services, configuration);

    public static IServiceCollection AddAdminDbContextForMigrator(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AdminDbContext>(opts =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", AdminDbContext.SchemaName)));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<AdminDbContext>());

        services.AddSingleton<IDateTimeProvider, VrBook.Infrastructure.Common.SystemClock>();
        services.AddSingleton<ICurrentUser, VrBook.Infrastructure.Common.AnonymousCurrentUser>();
        return services;
    }
}
