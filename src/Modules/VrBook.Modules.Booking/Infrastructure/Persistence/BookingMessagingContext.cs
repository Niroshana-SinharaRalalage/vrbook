using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Booking.Infrastructure.Persistence;

/// <summary>
/// Composes a <see cref="BookingMessagingSnapshot"/> from a booking row plus the
/// property owner (via <see cref="IPropertyOwnerLookup"/>). Used by the A7.4
/// auto-thread handler when <c>BookingConfirmed</c> fires.
///
/// Phase-1 limitation: OwnerDisplayName falls back to "Property owner" since
/// Identity-side display-name lookup isn't wired through Contracts yet. A
/// future polish can add <c>IUserDisplayNameLookup</c> in Contracts.
/// </summary>
internal sealed class BookingMessagingContext(
    BookingDbContext db,
    IPropertyOwnerLookup propertyOwners) : IBookingMessagingContext
{
    public async Task<BookingMessagingSnapshot?> GetAsync(Guid bookingId, CancellationToken ct = default)
    {
        var b = await db.Bookings
            .AsNoTracking()
            .Where(x => x.Id == bookingId)
            .Select(x => new { x.Id, x.Reference, x.PropertyId, x.GuestUserId, x.GuestDisplayName })
            .FirstOrDefaultAsync(ct);
        if (b is null)
        {
            return null;
        }
        var owner = await propertyOwners.GetAsync(b.PropertyId, ct);
        if (owner is null)
        {
            return null;
        }
        return new BookingMessagingSnapshot(
            BookingId: b.Id,
            Reference: b.Reference,
            GuestUserId: b.GuestUserId,
            GuestDisplayName: b.GuestDisplayName,
            OwnerUserId: owner.OwnerUserId,
            OwnerDisplayName: "Property owner",
            PropertyTitle: owner.Title);
    }
}
