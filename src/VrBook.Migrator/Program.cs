using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using VrBook.Migrator;
using VrBook.Modules.Admin;
using VrBook.Modules.Booking;
using VrBook.Modules.Catalog;
using VrBook.Modules.Identity;
using VrBook.Modules.Loyalty;
using VrBook.Modules.Messaging;
using VrBook.Modules.Notifications;
using VrBook.Modules.Payment;
using VrBook.Modules.Pricing;
using VrBook.Modules.Reviews;
using VrBook.Modules.Sync;

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
    builder.Services.AddSyncDbContextForMigrator(builder.Configuration);
    builder.Services.AddMessagingDbContextForMigrator(builder.Configuration);
    builder.Services.AddLoyaltyDbContextForMigrator(builder.Configuration);
    builder.Services.AddNotificationsDbContextForMigrator(builder.Configuration);
    builder.Services.AddAdminDbContextForMigrator(builder.Configuration); // VRB-203 — admin.feature_flags

    // Slice OPS.M.22.6 — Bicep-declarative platform admin backfill.
    // Registers as scoped so it can be resolved once the host is built.
    builder.Services.AddScoped<SeedPlatformAdminsBackfill>();

    // Slice OPS.2.2 — Bicep-declarative Playwright E2E fixture backfill
    // (isolated e2e-tenant + pre-seeded owner/platform-admin personas).
    // No-op unless Bootstrap:E2e:Enabled is true (staging only).
    builder.Services.AddScoped<SeedE2EBackfill>();

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

    // Slice OPS.M.22.6 — declarative platform-admin backfill runs AFTER
    // migrations so the M.22.2 pre_seeded_at column is guaranteed present.
    // No-op when Bootstrap:SeedPlatformAdmins is empty; safe on every deploy.
    using (var backfillScope = host.Services.CreateScope())
    {
        var backfill = backfillScope.ServiceProvider.GetRequiredService<SeedPlatformAdminsBackfill>();
        await backfill.RunAsync();
    }

    // Slice OPS.2.2 — E2E fixture backfill runs AFTER migrations so the
    // is_e2e column (OpsM2) + pre_seeded_at column (M.22) are guaranteed
    // present. No-op unless Bootstrap:E2e:Enabled=true; safe on every deploy.
    using (var e2eScope = host.Services.CreateScope())
    {
        var e2eBackfill = e2eScope.ServiceProvider.GetRequiredService<SeedE2EBackfill>();
        await e2eBackfill.RunAsync();
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
