using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Booking.Domain;
using VrBook.Modules.Booking.Infrastructure.Persistence;

namespace VrBook.Modules.Booking.Infrastructure.Holds;

/// <summary>
/// Postgres-backed <see cref="IHoldStore"/> — Phase 1 default. The proposal
/// §7.3/§9.3 model used Redis for liveness with a Postgres mirror; with Azure
/// Cache for Redis being retired (2026), staging can't cheaply provision a
/// classic Redis instance and Azure Managed Redis is ~5x cost at the smallest
/// SKU. For a Phase 1 demo + first pilot the entire hold flow runs on the
/// Postgres mirror we already have:
///
///   * <see cref="CreateAsync"/> runs in a SERIALIZABLE transaction, checks
///     for any active (or unconsumed/unreleased and not yet expired) hold
///     overlapping the requested range via SELECT ... FOR UPDATE, and INSERTs
///     a new <see cref="BookingHold"/> if clear.
///   * Concurrent attempts for the same range observe each other's row lock
///     and the loser receives 40001 serialization_failure which we surface
///     as a 409 <see cref="ConflictException"/>.
///
/// When Slice 7 (or a later hardening pass) brings up Azure Managed Redis,
/// the existing <see cref="RedisHoldStore"/> stays in the codebase and the
/// DI registration switches by config flag. The <see cref="IHoldStore"/>
/// contract is unchanged.
/// </summary>
internal sealed class PostgresHoldStore(
    BookingDbContext db,
    ILogger<PostgresHoldStore> logger) : IHoldStore
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

        var holdId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(ttl);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            // Lock any active, unexpired holds that overlap the requested range.
            // status: Active=0, Consumed=1, Released=2, Expired=3.
            // Note: Postgres rejects SELECT COUNT(*) ... FOR UPDATE
            // (0A000: "FOR UPDATE is not allowed with aggregate functions").
            // Select the IDs instead and check HasRows.
            const string overlapSql = """
                SELECT id FROM booking.booking_holds
                WHERE property_id = @p0
                  AND status = 0
                  AND expires_at > @p1
                  AND checkin < @p3
                  AND @p2 < checkout
                FOR UPDATE
                """;
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            cmd.CommandText = overlapSql;
            AddParam(cmd, "@p0", propertyId);
            AddParam(cmd, "@p1", now);
            AddParam(cmd, "@p2", checkin);
            AddParam(cmd, "@p3", checkout);
            bool anyOverlap;
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                anyOverlap = await reader.ReadAsync(ct);
            }
            if (anyOverlap)
            {
                throw new ConflictException(
                    $"Another guest is currently holding these dates for property {propertyId}. " +
                    "Please retry in a few minutes.");
            }

            var hold = BookingHold.Create(holdId, propertyId, checkin, checkout, guests, sessionId, expiresAt);
            db.Set<BookingHold>().Add(hold);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg && pg.SqlState == "40001")
        {
            // Concurrent transaction committed an overlapping hold between our
            // FOR UPDATE check and our COMMIT.
            logger.LogInformation(ex,
                "Hold race lost on property {PropertyId} for {Checkin}-{Checkout}; returning 409.",
                propertyId, checkin, checkout);
            throw new ConflictException(
                $"Another guest is currently holding these dates for property {propertyId}. " +
                "Please retry in a few minutes.");
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
        var hold = await db.Set<BookingHold>().FirstOrDefaultAsync(h => h.Id == holdId, ct);
        if (hold is null
            || hold.Status != HoldStatus.Active
            || hold.PropertyId != propertyId
            || hold.Checkin != checkin
            || hold.Checkout != checkout
            || hold.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return false;
        }
        hold.MarkConsumed(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReleaseAsync(Guid holdId, CancellationToken ct = default)
    {
        var hold = await db.Set<BookingHold>().FirstOrDefaultAsync(h => h.Id == holdId, ct);
        if (hold is not null && hold.Status == HoldStatus.Active)
        {
            hold.MarkReleased(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
        }
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
