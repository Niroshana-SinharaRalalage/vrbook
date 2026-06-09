using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Outbox;
using VrBook.Modules.Sync.Infrastructure;
using VrBook.Modules.Sync.Infrastructure.Persistence;

namespace VrBook.Modules.Sync;

public sealed class SyncModule : IModuleRegistration
{
    public string Name => "sync";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<SyncDbContext>((sp, opts) =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", SyncDbContext.SchemaName))
                .UseOutbox(sp));

        services.AddScoped<IChannelFeedRepository, ChannelFeedRepository>();
        services.AddScoped<IExternalReservationRepository, ExternalReservationRepository>();
        services.AddScoped<ISyncConflictRepository, SyncConflictRepository>();

        // A6: replace the A0 stub from VrBook.Infrastructure with the real checker
        // backed by sync.external_reservations. Replace (not just add) so the DI
        // container resolves the real one.
        services.Replace(ServiceDescriptor.Scoped<IExternalChannelConflictChecker, RealExternalChannelConflictChecker>());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SyncDbContext>());

        services.AddModuleAssembly(typeof(SyncModule).Assembly);
        return services;
    }
}

public static class SyncModuleRegistration
{
    public static IServiceCollection AddSyncModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new SyncModule().AddModule(services, configuration);

    public static IServiceCollection AddSyncDbContextForMigrator(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Migrator does NOT need the outbox interceptor — it only runs DDL.
        services.AddDbContext<SyncDbContext>(opts =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", SyncDbContext.SchemaName)));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<SyncDbContext>());

        services.AddSingleton<IDateTimeProvider, VrBook.Infrastructure.Common.SystemClock>();
        services.AddSingleton<ICurrentUser, VrBook.Infrastructure.Common.AnonymousCurrentUser>();
        return services;
    }
}
