using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

// =================================================================================
// VrBook.Workers.Booking — KEDA Service Bus-scaled. A0 skeleton: hosts an empty loop.
// A4 replaces this with the SLA timer + state-transition handlers. See proposal §7.
// =================================================================================

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Configuration.AddEnvironmentVariables();
    builder.Services.AddSerilog((sp, lc) => lc.ReadFrom.Configuration(builder.Configuration));

    builder.Services.AddHostedService<BookingWorkerSkeleton>();

    using var host = builder.Build();
    Log.Information("Booking Worker (A0 skeleton). Real implementation in A4.");
    await host.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Booking worker crashed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

internal sealed class BookingWorkerSkeleton : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
