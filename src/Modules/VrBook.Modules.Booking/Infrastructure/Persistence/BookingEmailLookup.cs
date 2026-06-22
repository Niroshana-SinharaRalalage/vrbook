using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Booking.Infrastructure.Persistence;

internal sealed class BookingEmailLookup(BookingDbContext db) : IBookingEmailLookup
{
    public async Task<BookingEmailSnapshot?> GetAsync(Guid bookingId, CancellationToken ct = default)
    {
        var b = await db.Bookings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == bookingId, ct);
        if (b is null)
        {
            return null;
        }

        var nights = b.Stay.CheckoutDate.DayNumber - b.Stay.CheckinDate.DayNumber;
        var inv = CultureInfo.InvariantCulture;

        return new BookingEmailSnapshot(
            BookingId: b.Id,
            Reference: b.Reference,
            PropertyTitle: b.PropertyTitle,
            GuestDisplayName: b.GuestDisplayName,
            Checkin: b.Stay.CheckinDate.ToString("yyyy-MM-dd", inv),
            Checkout: b.Stay.CheckoutDate.ToString("yyyy-MM-dd", inv),
            Nights: nights,
            GuestCount: b.GuestCount,
            Currency: b.Currency,
            Subtotal: b.Subtotal.ToString("F2", inv),
            Fees: b.Fees.ToString("F2", inv),
            Taxes: b.Taxes.ToString("F2", inv),
            Total: b.Total.ToString("F2", inv),
            CancellationPolicy: b.CancellationPolicy.ToString());
    }
}
