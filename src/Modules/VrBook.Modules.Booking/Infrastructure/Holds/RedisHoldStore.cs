using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Booking.Domain;
using VrBook.Modules.Booking.Infrastructure.Persistence;

namespace VrBook.Modules.Booking.Infrastructure.Holds;

/// <summary>
/// Redis-backed implementation of <see cref="IHoldStore"/> per proposal §7.3
/// and §9.3. Two-tier durability:
///   * Redis: authoritative for liveness. SET NX with TTL on the
///     <c>vrbook:hold:{propertyId}:{checkin:O}:{checkout:O}</c> key returns the
///     hold id (Guid hex) iff the key was set; concurrent attempts on the same
///     range get a 409. A reverse index <c>vrbook:hold:byId:{holdId}</c> stores
///     the canonical key so <see cref="ReleaseAsync"/> can DEL by holdId.
///   * Postgres: <c>booking.booking_holds</c> mirror lets restart reconcile
///     stale state and gives the audit trail. The DB row is created in the
///     same call as the Redis SET; on Redis failure no row is written.
///
/// Slice 0.1 — closes the §7.3 race risk for concurrent checkouts.
/// </summary>
internal sealed class RedisHoldStore(
    IConnectionMultiplexer redis,
    BookingDbContext db,
    IPropertyOwnerLookup propertyOwners,
    ILogger<RedisHoldStore> logger) : IHoldStore
{
    public async Task<HoldDto> CreateAsync(
        Guid propertyId,
        DateOnly checkin,
        DateOnly checkout,
        int guests,
        Guid? sessionId,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        if (checkout <= checkin)
        {
            throw new BusinessRuleViolationException(
                "booking.hold.date_range",
                "Checkout must be after checkin.");
        }

        var key = RangeKey(propertyId, checkin, checkout);
        var holdId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
        var redisDb = redis.GetDatabase();

        // SET NX: returns false if the key already exists (someone else holds it).
        var acquired = await redisDb.StringSetAsync(
            key, holdId.ToString("N"), ttl, When.NotExists);
        if (!acquired)
        {
            throw new ConflictException(
                $"Another guest is currently holding these dates for property {propertyId}. " +
                "Please retry in a few minutes.");
        }

        // Reverse index for ReleaseAsync(holdId).
        await redisDb.StringSetAsync(IdKey(holdId), key, ttl);

        // Mirror to Postgres for audit + restart reconciliation.
        // OPS.M.3c — TenantId from the property; default-tenant fallback only
        // for orphan rows (post-Wave-B there should be none).
        var owner = await propertyOwners.GetAsync(propertyId, ct);
        var holdTenantId = owner!.TenantId;
        var hold = BookingHold.Create(
            holdTenantId,
            holdId, propertyId, checkin, checkout, guests, sessionId, expiresAt);
        db.Set<BookingHold>().Add(hold);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // DB write failed — release Redis so we don't hold dates the audit
            // trail doesn't know about.
            await redisDb.KeyDeleteAsync(key);
            await redisDb.KeyDeleteAsync(IdKey(holdId));
            logger.LogError(ex, "Failed to persist BookingHold {HoldId}; Redis hold rolled back.", holdId);
            throw;
        }

        return new HoldDto(holdId, propertyId, checkin, checkout, expiresAt);
    }

    public async Task<bool> TryConsumeAsync(
        Guid holdId,
        Guid propertyId,
        DateOnly checkin,
        DateOnly checkout,
        CancellationToken ct = default)
    {
        var key = RangeKey(propertyId, checkin, checkout);
        var redisDb = redis.GetDatabase();
        var current = await redisDb.StringGetAsync(key);
        if (current.IsNullOrEmpty || current != holdId.ToString("N"))
        {
            return false; // expired or belongs to another session
        }
        await redisDb.KeyDeleteAsync(key);
        await redisDb.KeyDeleteAsync(IdKey(holdId));

        var row = await db.Set<BookingHold>().FirstOrDefaultAsync(h => h.Id == holdId, ct);
        if (row is not null)
        {
            row.MarkConsumed(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    public async Task ReleaseAsync(Guid holdId, Guid? expectedSessionId, CancellationToken ct = default)
    {
        // Slice OPS.M.10.2 F9 (audit #22) — verify ownership against the
        // mirrored DB row before deleting the Redis keys. Null
        // expectedSessionId preserves unconditional release for sweep /
        // admin cleanup paths.
        var row = await db.Set<BookingHold>().FirstOrDefaultAsync(h => h.Id == holdId, ct);
        if (row is null)
        {
            return;
        }
        if (expectedSessionId is not null && row.SessionId != expectedSessionId)
        {
            return;
        }

        var redisDb = redis.GetDatabase();
        var rangeKey = await redisDb.StringGetAsync(IdKey(holdId));
        if (!rangeKey.IsNullOrEmpty)
        {
            await redisDb.KeyDeleteAsync(rangeKey.ToString());
        }
        await redisDb.KeyDeleteAsync(IdKey(holdId));

        if (row.Status == HoldStatus.Active)
        {
            row.MarkReleased(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
        }
    }

    private static string RangeKey(Guid propertyId, DateOnly checkin, DateOnly checkout) =>
        $"vrbook:hold:{propertyId}:{checkin:yyyy-MM-dd}:{checkout:yyyy-MM-dd}";

    private static string IdKey(Guid holdId) => $"vrbook:hold:byId:{holdId}";
}
