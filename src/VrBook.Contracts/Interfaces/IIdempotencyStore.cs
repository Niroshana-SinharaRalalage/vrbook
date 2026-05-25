namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Stores the response body hash for every <c>Idempotency-Key</c> seen on a mutating endpoint.
/// Retries with the same key replay the prior response. See proposal §6.1.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Returns the cached response if the key has been seen, else null.</summary>
    Task<IdempotentResponse?> TryGetAsync(string key, CancellationToken ct = default);

    /// <summary>Stores the response for 24h. Idempotent (no-op if already stored).</summary>
    Task StoreAsync(string key, IdempotentResponse response, TimeSpan? ttl = null, CancellationToken ct = default);
}

public sealed record IdempotentResponse(int StatusCode, string ContentType, byte[] Body);
