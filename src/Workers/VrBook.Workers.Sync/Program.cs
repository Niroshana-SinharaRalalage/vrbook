using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

// =================================================================================
// VrBook.Workers.Sync — Container App Job, cron */5 * * * *. A0 skeleton: exits cleanly.
// A6 replaces this with the iCal poll loop. See proposal §8.
// =================================================================================

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Configuration.AddEnvironmentVariables();
    builder.Services.AddSerilog((sp, lc) => lc.ReadFrom.Configuration(builder.Configuration));

    using var host = builder.Build();

    Log.Information("Sync Worker (A0 skeleton). Real implementation in A6.");
    Log.Information("Would: poll due channel feeds, parse ICS, upsert external_reservations, detect conflicts.");

    await Task.CompletedTask;
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Sync worker crashed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
