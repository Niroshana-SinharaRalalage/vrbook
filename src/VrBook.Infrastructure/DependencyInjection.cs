using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Common;
using VrBook.Infrastructure.Redis;
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

        // A0 stubs — modules replace these as they ship (A5, A6, A8, A9).
        services.AddSingleton<ITaxCalculator, StubTaxCalculator>();
        services.AddSingleton<ILoyaltyDiscountResolver, StubLoyaltyDiscountResolver>();
        services.AddSingleton<IBookingAvailabilityReader, StubBookingAvailabilityReader>();
        services.AddSingleton<IExternalChannelConflictChecker, StubExternalChannelConflictChecker>();
        services.AddSingleton<IFeatureToggle, StubFeatureToggle>();

        return services;
    }
}
