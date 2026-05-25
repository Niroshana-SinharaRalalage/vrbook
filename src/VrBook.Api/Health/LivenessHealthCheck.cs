using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VrBook.Api.Health;

/// <summary>
/// Liveness — "the process is alive". Always healthy unless the host itself is in a bad
/// state. Container Apps restarts the replica on consecutive failures.
/// </summary>
public sealed class LivenessHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Healthy("alive"));
}
