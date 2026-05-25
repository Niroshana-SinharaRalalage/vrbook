using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;

// =================================================================================
// VrBook.Workers.Notifications — KEDA Service Bus-scaled. A0 skeleton: hosts an empty loop.
// A9 replaces this with the SendGrid + Stubble template pipeline. See proposal §13.
// =================================================================================

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Configuration.AddEnvironmentVariables();
    builder.Services.AddSerilog((sp, lc) => lc.ReadFrom.Configuration(builder.Configuration));

    builder.Services.AddHostedService<NotificationWorkerSkeleton>();

    using var host = builder.Build();
    Log.Information("Notification Worker (A0 skeleton). Real implementation in A9.");
    await host.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Notification worker crashed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

internal sealed class NotificationWorkerSkeleton : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
