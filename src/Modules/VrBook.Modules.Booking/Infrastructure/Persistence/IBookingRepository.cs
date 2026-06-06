using DomainBooking = VrBook.Modules.Booking.Domain.Booking;

namespace VrBook.Modules.Booking.Infrastructure.Persistence;

public interface IBookingRepository
{
    Task<DomainBooking?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DomainBooking?> GetByReferenceAsync(string reference, CancellationToken cancellationToken = default);
    Task AddAsync(DomainBooking booking, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DomainBooking>> ListForGuestAsync(Guid guestUserId, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns bookings on the given property that overlap [checkin, checkout) AND
    /// occupy the calendar (Tentative + Confirmed + CheckedIn). Cancelled / Rejected
    /// bookings free the dates.
    /// </summary>
    Task<IReadOnlyList<DomainBooking>> FindOverlapsAsync(Guid propertyId, DateOnly checkin, DateOnly checkout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns occupied date ranges on the property within [fromDate, toDate). Used by the
    /// frontend calendar to grey out unavailable dates.
    /// </summary>
    Task<IReadOnlyList<(DateOnly Checkin, DateOnly Checkout)>> ListBlockedRangesAsync(Guid propertyId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
}
