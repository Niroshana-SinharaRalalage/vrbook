using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Dtos;

/// <summary>
/// Slice 0.6 — multi-source availability view for the /admin/calendar screen.
/// Merges direct bookings (booking schema), external reservations (sync schema),
/// and active holds (booking_holds + Redis) into one DTO bounded by a date window.
/// </summary>
public sealed record PropertyCalendarDto(
    Guid PropertyId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CalendarBookingEntry> Bookings,
    IReadOnlyList<CalendarExternalEntry> ExternalReservations,
    IReadOnlyList<CalendarHoldEntry> Holds);

public sealed record CalendarBookingEntry(
    Guid BookingId,
    string Reference,
    DateOnly Checkin,
    DateOnly Checkout,
    BookingStatus Status,
    string GuestDisplayName);

public sealed record CalendarExternalEntry(
    Guid ExternalReservationId,
    ChannelKind Channel,
    DateOnly Checkin,
    DateOnly Checkout,
    string? Summary);

public sealed record CalendarHoldEntry(
    Guid HoldId,
    DateOnly Checkin,
    DateOnly Checkout,
    DateTimeOffset ExpiresAt);
