using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace VrBook.Api.Health;

/// <summary>
/// Readiness — "the process can serve traffic". Pings Redis (lightweight) and reports
/// degraded if it's unreachable. Postgres connectivity will be added by A2/A4 when
/// real DbContexts ship.
/// </summary>
public sealed class ReadinessHealthCheck(IServiceProvider sp) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var redis = sp.GetService<IConnectionMultiplexer>();
        if (redis is null)
        {
            return HealthCheckResult.Degraded("redis not configured");
        }

        try
        {
            await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy("ready");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("redis ping failed", ex);
        }
    }
}
