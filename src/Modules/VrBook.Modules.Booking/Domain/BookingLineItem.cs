using VrBook.Domain.Common;

namespace VrBook.Modules.Booking.Domain;

/// <summary>
/// Line item captured at booking creation time. Snapshots whatever the Pricing
/// engine computed so the price is fixed once booked, even if pricing rules change.
/// </summary>
public sealed class BookingLineItem : Entity
{
    public Guid BookingId { get; private set; }
    public string Kind { get; private set; } = default!;         // "Nightly" | "Cleaning" | "Tax" | etc.
    public string Label { get; private set; } = default!;
    public int Quantity { get; private set; }
    public decimal UnitAmount { get; private set; }
    public decimal LineTotal { get; private set; }

    private BookingLineItem() { } // EF

    internal BookingLineItem(Guid bookingId, string kind, string label, int quantity, decimal unitAmount, decimal lineTotal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Id = Guid.NewGuid();
        BookingId = bookingId;
        Kind = kind.Trim();
        Label = label.Trim();
        Quantity = quantity;
        UnitAmount = unitAmount;
        LineTotal = lineTotal;
    }
}
