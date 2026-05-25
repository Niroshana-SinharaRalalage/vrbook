using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;

namespace VrBook.Infrastructure.Stubs;

/// <summary>
/// A0 stub for Catalog → Booking integration. Returns "always available" so the Catalog
/// module can be built and demonstrated before the Booking module ships in A4.
/// </summary>
public sealed class StubBookingAvailabilityReader : IBookingAvailabilityReader
{
    public Task<bool> IsAvailableAsync(
        Guid propertyId, DateOnly checkin, DateOnly checkout, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<IReadOnlyList<AvailabilityDayDto>> GetDailyAvailabilityAsync(
        Guid propertyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        var days = new List<AvailabilityDayDto>();
        for (var d = fromDate; d < toDate; d = d.AddDays(1))
        {
            days.Add(new AvailabilityDayDto(d, Available: true, BlockReason: null));
        }
        return Task.FromResult<IReadOnlyList<AvailabilityDayDto>>(days);
    }
}
