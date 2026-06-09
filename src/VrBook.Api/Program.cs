using Hellang.Middleware.ProblemDetails;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using VrBook.Api.Health;
using VrBook.Api.Middleware;
using VrBook.Application.Common;
using VrBook.Contracts.Common;
using VrBook.Domain.Common;
using VrBook.Infrastructure;
using VrBook.Infrastructure.Outbox;
using VrBook.Modules.Admin;
using VrBook.Modules.Booking;
using VrBook.Modules.Catalog;
using VrBook.Modules.Identity;
using VrBook.Modules.Identity.Infrastructure.Auth;
using VrBook.Modules.Loyalty;
using VrBook.Modules.Messaging;
using VrBook.Modules.Notifications;
using VrBook.Modules.Payment;
using VrBook.Modules.Pricing;
using VrBook.Modules.Reviews;
using VrBook.Modules.Sync;

// =================================================================================
// VrBook.Api — host for the modular monolith. Every bounded context is registered
// here via Add{Module}Module extensions. See proposal §3.
// =================================================================================

var builder = WebApplication.CreateBuilder(args);

// ---- Serilog (configured from appsettings.json under "Serilog") ----
// PiiRedactingEnricher runs LAST so it sees the final property bag (after
// FromLogContext + custom WithProperty have populated it). Any property whose
// key matches the sensitive list in PiiRedactingEnricher is replaced with
// [REDACTED] before the sink writes the JSON line. See docs/EXECUTION_PLAN.md
// section 4.2 for the standard.
builder.Host.UseSerilog((ctx, sp, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(sp)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "VrBook.Api")
    .Enrich.With<VrBook.Api.Observability.PiiRedactingEnricher>()
    // Suppresses the misleading "responded 500" framework log line when a domain
    // exception (BRV / NotFound / Conflict / Forbidden / FluentValidation) is
    // actually being remapped to 422/404/409/403/400 by ProblemDetailsMiddleware.
    .Filter.ByIncludingOnly(VrBook.Api.Observability.DomainExceptionLogFilter.ShouldLog));

// ---- ASP.NET Core core services ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetailsConfigured();
builder.Services.AddCors(opts => opts.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services
    .AddControllers(opts =>
    {
        opts.Filters.Add<TraceContextFilter>();
    })
    .AddJsonOptions(opts =>
    {
        // Accept + emit enum values as strings (e.g. "House" instead of 0).
        // Far friendlier for hand-crafted curl/Postman calls + matches what the
        // OpenAPI schema advertises.
        opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    })
    .ConfigureApiBehaviorOptions(opts =>
    {
        // Let ProblemDetails middleware build the 400 envelope from model state.
        opts.SuppressModelStateInvalidFilter = false;
        opts.InvalidModelStateResponseFactory = ctx =>
        {
            var problem = new ValidationProblemDetails(ctx.ModelState)
            {
                Type = ProblemTypes.Validation,
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest,
                Instance = ctx.HttpContext.Request.Path,
            };
            problem.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
            return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
        };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerConfigured();

// ---- AuthN / AuthZ — owned by the Identity module (proposal §14). ----
// Real AD B2C JWT bearer when AzureAdB2C:* is configured; falls through to the
// synthetic DevAuth principal when DevAuth:AllowAnonymous=true. Authorization
// policies (OwnerOrAdmin, Admin) registered inside.
builder.Services.AddVrBookAuthentication(builder.Configuration);

// ---- App + infra cores ----
builder.Services.AddApplicationCore();
builder.Services.AddInfrastructureCore(builder.Configuration);

// A0.3: domain-event publisher + SaveChanges outbox interceptor. Each module
// opts in by chaining .UseOutbox(sp) in its AddDbContext call. Without this
// every domain event raised by every aggregate is silently discarded.
builder.Services.AddOutbox();

// ---- Modules (each is a no-op stub in A0; agents replace per §20.2) ----
builder.Services
    .AddIdentityModule(builder.Configuration)
    .AddCatalogModule(builder.Configuration)
    .AddPricingModule(builder.Configuration)
    .AddBookingModule(builder.Configuration)
    .AddPaymentModule(builder.Configuration)
    .AddSyncModule(builder.Configuration)
    .AddMessagingModule(builder.Configuration)
    .AddReviewsModule(builder.Configuration)
    .AddLoyaltyModule(builder.Configuration)
    .AddNotificationsModule(builder.Configuration)
    .AddAdminModule(builder.Configuration);

// ---- Health probes (Container Apps Liveness + Readiness; see proposal §23.5) ----
builder.Services.AddHealthChecks()
    .AddCheck<LivenessHealthCheck>("self", tags: new[] { "live" })
    .AddCheck<ReadinessHealthCheck>("ready", tags: new[] { "ready" });

// ---- Application Insights (only when ConnectionString is provided) ----
var aiCs = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(aiCs))
{
    builder.Services.AddApplicationInsightsTelemetry();
}

var app = builder.Build();

// ---- Pipeline ----
app.UseProblemDetails();          // catches DomainException, ValidationException, etc.
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment() ||
    app.Configuration.GetValue<bool>("Swagger:EnableInProduction"))
{
    app.UseSwagger();
    app.UseSwaggerUI(opts =>
    {
        opts.SwaggerEndpoint("/swagger/v1/swagger.json", "VrBook API v1");
        opts.RoutePrefix = "swagger";
    });
}

app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseIdentityModule();   // on-first-login provisioning — MUST run after UseAuthentication
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = r => r.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });
app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();

/// <summary>Marker for WebApplicationFactory&lt;Program&gt; in tests.</summary>
public partial class Program;
