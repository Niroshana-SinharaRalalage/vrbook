using VrBook.Contracts.Interfaces;
using VrBook.Modules.Sync.Infrastructure.Persistence;

namespace VrBook.Modules.Sync.Infrastructure;

/// <summary>
/// Replaces <c>StubExternalChannelConflictChecker</c> from VrBook.Infrastructure once
/// the Sync module ships (A6). Asks the local mirror of external reservations whether
/// any active row overlaps the given window for the property. The mirror is kept
/// fresh by the sync worker's */5 cron — staleness is bounded by feed
/// <c>PollIntervalMinutes</c>.
/// </summary>
internal sealed class RealExternalChannelConflictChecker(IExternalReservationRepository reservations)
    : IExternalChannelConflictChecker
{
    public async Task<bool> HasOverlapAsync(
        Guid propertyId,
        DateOnly checkin,
        DateOnly checkout,
        CancellationToken ct = default)
    {
        var overlapping = await reservations.ListOverlappingAsync(propertyId, checkin, checkout, ct);
        return overlapping.Count > 0;
    }
}
