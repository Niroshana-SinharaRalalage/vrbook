using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using VrBook.Modules.Booking;
using VrBook.Modules.Catalog;
using VrBook.Modules.Identity;
using VrBook.Modules.Payment;
using VrBook.Modules.Pricing;
using VrBook.Modules.Reviews;

// =================================================================================
// VrBook.Migrator — one-shot console app run as a Container App Job before every
// API revision update. Discovers every per-module DbContext registered in DI and
// applies its pending EF migrations. Owns the only Postgres role with DDL rights.
// =================================================================================

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "VrBook.Migrator")
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Configuration
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
        .AddEnvironmentVariables();

    builder.Services.AddSerilog((sp, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext());

    // Each module exposes a static AddXxxDbContextForMigrator helper (separate from
    // AddXxxModule so the migrator isn't pulling in MediatR / API / middleware services).
    builder.Services.AddIdentityDbContextForMigrator(builder.Configuration);
    builder.Services.AddCatalogDbContextForMigrator(builder.Configuration);
    builder.Services.AddPricingDbContextForMigrator(builder.Configuration);
    builder.Services.AddBookingDbContextForMigrator(builder.Configuration);
    builder.Services.AddPaymentDbContextForMigrator(builder.Configuration);
    builder.Services.AddReviewsDbContextForMigrator(builder.Configuration);

    using var host = builder.Build();

    Log.Information("Migrator starting. Environment={Env}", host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);

    var contexts = host.Services.GetServices<DbContext>().ToArray();
    if (contexts.Length == 0)
    {
        Log.Warning("No DbContexts registered. Nothing to migrate. " +
                    "Modules wire their DbContexts via AddXxxDbContextForMigrator once they ship.");
        return 0;
    }

    foreach (var ctx in contexts)
    {
        var name = ctx.GetType().Name;
        Log.Information("Migrating {DbContext}", name);
        await ctx.Database.MigrateAsync();
        Log.Information("Migrated {DbContext} ✓", name);
    }

    Log.Information("Migrator complete.");
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Migrator failed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
