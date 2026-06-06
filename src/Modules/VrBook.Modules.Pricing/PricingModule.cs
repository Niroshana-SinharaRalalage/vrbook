using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Pricing.Infrastructure.Persistence;

namespace VrBook.Modules.Pricing;

public sealed class PricingModule : IModuleRegistration
{
    public string Name => "pricing";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PricingDbContext>(opts =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", PricingDbContext.SchemaName)));

        services.AddScoped<IPricingPlanRepository, PricingPlanRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PricingDbContext>());

        services.AddModuleAssembly(typeof(PricingModule).Assembly);
        return services;
    }
}

public static class PricingModuleRegistration
{
    public static IServiceCollection AddPricingModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new PricingModule().AddModule(services, configuration);

    public static IServiceCollection AddPricingDbContextForMigrator(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PricingDbContext>(opts =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", PricingDbContext.SchemaName)));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<PricingDbContext>());

        services.AddSingleton<IDateTimeProvider, VrBook.Infrastructure.Common.SystemClock>();
        services.AddSingleton<ICurrentUser, VrBook.Infrastructure.Common.AnonymousCurrentUser>();
        return services;
    }
}
