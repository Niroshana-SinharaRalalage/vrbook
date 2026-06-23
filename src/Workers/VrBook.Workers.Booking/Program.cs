using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using VrBook.Application.Common;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure;
using VrBook.Infrastructure.Common;
using VrBook.Infrastructure.Outbox;
using VrBook.Modules.Booking;
using VrBook.Modules.Booking.Application.Commands;
using VrBook.Modules.Catalog;
using VrBook.Modules.Identity;
using VrBook.Modules.Loyalty;
using VrBook.Modules.Notifications;
using VrBook.Modules.Payment;
using VrBook.Modules.Pricing;
using VrBook.Modules.Sync;

// =================================================================================
// VrBook.Workers.Booking — Container App Job for booking sweeps.
// One image, two modes selected via --mode arg:
//   --mode=expiry     Slice 0.4 SLA sweep, cron */10 * * * *. ExpirySweepCommand.
//   --mode=completion Slice 5 daily sweep, cron 0 6 * * *. CompletionSweepCommand.
// Default mode is 'expiry' so the existing booking-expiry-job needs no Bicep arg.
// See docs/REPLAN.md slice 0.4 + docs/SLICE5_PLAN.md §2.1.
// =================================================================================

Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "VrBook.Workers.Booking")
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
        .Enrich.WithProperty("Application", "VrBook.Workers.Booking")
        .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter()));

    // Infrastructure essentials.
    builder.Services.AddSingleton<IDateTimeProvider, SystemClock>();
    builder.Services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
    builder.Services.AddOutbox();
    builder.Services.AddInfrastructureCore(builder.Configuration);

    // Each Add*Module() extension internally calls AddModuleAssembly() which
    // calls services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(module)).
    // We do NOT do an explicit AddMediatR scan here — that registered every
    // handler twice. For IRequest<TResponse> handlers MediatR resolves a single
    // one so duplicates are silently deduped, but INotificationHandler
    // subscribers (e.g. BookingNotificationHandlers handling BookingCompleted)
    // get called for EACH registration, queuing duplicate emails. Loyalty +
    // Notifications + Identity are required for --mode=completion: when the
    // sweep calls Booking.Complete() the BookingCompleted event ripples through
    // Loyalty (stay count + TierPromoted) and Notifications (thanks-for-staying
    // + review request emails). Identity ships IUserEmailLookup.
    builder.Services.AddCatalogModule(builder.Configuration);
    builder.Services.AddPricingModule(builder.Configuration);
    builder.Services.AddBookingModule(builder.Configuration);
    builder.Services.AddPaymentModule(builder.Configuration);
    builder.Services.AddSyncModule(builder.Configuration);
    builder.Services.AddLoyaltyModule(builder.Configuration);
    builder.Services.AddNotificationsModule(builder.Configuration);
    builder.Services.AddIdentityModule(builder.Configuration);

    using var host = builder.Build();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();

    var mode = ReadMode(args);
    logger.LogInformation("Booking Worker starting in mode={Mode}.", mode);

    using var scope = host.Services.CreateScope();
    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

    if (mode == "completion")
    {
        var r = await mediator.Send(new CompletionSweepCommand());
        logger.LogInformation(
            "Booking Completion sweep complete. scanned={Scanned} completed={Completed} skipped={Skipped}",
            r.Scanned, r.Completed, r.Skipped);
        return r.Skipped > 0 && r.Completed == 0 ? 2 : 0;
    }
    else
    {
        var r = await mediator.Send(new ExpirySweepCommand());
        logger.LogInformation(
            "Booking Expiry sweep complete. scanned={Scanned} autoConfirmed={Confirmed} autoExpired={Expired} skipped={Skipped}",
            r.Scanned, r.AutoConfirmed, r.AutoExpired, r.Skipped);
        return r.Skipped > 0 && r.AutoConfirmed == 0 && r.AutoExpired == 0 ? 2 : 0;
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "Booking Worker crashed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static string ReadMode(string[] args)
{
    foreach (var a in args)
    {
        if (a.StartsWith("--mode=", StringComparison.OrdinalIgnoreCase))
        {
            var v = a["--mode=".Length..].Trim().ToLowerInvariant();
            return v is "expiry" or "completion" ? v : "expiry";
        }
    }
    return "expiry";
}
