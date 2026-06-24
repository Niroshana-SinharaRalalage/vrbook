namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Slice 7 — server-to-client realtime push port. Backed in production by
/// <c>SignalRRealtimeNotifier</c> (Azure SignalR Service, Serverless mode);
/// dev environments without a SignalR connection string fall back to
/// <c>NullRealtimeNotifier</c> which logs + no-ops. See
/// <c>docs/SLICE7_PLAN.md</c> §2.5 + §2.6.
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>
    /// Pushes a message to every client connected as <paramref name="userId"/>.
    /// Fire-and-forget by contract — the caller should not await the result on
    /// any user-visible request critical path. Implementations swallow exceptions
    /// internally; this signature exists so callers can choose to await in tests.
    /// </summary>
    Task NotifyUserAsync(Guid userId, string method, object payload, CancellationToken ct = default);

    /// <summary>
    /// Mints a per-user negotiate token for the SignalR Service. Token TTL is
    /// 1 hour (the SDK's <c>accessTokenFactory</c> re-fetches when it expires).
    /// </summary>
    Task<NegotiateResult> NegotiateForUserAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>Negotiate result. Mirrors the existing <c>RealtimeNegotiateResponse</c> DTO shape.</summary>
public sealed record NegotiateResult(string Url, string AccessToken, DateTimeOffset ExpiresAt);
