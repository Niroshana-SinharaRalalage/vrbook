using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using VrBook.Modules.Identity;

// =================================================================================
// VrBook.Migrator — one-shot console app run as a Container App Job before every
// API revision update. Discovers every per-module DbContext registered in DI and
// applies its pending EF migrations. Owns the only Postgres role with DDL rights.
// =================================================================================

// TEMP BOOT DIAGNOSTICS: prove Main() actually runs. Container Apps was reporting
// silent exit-1 from the migrator job. Remove once root cause is identified.
Console.Error.WriteLine("[BOOT] Migrator Main entered. CLR ready.");
Console.Error.Flush();

try
{
    Console.Error.WriteLine("[BOOT] Creating Serilog bootstrap logger...");
    Log.Logger = new LoggerConfiguration()
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "VrBook.Migrator")
        .WriteTo.Console()
        .CreateBootstrapLogger();
    Console.Error.WriteLine("[BOOT] Serilog bootstrap logger ready.");

    Console.Error.WriteLine("[BOOT] Host.CreateApplicationBuilder...");
    var builder = Host.CreateApplicationBuilder(args);
    Console.Error.WriteLine("[BOOT] Application builder created.");

    builder.Configuration
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
        .AddEnvironmentVariables();
    Console.Error.WriteLine("[BOOT] Configuration sources added.");

    builder.Services.AddSerilog((sp, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext());
    Console.Error.WriteLine("[BOOT] AddSerilog done.");

    // Each module exposes a static AddXxxDbContextForMigrator helper (separate from
    // AddXxxModule so the migrator isn't pulling in MediatR / API / middleware services).
    builder.Services.AddIdentityDbContextForMigrator(builder.Configuration);
    Console.Error.WriteLine("[BOOT] AddIdentityDbContextForMigrator done.");
    // TODO(A2): builder.Services.AddCatalogDbContextForMigrator(builder.Configuration);
    // TODO(A3..): one per module-DbContext as they ship.

    Console.Error.WriteLine("[BOOT] builder.Build()...");
    using var host = builder.Build();
    Console.Error.WriteLine("[BOOT] Host built.");

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
    // Belt-and-braces: write the exception raw to stderr in case Serilog is dead.
    Console.Error.WriteLine($"[BOOT-FATAL] {ex.GetType().FullName}: {ex.Message}");
    Console.Error.WriteLine(ex.ToString());
    Console.Error.Flush();
    Log.Fatal(ex, "Migrator failed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
