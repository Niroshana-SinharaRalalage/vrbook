using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Booking.Infrastructure.Persistence;

/// <summary>
/// Booking module's implementation of <see cref="IConfirmedBookingLookup"/>. Used
/// exclusively by the Sync module for conflict detection (A6 stage 5). Returns
/// any direct booking whose stay overlaps the given window for the property —
/// half-open semantics consistent with <c>Stay</c> in the domain.
///
/// Includes Confirmed AND CheckedIn AND CheckedOut so an arriving external
/// reservation on dates an existing guest already used produces an audit-trail
/// conflict even after the direct guest left. Tentative is excluded — a
/// tentative booking can still be rejected, so the external reservation has not
/// "won" yet from the host's perspective.
/// </summary>
internal sealed class ConfirmedBookingLookup(BookingDbContext db) : IConfirmedBookingLookup
{
    public async Task<IReadOnlyList<ConfirmedBookingOverlap>> FindOverlappingAsync(
        Guid propertyId,
        DateOnly checkin,
        DateOnly checkout,
        CancellationToken ct = default)
    {
        var rows = await db.Bookings
            .AsNoTracking()
            .Where(b => b.PropertyId == propertyId)
            .Where(b => b.Status == BookingStatus.Confirmed
                     || b.Status == BookingStatus.CheckedIn
                     || b.Status == BookingStatus.CheckedOut)
            .Where(b => b.Stay.CheckinDate < checkout && checkin < b.Stay.CheckoutDate)
            .Select(b => new ConfirmedBookingOverlap(
                b.Id,
                b.Stay.CheckinDate,
                b.Stay.CheckoutDate,
                b.GuestDisplayName,
                b.Reference))
            .ToListAsync(ct);
        return rows;
    }
}
