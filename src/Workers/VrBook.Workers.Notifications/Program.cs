using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure;
using VrBook.Infrastructure.Common;
using VrBook.Infrastructure.Outbox;
using VrBook.Modules.Notifications;
using VrBook.Modules.Notifications.Application.Dispatch;

// =================================================================================
// VrBook.Workers.Notifications — Slice 4 C2 dispatch sweep.
// Container App Job, cron */2 * * * *. One-shot per tick: release expired
// Sending leases, then dispatch up to BatchSize Queued rows whose NotBefore
// is due. See docs/SLICE4_PLAN.md §2.4 / §2.5.
// =================================================================================

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "VrBook.Workers.Notifications")
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
        .Enrich.WithProperty("Application", "VrBook.Workers.Notifications")
        .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter()));

    builder.Services.AddSingleton<IDateTimeProvider, SystemClock>();
    builder.Services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
    builder.Services.AddOutbox();
    builder.Services.AddInfrastructureCore(builder.Configuration);

    // Notifications module's AddModuleAssembly() (called inside
    // AddNotificationsModule below) already registers MediatR for this
    // assembly. The explicit AddMediatR block that used to live here was
    // redundant - latent on this worker because it only Send()s
    // IRequest<TResponse> handlers (last-wins dedup) but a real bug on the
    // Booking worker (Slice 5 hotfix 9c580b6) where INotificationHandler
    // subscribers fired twice. Mirror that fix here for consistency.
    //
    // The dispatcher only needs IEmailSender + NotificationsDbContext; the
    // row already carries a resolved RecipientEmail from the API-side queue
    // handler (Slice 4 C1). Identity is NOT registered here on purpose.
    builder.Services.AddNotificationsModule(builder.Configuration);

    using var host = builder.Build();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();

    logger.LogInformation("Notification Dispatch Worker — Slice 4 C2 starting.");

    using var scope = host.Services.CreateScope();
    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
    var result = await mediator.Send(new DrainQueuedNotificationsCommand());

    logger.LogInformation(
        "Notification dispatch complete. released={Released} picked={Picked} sent={Sent} failed={Failed} deadLettered={DeadLettered}",
        result.Released, result.Picked, result.Sent, result.Failed, result.DeadLettered);

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Notification Dispatch Worker crashed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
