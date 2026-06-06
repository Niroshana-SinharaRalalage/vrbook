using DomainBooking = VrBook.Modules.Booking.Domain.Booking;

namespace VrBook.Modules.Booking.Infrastructure.Persistence;

public interface IBookingRepository
{
    Task<DomainBooking?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DomainBooking?> GetByReferenceAsync(string reference, CancellationToken cancellationToken = default);
    Task AddAsync(DomainBooking booking, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DomainBooking>> ListForGuestAsync(Guid guestUserId, int skip, int take, CancellationToken cancellationToken = default);
}
