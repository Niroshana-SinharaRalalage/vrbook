using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Common;
using VrBook.Infrastructure.Outbox;
using VrBook.Modules.Sync;
using VrBook.Modules.Sync.Application.SyncRuns.Commands;
using VrBook.Modules.Sync.Infrastructure.Persistence;

// =================================================================================
// VrBook.Workers.Sync — Container App Job, cron */5 * * * *. A6: one-shot pass over
// every channel feed whose IsDueForPoll(now) is true. Per-feed errors are isolated
// so one bad URL doesn't tank the whole batch.
// =================================================================================

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "VrBook.Workers.Sync")
    .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter())
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
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "VrBook.Workers.Sync")
        .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter()));

    // Minimal infrastructure dependencies for the Sync module DbContext.
    builder.Services.AddSingleton<IDateTimeProvider, SystemClock>();
    builder.Services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
    builder.Services.AddOutbox();
    builder.Services.AddHttpClient();

    // MediatR for RunSyncForFeedCommand. The Sync module's AddModuleAssembly
    // call inside AddSyncModule registers the handler.
    builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(SyncModule).Assembly));

    // Wire just the Sync module so we get SyncDbContext + repositories + the
    // AirBnBICalChannel HTTP client. We do NOT pull every module here — the
    // worker has a single bounded job.
    builder.Services.AddSyncModule(builder.Configuration);

    using var host = builder.Build();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();

    using var scope = host.Services.CreateScope();
    var feeds = scope.ServiceProvider.GetRequiredService<IChannelFeedRepository>();
    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
    var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
    var now = clock.UtcNow;

    var due = await feeds.ListDueForPollAsync(now);
    logger.LogInformation("Sync Worker started. Due feeds: {DueCount}", due.Count);

    var ok = 0;
    var failed = 0;
    foreach (var feed in due)
    {
        try
        {
            var result = await mediator.Send(new RunSyncForFeedCommand(feed.Id));
            if (result.Status == VrBook.Contracts.Enums.SyncRunStatus.Success)
            {
                ok++;
            }
            else
            {
                failed++;
            }
        }
        catch (Exception ex)
        {
            failed++;
            logger.LogError(ex, "Sync run threw outside of RunSyncForFeedHandler for feed {FeedId}", feed.Id);
        }
    }

    logger.LogInformation("Sync Worker complete. ok={Ok} failed={Failed} total={Total}", ok, failed, due.Count);
    return failed > 0 && ok == 0 ? 2 : 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Sync Worker crashed during bootstrap");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
