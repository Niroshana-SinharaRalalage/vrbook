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
using VrBook.Modules.Payment;
using VrBook.Modules.Pricing;
using VrBook.Modules.Sync;

// =================================================================================
// VrBook.Workers.Booking — Slice 0.4 booking SLA sweep.
// Container App Job, cron */10 * * * *. One-shot per tick: scan Tentative
// bookings whose TentativeUntil has passed, dispatch ExpirySweepCommand which
// auto-confirms or auto-expires each. See docs/REPLAN.md slice 0.4.
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

    // Module assemblies for MediatR handler discovery.
    builder.Services.AddMediatR(cfg => cfg
        .RegisterServicesFromAssembly(typeof(BookingModule).Assembly)
        .RegisterServicesFromAssembly(typeof(PaymentModule).Assembly)
        .RegisterServicesFromAssembly(typeof(PricingModule).Assembly)
        .RegisterServicesFromAssembly(typeof(CatalogModule).Assembly)
        .RegisterServicesFromAssembly(typeof(SyncModule).Assembly));

    // Modules: Booking does the sweep, Payment handles capture/cancel,
    // Sync provides the IExternalChannelConflictChecker real implementation.
    builder.Services.AddCatalogModule(builder.Configuration);
    builder.Services.AddPricingModule(builder.Configuration);
    builder.Services.AddBookingModule(builder.Configuration);
    builder.Services.AddPaymentModule(builder.Configuration);
    builder.Services.AddSyncModule(builder.Configuration);

    using var host = builder.Build();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();

    logger.LogInformation("Booking Expiry Worker — Slice 0.4 sweep starting.");

    using var scope = host.Services.CreateScope();
    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
    var result = await mediator.Send(new ExpirySweepCommand());

    logger.LogInformation(
        "Booking Expiry sweep complete. scanned={Scanned} autoConfirmed={Confirmed} autoExpired={Expired} skipped={Skipped}",
        result.Scanned, result.AutoConfirmed, result.AutoExpired, result.Skipped);

    return result.Skipped > 0 && result.AutoConfirmed == 0 && result.AutoExpired == 0 ? 2 : 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Booking Expiry Worker crashed");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
