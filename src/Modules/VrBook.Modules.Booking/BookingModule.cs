using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Outbox;
using VrBook.Modules.Booking.Infrastructure.Persistence;

namespace VrBook.Modules.Booking;

public sealed class BookingModule : IModuleRegistration
{
    public string Name => "booking";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BookingDbContext>((sp, opts) =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", BookingDbContext.SchemaName))
                .UseOutbox(sp));

        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<BookingDbContext>());

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
