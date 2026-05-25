namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Redis-backed distributed lock used by the booking hold flow (proposal §7.3).
/// Acquisition uses <c>SET NX PX</c>; release is conditional (Lua) to avoid
/// releasing a lock you no longer own.
/// </summary>
public interface IDistributedLock
{
    /// <summary>Returns a handle if acquired; null if the lock is held by someone else.</summary>
    Task<IDistributedLockHandle?> TryAcquireAsync(
        string key,
        TimeSpan ttl,
        CancellationToken ct = default);
}

public interface IDistributedLockHandle : IAsyncDisposable
{
    string Key { get; }
    string Token { get; }
    DateTimeOffset AcquiredAt { get; }
    DateTimeOffset ExpiresAt { get; }
}
