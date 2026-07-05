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
                // Slice OPS.M.16 — overlap rule:
                //   * For CheckedOut bookings, block same-day turnover
                //     (checkin_new <= checkout_existing). This prevents a
                //     guest checking in on the day housekeeping is still
                //     in progress. Unblocks once the admin flips the
                //     booking to Completed OR the daily sweep does so at
                //     the snapshotted CompletionDueAt.
                //   * For every other still-active status (Tentative,
                //     Confirmed, CheckedIn, Completed), keep the strict
                //     half-open [checkin, checkout) hospitality standard
                //     — turnover-day shared arrivals ARE allowed.
                //   * Cancelled / Rejected / Refunded never block.
                && b.Status != BookingStatus.Cancelled
                && b.Status != BookingStatus.Rejected
                && b.Status != BookingStatus.Refunded
                && b.Stay.CheckinDate < checkout
                && (b.Status == BookingStatus.CheckedOut
                        ? checkin <= b.Stay.CheckoutDate
                        : checkin < b.Stay.CheckoutDate))
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
            .Select(b => new { b.Stay.CheckinDate, b.Stay.CheckoutDate, b.Status })
            .ToListAsync(cancellationToken);

        // Slice OPS.M.16 — for CheckedOut bookings, extend the blocked range
        // by one day so the guest availability calendar reflects the
        // turnover-day soft block. DateOnly.AddDays translated poorly in
        // EF for this composite projection so we materialize first and
        // reshape in memory.
        return rows.Select(r => (
            r.CheckinDate,
            r.Status == BookingStatus.CheckedOut ? r.CheckoutDate.AddDays(1) : r.CheckoutDate
        )).ToArray();
    }
}
