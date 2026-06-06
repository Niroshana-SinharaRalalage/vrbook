using Microsoft.EntityFrameworkCore;
using DomainBooking = VrBook.Modules.Booking.Domain.Booking;

namespace VrBook.Modules.Booking.Infrastructure.Persistence;

internal sealed class BookingRepository(BookingDbContext db) : IBookingRepository
{
    public Task<DomainBooking?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Bookings
            .Include(b => b.LineItems)
            .Include(b => b.Guests)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public Task<DomainBooking?> GetByReferenceAsync(string reference, CancellationToken cancellationToken = default) =>
        db.Bookings
            .Include(b => b.LineItems)
            .Include(b => b.Guests)
            .FirstOrDefaultAsync(b => b.Reference == reference, cancellationToken);

    public Task AddAsync(DomainBooking booking, CancellationToken cancellationToken = default)
    {
        db.Bookings.Add(booking);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<DomainBooking>> ListForGuestAsync(Guid guestUserId, int skip, int take, CancellationToken cancellationToken = default) =>
        await db.Bookings
            .Where(b => b.GuestUserId == guestUserId)
            .OrderByDescending(b => b.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
}
