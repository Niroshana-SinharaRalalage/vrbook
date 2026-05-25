using VrBook.Contracts.Dtos;

namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Booking hold lifecycle. Holds are stored in Redis (authoritative for liveness)
/// and mirrored to <c>booking.booking_holds</c> so stale state can be cleaned on restart.
/// </summary>
public interface IHoldStore
{
    Task<HoldDto> CreateAsync(
        Guid propertyId,
        DateOnly checkin,
        DateOnly checkout,
        int guests,
        Guid? sessionId,
        TimeSpan ttl,
        CancellationToken ct = default);

    Task<bool> TryConsumeAsync(
        Guid holdId,
        Guid propertyId,
        DateOnly checkin,
        DateOnly checkout,
        CancellationToken ct = default);

    Task ReleaseAsync(Guid holdId, CancellationToken ct = default);
}
