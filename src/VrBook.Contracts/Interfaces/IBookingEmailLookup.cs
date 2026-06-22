namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Slice 4 polish: cross-module read used by Notifications to enrich
/// queued-row payloads with the booking's <em>display</em> fields (property
/// title, totals, guest name). Booking domain events deliberately carry only
/// ids; the email body needs richer context. Implementation lives in the
/// Booking module against the Booking aggregate; the interface keeps the
/// Notifications module free of a Booking-module reference.
/// </summary>
public interface IBookingEmailLookup
{
    Task<BookingEmailSnapshot?> GetAsync(Guid bookingId, CancellationToken ct = default);
}

/// <summary>Pre-formatted strings so the Mustache renderer can stamp values directly.</summary>
public sealed record BookingEmailSnapshot(
    Guid BookingId,
    string Reference,
    string PropertyTitle,
    string GuestDisplayName,
    string Checkin,         // yyyy-MM-dd
    string Checkout,        // yyyy-MM-dd
    int Nights,
    int GuestCount,
    string Currency,
    string Subtotal,        // e.g. "360.00"
    string Fees,
    string Taxes,
    string Total,
    string CancellationPolicy);
