using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Enums;
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

    public async Task<IReadOnlyList<DomainBooking>> FindOverlapsAsync(Guid propertyId, DateOnly checkin, DateOnly checkout, CancellationToken cancellationToken = default) =>
        await db.Bookings
            .AsNoTracking()
            .Where(b => b.PropertyId == propertyId
                && (b.Status == BookingStatus.Tentative
                    || b.Status == BookingStatus.Confirmed
                    || b.Status == BookingStatus.CheckedIn)
                && b.Stay.CheckinDate < checkout
                && checkin < b.Stay.CheckoutDate)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(DateOnly Checkin, DateOnly Checkout)>> ListBlockedRangesAsync(Guid propertyId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default)
    {
        var rows = await db.Bookings
            .AsNoTracking()
            .Where(b => b.PropertyId == propertyId
                && (b.Status == BookingStatus.Tentative
                    || b.Status == BookingStatus.Confirmed
                    || b.Status == BookingStatus.CheckedIn
                    || b.Status == BookingStatus.CheckedOut
                    || b.Status == BookingStatus.Completed)
                && b.Stay.CheckinDate < toDate
                && fromDate < b.Stay.CheckoutDate)
            .Select(b => new { b.Stay.CheckinDate, b.Stay.CheckoutDate })
            .ToListAsync(cancellationToken);
        return rows.Select(r => (r.CheckinDate, r.CheckoutDate)).ToArray();
    }
}
