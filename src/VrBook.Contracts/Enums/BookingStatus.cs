namespace VrBook.Contracts.Enums;

/// <summary>
/// Booking lifecycle states. See proposal §7.1 (state diagram) and §7.2 (transition table).
/// Order is meaningful for sorting but NOT for permitted transitions —
/// those live in <c>VrBook.Modules.Booking</c> behind the aggregate.
/// </summary>
public enum BookingStatus
{
    Draft = 0,
    Tentative = 1,
    Confirmed = 2,
    CheckedIn = 3,
    CheckedOut = 4,
    Completed = 5,
    Cancelled = 6,
    Rejected = 7,
    Disputed = 8,
    Refunded = 9,
}
