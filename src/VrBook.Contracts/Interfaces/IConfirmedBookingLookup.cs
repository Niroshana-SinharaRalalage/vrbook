namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Read-side contract Sync uses to detect direct bookings that overlap an
/// imported external (AirBnB) reservation. Implementation lives in the Booking
/// module; consumers (Sync) inject this from DI.
///
/// Pattern decision (A6 stage 5): cross-module data access goes through a thin
/// query interface in Contracts. Sync NEVER reads <c>booking.bookings</c>
/// directly — that would break the modular-monolith ownership rule.
/// </summary>
public interface IConfirmedBookingLookup
{
    Task<IReadOnlyList<ConfirmedBookingOverlap>> FindOverlappingAsync(
        Guid propertyId,
        DateOnly checkin,
        DateOnly checkout,
        CancellationToken ct = default);
}

/// <summary>Compact projection of an overlapping direct booking — just enough
/// data for Sync to record a <c>SyncConflict</c> row.</summary>
public sealed record ConfirmedBookingOverlap(
    Guid BookingId,
    DateOnly Checkin,
    DateOnly Checkout,
    string GuestDisplayName,
    string Reference);
