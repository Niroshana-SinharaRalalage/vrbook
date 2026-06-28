using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Outbox;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Sync.Application.Behaviors;
using VrBook.Modules.Sync.Infrastructure;
using VrBook.Modules.Sync.Infrastructure.Channels;
using VrBook.Modules.Sync.Infrastructure.Persistence;
using VrBook.Modules.Sync.Infrastructure.RateLimiting;

namespace VrBook.Modules.Sync;

public sealed class SyncModule : IModuleRegistration
{
    public string Name => "sync";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // OPS.M.9 §4.3 + §4.4 — registers DbContext + DbContextFactory +
        // TenantGucCommandInterceptor + IRlsBypassDbContextFactory together.
        services.AddTenantScopedDbContext<SyncDbContext>(configuration, SyncDbContext.SchemaName);

        services.AddScoped<IChannelFeedRepository, ChannelFeedRepository>();
        services.AddScoped<IExternalReservationRepository, ExternalReservationRepository>();
        services.AddScoped<ISyncConflictRepository, SyncConflictRepository>();

        // A6: replace the A0 stub from VrBook.Infrastructure with the real checker
        // backed by sync.external_reservations. Replace (not just add) so the DI
        // container resolves the real one.
        services.Replace(ServiceDescriptor.Scoped<IExternalChannelConflictChecker, RealExternalChannelConflictChecker>());

        // OPS.M.6 §3.2 + §3.3 + §3.4 (D2/D3/D4) — per-host outbound rate limit.
        services.Configure<ChannelPollOptions>(
            configuration.GetSection(ChannelPollOptions.SectionName));
        services.AddSingleton<IRateLimiter, InMemoryHostRateLimiter>();
        services.AddTransient<OutboundRateLimitHandler>();

        // A6 channel adapters. Each implements IExternalChannel for one ChannelKind.
        // The worker iterates the IEnumerable<IExternalChannel> resolved from DI.
        services.AddHttpClient(AirBnBICalChannel.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(15);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("VrBook-Sync/1.0 (+https://vrbook.example.com)");
            c.DefaultRequestHeaders.Accept.ParseAdd("text/calendar, text/plain;q=0.9, */*;q=0.5");
        })
        .AddHttpMessageHandler<OutboundRateLimitHandler>();
        services.AddScoped<IExternalChannel, AirBnBICalChannel>();

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<SyncDbContext>());

        // OPS.M.6 §3.1 (D1) — paired with TenantAuthorizationBehavior's
        // early-return for IBackgroundCommand. Asserts non-empty TenantId on
        // worker-origin requests + pushes tenant_id into the logging scope.
        services.AddTransient(
            typeof(IPipelineBehavior<,>),
            typeof(BackgroundCommandTenantScopeBehavior<,>));

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
