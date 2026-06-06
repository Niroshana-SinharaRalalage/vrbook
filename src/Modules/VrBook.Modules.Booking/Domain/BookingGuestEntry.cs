using VrBook.Domain.Common;

namespace VrBook.Modules.Booking.Domain;

/// <summary>
/// Named additional guest on a booking. Named "BookingGuestEntry" (not "BookingGuest")
/// to avoid clashing with the contract DTO of the same short name.
/// </summary>
public sealed class BookingGuestEntry : Entity
{
    public Guid BookingId { get; private set; }
    public string FullName { get; private set; } = default!;
    public bool IsPrimary { get; private set; }

    private BookingGuestEntry() { } // EF

    internal BookingGuestEntry(Guid bookingId, string fullName, bool isPrimary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        Id = Guid.NewGuid();
        BookingId = bookingId;
        FullName = fullName.Trim();
        IsPrimary = isPrimary;
    }
}
