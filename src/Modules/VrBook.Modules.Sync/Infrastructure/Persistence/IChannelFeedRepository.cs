using VrBook.Modules.Sync.Domain;

namespace VrBook.Modules.Sync.Infrastructure.Persistence;

public interface IChannelFeedRepository
{
    Task<IReadOnlyList<ChannelFeed>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ChannelFeed>> ListByPropertyAsync(Guid propertyId, CancellationToken ct = default);
    Task<ChannelFeed?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ChannelFeed?> GetByOutboundTokenAsync(string token, CancellationToken ct = default);

    /// <summary>Returns enabled feeds whose <c>IsDueForPoll(now)</c> is true.</summary>
    Task<IReadOnlyList<ChannelFeed>> ListDueForPollAsync(DateTimeOffset now, CancellationToken ct = default);
}

public interface IExternalReservationRepository
{
    Task<ExternalReservation?> GetByFeedAndUidAsync(Guid channelFeedId, string iCalUid, CancellationToken ct = default);
    Task<IReadOnlyList<ExternalReservation>> ListByFeedAsync(Guid channelFeedId, CancellationToken ct = default);

    /// <summary>Returns active reservations for the property whose date range overlaps
    /// the given window. Used by the conflict checker AND by ad-hoc admin queries.</summary>
    Task<IReadOnlyList<ExternalReservation>> ListOverlappingAsync(
        Guid propertyId, DateOnly checkin, DateOnly checkout, CancellationToken ct = default);
}

public interface ISyncConflictRepository
{
    Task<SyncConflict?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SyncConflict>> ListPendingAsync(CancellationToken ct = default);
    Task<SyncConflict?> FindByPairAsync(Guid bookingId, Guid externalReservationId, CancellationToken ct = default);
}
