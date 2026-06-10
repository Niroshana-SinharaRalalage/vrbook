using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Outbox;
using VrBook.Modules.Messaging.Infrastructure.Persistence;

namespace VrBook.Modules.Messaging;

public sealed class MessagingModule : IModuleRegistration
{
    public string Name => "messaging";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MessagingDbContext>((sp, opts) =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", MessagingDbContext.SchemaName))
                .UseOutbox(sp));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MessagingDbContext>());

        services.AddModuleAssembly(typeof(MessagingModule).Assembly);
        return services;
    }
}

public static class MessagingModuleRegistration
{
    public static IServiceCollection AddMessagingModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new MessagingModule().AddModule(services, configuration);

    public static IServiceCollection AddMessagingDbContextForMigrator(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MessagingDbContext>(opts =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", MessagingDbContext.SchemaName)));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<MessagingDbContext>());

        services.AddSingleton<IDateTimeProvider, VrBook.Infrastructure.Common.SystemClock>();
        services.AddSingleton<ICurrentUser, VrBook.Infrastructure.Common.AnonymousCurrentUser>();
        return services;
    }
}
