using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Outbox;
using VrBook.Modules.Catalog.Application.Common;
using VrBook.Modules.Catalog.Infrastructure.Persistence;
using VrBook.Modules.Catalog.Infrastructure.Storage;

namespace VrBook.Modules.Catalog;

public sealed class CatalogModule : IModuleRegistration
{
    public string Name => "catalog";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CatalogDbContext>((sp, opts) =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", CatalogDbContext.SchemaName))
                .UseOutbox(sp));

        services.AddScoped<IPropertyRepository, PropertyRepository>();
        services.AddScoped<IAmenityRepository, AmenityRepository>();

        // The Catalog DbContext doubles as the module's IUnitOfWork. Each module
        // saves its own context; we don't span transactions across schemas here.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());

        services.AddSingleton<IPropertyImageUrlBuilder, PropertyImageUrlBuilder>();

        services.AddModuleAssembly(typeof(CatalogModule).Assembly);
        return services;
    }
}

public static class CatalogModuleRegistration
{
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new CatalogModule().AddModule(services, configuration);

    /// <summary>
    /// Variant used by VrBook.Migrator. Registers only what's needed to apply migrations.
    /// </summary>
    public static IServiceCollection AddCatalogDbContextForMigrator(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CatalogDbContext>(opts =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", CatalogDbContext.SchemaName)));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<CatalogDbContext>());

        services.AddSingleton<IDateTimeProvider, VrBook.Infrastructure.Common.SystemClock>();
        services.AddSingleton<ICurrentUser, VrBook.Infrastructure.Common.AnonymousCurrentUser>();
        return services;
    }
}
