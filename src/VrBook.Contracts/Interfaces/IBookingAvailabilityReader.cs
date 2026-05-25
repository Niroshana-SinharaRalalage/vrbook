using VrBook.Contracts.Dtos;

namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Read-only view of booking availability for the Catalog module's search + property pages.
/// Owned and implemented by the Booking module; stub returns "always available" until A4 ships.
/// </summary>
public interface IBookingAvailabilityReader
{
    Task<bool> IsAvailableAsync(
        Guid propertyId,
        DateOnly checkin,
        DateOnly checkout,
        CancellationToken ct = default);

    Task<IReadOnlyList<AvailabilityDayDto>> GetDailyAvailabilityAsync(
        Guid propertyId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct = default);
}
