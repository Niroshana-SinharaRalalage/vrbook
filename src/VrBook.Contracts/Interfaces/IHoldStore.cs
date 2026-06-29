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

    /// <summary>
    /// Releases the hold. If <paramref name="expectedSessionId"/> is non-null,
    /// the stored sessionId on the hold must match — otherwise the call is a
    /// no-op (defense against an attacker guessing another user's HoldId).
    /// Slice OPS.M.10.2 F9 (audit #22). A null <paramref name="expectedSessionId"/>
    /// preserves the prior unconditional-release semantics, used by the
    /// background sweep and admin cleanup paths.
    /// </summary>
    Task ReleaseAsync(Guid holdId, Guid? expectedSessionId, CancellationToken ct = default);
}
