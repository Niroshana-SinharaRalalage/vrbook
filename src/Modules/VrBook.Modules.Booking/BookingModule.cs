using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Outbox;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Booking.Infrastructure.Persistence;

namespace VrBook.Modules.Booking;

public sealed class BookingModule : IModuleRegistration
{
    public string Name => "booking";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // OPS.M.9 §4.3 + §4.4 — registers DbContext + DbContextFactory +
        // TenantGucCommandInterceptor + IRlsBypassDbContextFactory together.
        services.AddTenantScopedDbContext<BookingDbContext>(configuration, BookingDbContext.SchemaName);

        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<BookingDbContext>());

        // Slice 0.1: hold store. Default to the Postgres-backed implementation;
        // Microsoft.Cache/Redis is retiring (2026) so Phase 1 staging cannot cheaply
        // provision the classic Redis required by RedisHoldStore. Postgres has the
        // same correctness guarantees (serializable txn + SELECT FOR UPDATE on
        // booking.booking_holds). To re-enable Redis later, set
        // "Features__Booking.UseRedisHoldStore": true after provisioning Azure Managed
        // Redis (or running Microsoft.Cache/redisEnterprise in production).
        // VRB-203 — renamed from Features:UseRedisHoldStore to the
        // Features:<Area>.<Capability> convention. This is a startup-time DI selection
        // (not a live toggle): the hold store is chosen at composition, so a runtime
        // override has no effect until restart.
        var useRedisHoldStore = configuration.GetValue("Features:Booking.UseRedisHoldStore", false);
        if (useRedisHoldStore)
        {
            services.AddScoped<VrBook.Contracts.Interfaces.IHoldStore,
                               VrBook.Modules.Booking.Infrastructure.Holds.RedisHoldStore>();
        }
        else
        {
            services.AddScoped<VrBook.Contracts.Interfaces.IHoldStore,
                               VrBook.Modules.Booking.Infrastructure.Holds.PostgresHoldStore>();
        }

        // A6 stage 5: cross-module read for Sync conflict detection.
        services.AddScoped<VrBook.Contracts.Interfaces.IConfirmedBookingLookup,
                           VrBook.Modules.Booking.Infrastructure.Persistence.ConfirmedBookingLookup>();

        // A7.4: cross-module read for Messaging thread bootstrap on BookingConfirmed.
        services.AddScoped<VrBook.Contracts.Interfaces.IBookingMessagingContext,
                           VrBook.Modules.Booking.Infrastructure.Persistence.BookingMessagingContext>();

        // Slice 4 polish: cross-module read for Notifications template enrichment.
        services.AddScoped<VrBook.Contracts.Interfaces.IBookingEmailLookup,
                           VrBook.Modules.Booking.Infrastructure.Persistence.BookingEmailLookup>();

        services.AddModuleAssembly(typeof(BookingModule).Assembly);
        return services;
    }
}

public static class BookingModuleRegistration
{
    public static IServiceCollection AddBookingModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new BookingModule().AddModule(services, configuration);

    public static IServiceCollection AddBookingDbContextForMigrator(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Migrator does NOT need the outbox interceptor — it only runs DDL.
        services.AddDbContext<BookingDbContext>(opts =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", BookingDbContext.SchemaName)));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<BookingDbContext>());

        services.AddSingleton<IDateTimeProvider, VrBook.Infrastructure.Common.SystemClock>();
        services.AddSingleton<ICurrentUser, VrBook.Infrastructure.Common.AnonymousCurrentUser>();
        return services;
    }
}
