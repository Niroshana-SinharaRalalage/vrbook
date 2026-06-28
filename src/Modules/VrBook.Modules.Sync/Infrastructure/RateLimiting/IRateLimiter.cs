namespace VrBook.Modules.Sync.Infrastructure.RateLimiting;

/// <summary>
/// OPS.M.6 §3.2 (D2) — outbound rate-limit gate. Implemented per host
/// (NOT per tenant) because the threat we're guarding against is
/// external-IP ban from the upstream provider (Airbnb, Booking, VRBO),
/// not noisy-neighbor tenants on our side.
///
/// <para>Single-instance, in-memory bucket per OPS.M.6 §3.2. Multi-instance
/// poller hardening is deferred to Slice OPS.M.10's distributed-state pass.</para>
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Block until a token is available for <paramref name="host"/> (which
    /// may be a subdomain — the implementation resolves to the matching
    /// suffix-policy via <see cref="HostMatcher"/>). Returns <c>false</c>
    /// when the configured max wait window elapses without acquisition;
    /// throws <see cref="OperationCanceledException"/> when
    /// <paramref name="ct"/> is cancelled.
    /// </summary>
    ValueTask<bool> TryAcquireAsync(string host, CancellationToken ct = default);
}
