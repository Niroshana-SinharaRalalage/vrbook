using Microsoft.EntityFrameworkCore;
using VrBook.Modules.Sync.Domain;

namespace VrBook.Modules.Sync.Infrastructure.Persistence;

internal sealed class ChannelFeedRepository(SyncDbContext db) : IChannelFeedRepository
{
    public Task<IReadOnlyList<ChannelFeed>> ListAsync(CancellationToken ct = default) =>
        AsReadOnly(db.ChannelFeeds.OrderBy(f => f.PropertyId).ThenBy(f => f.Channel).ToListAsync(ct));

    public Task<IReadOnlyList<ChannelFeed>> ListByPropertyAsync(Guid propertyId, CancellationToken ct = default) =>
        AsReadOnly(db.ChannelFeeds.Where(f => f.PropertyId == propertyId).OrderBy(f => f.Channel).ToListAsync(ct));

    public Task<ChannelFeed?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.ChannelFeeds.FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<ChannelFeed?> GetByOutboundTokenAsync(string token, CancellationToken ct = default) =>
        db.ChannelFeeds.FirstOrDefaultAsync(f => f.OutboundToken == token, ct);

    public async Task<IReadOnlyList<ChannelFeed>> ListDueForPollAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        // EF can't translate IsDueForPoll() and DateDiff is provider-specific. Pull
        // all enabled feeds and filter in-memory. Phase 1 expects <100 feeds total —
        // tiny set, runs every 5 minutes. If we grow past a few hundred feeds we
        // can swap to a raw SQL projection using Postgres EXTRACT(EPOCH FROM …).
        var enabled = await db.ChannelFeeds.Where(f => f.IsEnabled).ToListAsync(ct);
        return enabled.Where(f => f.IsDueForPoll(now)).ToArray();
    }

    private static async Task<IReadOnlyList<T>> AsReadOnly<T>(Task<List<T>> source) => await source;
}

internal sealed class ExternalReservationRepository(SyncDbContext db) : IExternalReservationRepository
{
    public Task<ExternalReservation?> GetByFeedAndUidAsync(
        Guid channelFeedId, string iCalUid, CancellationToken ct = default) =>
        db.ExternalReservations
            .FirstOrDefaultAsync(r => r.ChannelFeedId == channelFeedId && r.ICalUid == iCalUid, ct);

    public Task<IReadOnlyList<ExternalReservation>> ListByFeedAsync(
        Guid channelFeedId, CancellationToken ct = default) =>
        AsReadOnly(db.ExternalReservations
            .Where(r => r.ChannelFeedId == channelFeedId)
            .OrderBy(r => r.Checkin)
            .ToListAsync(ct));

    public Task<IReadOnlyList<ExternalReservation>> ListOverlappingAsync(
        Guid propertyId, DateOnly checkin, DateOnly checkout, CancellationToken ct = default)
    {
        // Half-open overlap: er.Checkin < checkout AND checkin < er.Checkout
        var query = db.ExternalReservations
            .Where(r => r.PropertyId == propertyId
                     && r.CancelledAt == null
                     && r.Checkin < checkout
                     && checkin < r.Checkout);
        return AsReadOnly(query.ToListAsync(ct));
    }

    private static async Task<IReadOnlyList<T>> AsReadOnly<T>(Task<List<T>> source) => await source;
}

internal sealed class SyncConflictRepository(SyncDbContext db) : ISyncConflictRepository
{
    public Task<SyncConflict?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.SyncConflicts.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<IReadOnlyList<SyncConflict>> ListPendingAsync(CancellationToken ct = default) =>
        AsReadOnly(db.SyncConflicts
            .Where(c => c.Resolution == VrBook.Contracts.Enums.SyncConflictResolution.Pending)
            .OrderBy(c => c.DetectedAt)
            .ToListAsync(ct));

    public Task<SyncConflict?> FindByPairAsync(
        Guid bookingId, Guid externalReservationId, CancellationToken ct = default) =>
        db.SyncConflicts.FirstOrDefaultAsync(
            c => c.BookingId == bookingId && c.ExternalReservationId == externalReservationId, ct);

    private static async Task<IReadOnlyList<T>> AsReadOnly<T>(Task<List<T>> source) => await source;
}
