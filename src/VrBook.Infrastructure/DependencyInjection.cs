using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Common;
using VrBook.Infrastructure.Realtime;
using VrBook.Infrastructure.Redis;
using VrBook.Infrastructure.Storage;
using VrBook.Infrastructure.Stubs;

namespace VrBook.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers cross-cutting infrastructure: Redis, distributed locks, system clock,
    /// anonymous current-user fallback, and A0 stubs for cross-context interfaces.
    /// Modules replace stubs by registering the real implementations later.
    /// </summary>
    public static IServiceCollection AddInfrastructureCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Singletons
        services.AddSingleton<IDateTimeProvider, SystemClock>();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();

        // Redis — connection string from config; required for hold + lock + cache.
        var redisCs = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisCs))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(redisCs));
            services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        }

        // Slice 7 — SignalR Service realtime notifier. Falls back to a null
        // logger when the connection string isn't configured so dev hosts boot
        // without an Azure SignalR Service. See SLICE7_PLAN §2.5.
        var signalrCs = configuration["SignalR:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(signalrCs))
        {
            services.AddSingleton<IRealtimeNotifier>(sp =>
                new SignalRRealtimeNotifier(
                    signalrCs,
                    sp.GetRequiredService<ILogger<SignalRRealtimeNotifier>>()));
        }
        else
        {
            services.AddSingleton<IRealtimeNotifier, NullRealtimeNotifier>();
        }

        // VRB-101 — Blob storage (property images + message attachments). Managed
        // identity in staging/prod (Blob:AccountUrl); connection string for local
        // Azurite. When neither is set the service is unregistered (bare dev has
        // no blob backend); integration tests register an in-memory fake in the
        // fixture. IBlobStorage previously had NO implementation.
        var blobAccountUrl = configuration["Blob:AccountUrl"];
        var blobConnectionString = configuration.GetConnectionString("Blob")
            ?? configuration["Blob:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(blobAccountUrl))
        {
            services.AddSingleton(_ => new BlobServiceClient(new Uri(blobAccountUrl), new DefaultAzureCredential()));
            services.AddSingleton<IBlobStorage, AzureBlobStorage>();
        }
        else if (!string.IsNullOrWhiteSpace(blobConnectionString))
        {
            services.AddSingleton(_ => new BlobServiceClient(blobConnectionString));
            services.AddSingleton<IBlobStorage, AzureBlobStorage>();
        }
        else
        {
            // No backend configured: register a fallback so DI build-time
            // validation passes (image handlers always depend on IBlobStorage);
            // it throws only if an upload is actually attempted.
            services.AddSingleton<IBlobStorage, UnconfiguredBlobStorage>();
        }

        // A0 stubs — modules replace these as they ship (A5, A6, A8, A9).
        services.AddSingleton<ITaxCalculator, StubTaxCalculator>();
        services.AddSingleton<ILoyaltyDiscountResolver, StubLoyaltyDiscountResolver>();
        services.AddSingleton<IBookingAvailabilityReader, StubBookingAvailabilityReader>();
        services.AddSingleton<IExternalChannelConflictChecker, StubExternalChannelConflictChecker>();
        services.AddSingleton<IFeatureToggle, StubFeatureToggle>();

        // VRB-216 Phase A — config-backed settings providers (the §3 PAY contract).
        // Scoped to match the DB-backed impls VRB-216 will Replace() them with.
        services.AddScoped<ICancellationTierProvider, VrBook.Infrastructure.Settings.ConfigCancellationTierProvider>();
        services.AddScoped<ICancellationPolicyResolver, VrBook.Infrastructure.Settings.ConfigCancellationPolicyResolver>();
        services.AddScoped<IPlatformFeeResolver, VrBook.Infrastructure.Settings.ConfigPlatformFeeResolver>();
        services.AddScoped<ITaxPostureProvider, VrBook.Infrastructure.Settings.ConfigTaxPostureProvider>();

        return services;
    }
}
