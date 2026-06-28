using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Outbox;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Reviews.Infrastructure.Persistence;

namespace VrBook.Modules.Reviews;

public sealed class ReviewsModule : IModuleRegistration
{
    public string Name => "reviews";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // OPS.M.9 §4.3 + §4.4 — registers DbContext + DbContextFactory +
        // TenantGucCommandInterceptor + IRlsBypassDbContextFactory together.
        services.AddTenantScopedDbContext<ReviewsDbContext>(configuration, ReviewsDbContext.SchemaName);

        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ReviewsDbContext>());

        services.AddModuleAssembly(typeof(ReviewsModule).Assembly);
        return services;
    }
}

public static class ReviewsModuleRegistration
{
    public static IServiceCollection AddReviewsModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new ReviewsModule().AddModule(services, configuration);

    public static IServiceCollection AddReviewsDbContextForMigrator(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ReviewsDbContext>(opts =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", ReviewsDbContext.SchemaName)));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<ReviewsDbContext>());

        services.AddSingleton<IDateTimeProvider, VrBook.Infrastructure.Common.SystemClock>();
        services.AddSingleton<ICurrentUser, VrBook.Infrastructure.Common.AnonymousCurrentUser>();
        return services;
    }
}
