using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Outbox;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Notifications.Infrastructure.Persistence;

namespace VrBook.Modules.Notifications;

public sealed class NotificationsModule : IModuleRegistration
{
    public string Name => "notifications";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // OPS.M.9 §4.3 + §4.4 — registers DbContext + DbContextFactory +
        // TenantGucCommandInterceptor + IRlsBypassDbContextFactory together.
        services.AddTenantScopedDbContext<NotificationsDbContext>(configuration, NotificationsDbContext.SchemaName);

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<NotificationsDbContext>());

        // Slice 4 C2: ACS adapter for outbound mail.
        services.AddSingleton<IEmailSender, Infrastructure.Email.AzureEmailSender>();
        // Slice 4 C3: Stubble + embedded Mustache templates.
        services.AddSingleton<Application.Dispatch.ITemplateRenderer,
                              Application.Dispatch.MustacheTemplateRenderer>();

        services.AddModuleAssembly(typeof(NotificationsModule).Assembly);
        return services;
    }
}

public static class NotificationsModuleRegistration
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new NotificationsModule().AddModule(services, configuration);

    public static IServiceCollection AddNotificationsDbContextForMigrator(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<NotificationsDbContext>(opts =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", NotificationsDbContext.SchemaName)));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<NotificationsDbContext>());

        services.AddSingleton<IDateTimeProvider, VrBook.Infrastructure.Common.SystemClock>();
        services.AddSingleton<ICurrentUser, VrBook.Infrastructure.Common.AnonymousCurrentUser>();
        return services;
    }
}
