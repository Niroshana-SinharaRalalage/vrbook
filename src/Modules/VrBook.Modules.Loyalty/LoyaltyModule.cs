using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Outbox;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Loyalty.Infrastructure;
using VrBook.Modules.Loyalty.Infrastructure.Persistence;

namespace VrBook.Modules.Loyalty;

public sealed class LoyaltyModule : IModuleRegistration
{
    public string Name => "loyalty";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // OPS.M.9 §4.3 + §4.4 — registers DbContext + DbContextFactory +
        // TenantGucCommandInterceptor + IRlsBypassDbContextFactory together.
        services.AddTenantScopedDbContext<LoyaltyDbContext>(configuration, LoyaltyDbContext.SchemaName);

        // VRB-206 (G1) — tier thresholds are now config-driven + fail-fast validated.
        // Registered in the module (not the API composition root) so the completion
        // worker, which also runs OnBookingCompletedHandler, gets the bound options too.
        services.AddOptions<LoyaltyOptions>()
            .Bind(configuration.GetSection(LoyaltyOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<LoyaltyOptions>, LoyaltyOptionsValidator>();

        // A8.1: replace the A0 stub from VrBook.Infrastructure with the real resolver
        // backed by loyalty.accounts. The pricing module's ComputeQuoteHandler picks
        // up whichever ILoyaltyDiscountResolver is registered.
        services.Replace(ServiceDescriptor.Scoped<ILoyaltyDiscountResolver, RealLoyaltyDiscountResolver>());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<LoyaltyDbContext>());

        services.AddModuleAssembly(typeof(LoyaltyModule).Assembly);
        return services;
    }
}

public static class LoyaltyModuleRegistration
{
    public static IServiceCollection AddLoyaltyModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new LoyaltyModule().AddModule(services, configuration);

    public static IServiceCollection AddLoyaltyDbContextForMigrator(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<LoyaltyDbContext>(opts =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", LoyaltyDbContext.SchemaName)));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<LoyaltyDbContext>());

        services.AddSingleton<IDateTimeProvider, VrBook.Infrastructure.Common.SystemClock>();
        services.AddSingleton<ICurrentUser, VrBook.Infrastructure.Common.AnonymousCurrentUser>();
        return services;
    }
}
