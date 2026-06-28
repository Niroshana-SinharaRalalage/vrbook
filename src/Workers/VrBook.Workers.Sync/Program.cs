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

    // AddSyncModule -> AddModuleAssembly() already registers MediatR for the
    // Sync assembly. The explicit AddMediatR block that used to live here was
    // redundant - latent on this worker because it only dispatches
    // RunSyncForFeedCommand as IRequest<> (last-wins dedup) but a real bug on
    // the Booking worker (Slice 5 hotfix 9c580b6) where INotificationHandler
    // subscribers fired twice. Mirror that fix here for consistency.
    //
    // Wire just the Sync module so we get SyncDbContext + repositories + the
    // AirBnBICalChannel HTTP client. We do NOT pull every module here - the
    // worker has a single bounded job.
    builder.Services.AddSyncModule(builder.Configuration);

    using var host = builder.Build();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();

    // OPS.M.6 §3.7 (D7) Step 6 — Container Apps Jobs deliver SIGTERM with a
    // 30-second grace window; honor it so an in-flight RunSyncForFeed call
    // can finish cleanly. Console.CancelKeyPress also covers local Ctrl-C.
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true; // don't let the runtime kill us mid-write
        cts.Cancel();
    };

    using var scope = host.Services.CreateScope();
    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
    var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
    var now = clock.UtcNow;

    // OPS.M.9 §4.5 (D5) Step 9 — the bootstrap enumeration crosses every
    // tenant's due feeds; per-feed processing then runs under each feed's
    // BackgroundTenantScope (M.6 behavior wraps the handler). Bypass-open
    // the worker's initial read.
    var bypassFactory = scope.ServiceProvider.GetRequiredService<
        VrBook.Infrastructure.Persistence.IRlsBypassDbContextFactory<
            VrBook.Modules.Sync.Infrastructure.Persistence.SyncDbContext>>();
    System.Collections.Generic.IReadOnlyList<VrBook.Modules.Sync.Domain.ChannelFeed> due;
    await using (var bypass = await bypassFactory.CreateForBypassAsync(
        "sync-worker.list-due-feeds", cts.Token))
    {
        var feeds = scope.ServiceProvider.GetRequiredService<IChannelFeedRepository>();
        due = await feeds.ListDueForPollAsync(now);
    }
    logger.LogInformation("Sync Worker started. Due feeds: {DueCount}", due.Count);

    var ok = 0;
    var failed = 0;
    foreach (var feed in due)
    {
        if (cts.IsCancellationRequested)
        {
            logger.LogWarning("Cancellation requested; skipping remaining feeds.");
            break;
        }
        // OPS.M.6 §9 #2 + Step 6 — tenant_id + channel_feed_id auto-enrich
        // every log line inside the per-feed scope. The BackgroundCommandTenantScope
        // behavior also pushes tenant_id; pushing here covers the catch path
        // (which runs outside the behavior pipeline).
        using (Serilog.Context.LogContext.PushProperty("tenant_id", feed.TenantId))
        using (Serilog.Context.LogContext.PushProperty("channel_feed_id", feed.Id))
        {
            try
            {
                var cmd = new RunSyncForFeedCommand(feed.Id, feed.TenantId);
                var result = await mediator.Send(cmd, cts.Token);
                if (result.Status == VrBook.Contracts.Enums.SyncRunStatus.Success)
                {
                    ok++;
                }
                else
                {
                    failed++;
                }
            }
            catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Cancelled mid-run for feed {FeedId}.", feed.Id);
                break;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex,
                    "Sync run threw outside of RunSyncForFeedHandler tenant_id={TenantId} feed_id={FeedId}",
                    feed.TenantId, feed.Id);
            }
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
