using StackExchange.Redis;
using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Redis;

/// <summary>
/// Redis-backed distributed lock using <c>SET NX PX</c> and a Lua release script.
/// Used by the booking hold flow (proposal §7.3). The lock token is a per-instance
/// GUID — release is conditional on the token matching, so a lock can never be released
/// by a process that no longer owns it.
/// </summary>
public sealed class RedisDistributedLock(IConnectionMultiplexer redis) : IDistributedLock
{
    private const string ReleaseLuaScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end
    """;

    public async Task<IDistributedLockHandle?> TryAcquireAsync(
        string key,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var token = Guid.NewGuid().ToString("n");

        var acquired = await db.StringSetAsync(
            key, token, ttl, When.NotExists);

        if (!acquired)
        {
            return null;
        }

        return new Handle(redis, key, token, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.Add(ttl));
    }

    private sealed class Handle(
        IConnectionMultiplexer redis,
        string key,
        string token,
        DateTimeOffset acquiredAt,
        DateTimeOffset expiresAt) : IDistributedLockHandle
    {
        public string Key { get; } = key;
        public string Token { get; } = token;
        public DateTimeOffset AcquiredAt { get; } = acquiredAt;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public async ValueTask DisposeAsync()
        {
            try
            {
                var db = redis.GetDatabase();
                await db.ScriptEvaluateAsync(
                    ReleaseLuaScript,
                    keys: new RedisKey[] { Key },
                    values: new RedisValue[] { Token });
            }
            catch
            {
                // Best-effort release; lock will expire by TTL anyway.
            }
        }
    }
}
