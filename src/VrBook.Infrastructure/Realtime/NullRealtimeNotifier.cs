using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Realtime;

/// <summary>
/// Dev fallback when <c>SignalR:ConnectionString</c> is empty. Logs and no-ops
/// so the host boots without an Azure SignalR Service. <c>NegotiateForUserAsync</c>
/// throws so callers (the Negotiate endpoint) can map to <c>503 realtime.unavailable</c>
/// rather than return a half-valid token.
/// </summary>
internal sealed class NullRealtimeNotifier(ILogger<NullRealtimeNotifier> logger) : IRealtimeNotifier
{
    public Task NotifyUserAsync(Guid userId, string method, object payload, CancellationToken ct = default)
    {
        logger.LogDebug(
            "SignalR disabled (null notifier); dropping push to user {UserId} method {Method}",
            userId, method);
        return Task.CompletedTask;
    }

    public Task<NegotiateResult> NegotiateForUserAsync(Guid userId, CancellationToken ct = default)
    {
        logger.LogWarning(
            "Negotiate requested but SignalR:ConnectionString is not configured; returning realtime.unavailable.");
        throw new InvalidOperationException("realtime.unavailable");
    }
}
