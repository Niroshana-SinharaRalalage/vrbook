namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Read-side contract Messaging uses to bootstrap a thread when
/// <c>BookingConfirmed</c> fires. Returns the data not carried in the event
/// itself: GuestDisplayName + OwnerUserId + OwnerDisplayName + property title.
///
/// Implementation lives in the Booking module (joins booking.bookings with the
/// catalog read across modules). Saves Messaging from having to call both
/// Booking and Catalog separately.
/// </summary>
public interface IBookingMessagingContext
{
    Task<BookingMessagingSnapshot?> GetAsync(Guid bookingId, CancellationToken ct = default);
}

public sealed record BookingMessagingSnapshot(
    Guid BookingId,
    string Reference,
    Guid GuestUserId,
    string GuestDisplayName,
    Guid OwnerUserId,
    string OwnerDisplayName,
    string PropertyTitle,
    Guid? TenantId = null);
