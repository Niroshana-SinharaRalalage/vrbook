using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Payment.Application;
using VrBook.Modules.Payment.Infrastructure.Persistence;
using VrBook.Modules.Payment.Infrastructure.Stripe;

namespace VrBook.Modules.Payment;

public sealed class PaymentModule : IModuleRegistration
{
    public string Name => "payment";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PaymentDbContext>(opts =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", PaymentDbContext.SchemaName)));

        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));
        services.Configure<RefundOptions>(configuration.GetSection(RefundOptions.SectionName));
        services.AddSingleton<IStripeGateway, StripeGateway>();

        services.AddScoped<IPaymentIntentRepository, PaymentIntentRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PaymentDbContext>());

        services.AddModuleAssembly(typeof(PaymentModule).Assembly);
        return services;
    }
}

public static class PaymentModuleRegistration
{
    public static IServiceCollection AddPaymentModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new PaymentModule().AddModule(services, configuration);

    public static IServiceCollection AddPaymentDbContextForMigrator(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PaymentDbContext>(opts =>
            opts.UseNpgsql(
                configuration.GetConnectionString("Postgres") ?? string.Empty,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", PaymentDbContext.SchemaName)));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<PaymentDbContext>());

        services.AddSingleton<IDateTimeProvider, VrBook.Infrastructure.Common.SystemClock>();
        services.AddSingleton<ICurrentUser, VrBook.Infrastructure.Common.AnonymousCurrentUser>();
        return services;
    }
}
