using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Interfaces;

/// <summary>
/// VRB-102 Phase B — cross-module read used by the Payment refund path to resolve
/// the snapshotted cancellation policy + check-in date for a booking, without the
/// Payment module referencing the Booking module (mirrors <see cref="IBookingEmailLookup"/>).
/// Returns null when the booking is unknown; <see cref="BookingCancellationInfo.Model"/>
/// is null when the booking predates the snapshot (refund then falls back to the flat
/// service-fee policy).
/// </summary>
public interface IBookingCancellationLookup
{
    Task<BookingCancellationInfo?> GetAsync(Guid bookingId, CancellationToken ct = default);
}

/// <summary>The booking's snapshotted policy + check-in, flattened for cross-module transport.</summary>
public sealed record BookingCancellationInfo(
    Guid BookingId,
    DateOnly CheckinDate,
    CancellationModel? Model,
    int? FirstTierDays,
    int? SecondTierDays,
    int? MiddleTierRefundPct,
    int? FinalCutoffHours,
    int? TierVersion,
    bool RefundableUpgradePurchased,
    decimal? RefundableUpgradePriceAmount,
    string? RefundableUpgradePriceCurrency);
