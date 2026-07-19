using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Booking.Infrastructure.Persistence;

/// <summary>
/// VRB-102 Phase B — Booking-side impl of <see cref="IBookingCancellationLookup"/>.
/// The Payment refund path reads the booking's snapshotted policy through this so
/// it never references the Booking module (mirrors <c>BookingEmailLookup</c>).
/// </summary>
internal sealed class BookingCancellationLookup(BookingDbContext db) : IBookingCancellationLookup
{
    public async Task<BookingCancellationInfo?> GetAsync(Guid bookingId, CancellationToken ct = default)
    {
        var b = db.Bookings.Local.FirstOrDefault(x => x.Id == bookingId)
            ?? await db.Bookings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == bookingId, ct);
        if (b is null)
        {
            return null;
        }
        return new BookingCancellationInfo(
            BookingId: b.Id,
            CheckinDate: b.Stay.CheckinDate,
            Model: b.CancellationSnapshotModel,
            FirstTierDays: b.CancellationFirstTierDays,
            SecondTierDays: b.CancellationSecondTierDays,
            MiddleTierRefundPct: b.CancellationMiddleTierRefundPct,
            FinalCutoffHours: b.CancellationFinalCutoffHours,
            TierVersion: b.CancellationTierVersion,
            RefundableUpgradePurchased: b.RefundableUpgradePurchased,
            RefundableUpgradePriceAmount: b.RefundableUpgradePriceAmount,
            RefundableUpgradePriceCurrency: b.RefundableUpgradePriceCurrency);
    }
}
