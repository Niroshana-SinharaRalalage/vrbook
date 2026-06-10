using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Domain.Common;

namespace VrBook.Modules.Booking.Domain;

/// <summary>
/// Booking aggregate root. Implements the state machine in proposal §7.1.
/// Phase-1 simplifications: no hold integration, no payment integration, no
/// loyalty discount, no refund computation. Those land in A4.1+.
/// </summary>
public sealed class Booking : AggregateRoot
{
    public string Reference { get; private set; } = default!;
    public Guid PropertyId { get; private set; }
    public string PropertyTitle { get; private set; } = default!;
    public Guid GuestUserId { get; private set; }
    public string GuestDisplayName { get; private set; } = default!;
    public Stay Stay { get; private set; } = default!;
    public int GuestCount { get; private set; }

    public BookingStatus Status { get; private set; }
    public BookingSource Source { get; private set; }
    public string Currency { get; private set; } = "USD";

    public decimal Subtotal { get; private set; }
    public decimal Fees { get; private set; }
    public decimal Taxes { get; private set; }
    /// <summary>Loyalty / promo discount captured at booking time. Wired in A4.1 + A8.</summary>
    public decimal Discount { get; internal set; }
    public decimal Total { get; private set; }

    public CancellationPolicyCode CancellationPolicy { get; private set; } = CancellationPolicyCode.Moderate;

    public DateTimeOffset? TentativeUntil { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? CheckedInAt { get; private set; }
    public DateTimeOffset? CheckedOutAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? SpecialRequests { get; private set; }

    private readonly List<BookingLineItem> _lineItems = new();
    public IReadOnlyList<BookingLineItem> LineItems => _lineItems;

    private readonly List<BookingGuestEntry> _guests = new();
    public IReadOnlyList<BookingGuestEntry> Guests => _guests;

    private Booking() { } // EF

    public static Booking Place(
        Guid propertyId,
        string propertyTitle,
        Guid guestUserId,
        string guestDisplayName,
        Stay stay,
        int guestCount,
        string currency,
        decimal subtotal,
        decimal fees,
        decimal taxes,
        decimal total,
        IEnumerable<(string kind, string label, int qty, decimal unit, decimal lineTotal)> lineItems,
        IEnumerable<(string fullName, bool isPrimary)> guests,
        string? specialRequests)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(guestDisplayName);
        ArgumentOutOfRangeException.ThrowIfLessThan(guestCount, 1);

        var b = new Booking
        {
            Id = Guid.NewGuid(),
            Reference = BookingReference.Generate(),
            PropertyId = propertyId,
            PropertyTitle = propertyTitle,
            GuestUserId = guestUserId,
            GuestDisplayName = guestDisplayName,
            Stay = stay,
            GuestCount = guestCount,
            Status = BookingStatus.Tentative,
            Source = BookingSource.Direct,
            Currency = currency.ToUpperInvariant(),
            Subtotal = subtotal,
            Fees = fees,
            Taxes = taxes,
            Total = total,
            TentativeUntil = DateTimeOffset.UtcNow.AddHours(24),
            SpecialRequests = string.IsNullOrWhiteSpace(specialRequests) ? null : specialRequests.Trim(),
        };
        foreach (var (kind, label, qty, unit, lineTotal) in lineItems)
        {
            b._lineItems.Add(new BookingLineItem(b.Id, kind, label, qty, unit, lineTotal));
        }
        foreach (var (name, isPrimary) in guests)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            b._guests.Add(new BookingGuestEntry(b.Id, name, isPrimary));
        }
        b.Raise(new BookingPlaced(b.Id, b.Reference, propertyId, guestUserId,
            stay.CheckinDate, stay.CheckoutDate, b.TentativeUntil!.Value));
        return b;
    }

    // ---- State transitions ----
    public void Confirm()
    {
        Require(BookingStatus.Tentative);
        Status = BookingStatus.Confirmed;
        ConfirmedAt = DateTimeOffset.UtcNow;
        TentativeUntil = null;
        Raise(new BookingConfirmed(Id, Reference, PropertyId, GuestUserId,
            Stay.CheckinDate, Stay.CheckoutDate, "owner"));
    }

    public void Reject(string reason)
    {
        Require(BookingStatus.Tentative);
        Status = BookingStatus.Rejected;
        CancelledAt = DateTimeOffset.UtcNow;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Rejected by host" : reason.Trim();
        Raise(new BookingRejected(Id, Reference, PropertyId, GuestUserId, CancellationReason));
    }

    public void CancelByGuest(string reason)
    {
        if (Status is not (BookingStatus.Tentative or BookingStatus.Confirmed))
        {
            throw new BusinessRuleViolationException("booking.cancel", $"Cannot cancel a booking in {Status} state.");
        }
        Status = BookingStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Cancelled by guest" : reason.Trim();
        // A4 v1: refund computation lives in A5 (Payment). Publish 0 for now.
        Raise(new BookingCancelled(Id, Reference, PropertyId, GuestUserId, "guest", 0m, Currency));
    }

    public void CheckIn()
    {
        Require(BookingStatus.Confirmed);
        Status = BookingStatus.CheckedIn;
        CheckedInAt = DateTimeOffset.UtcNow;
        Raise(new BookingCheckedIn(Id, Reference));
    }

    public void CheckOut()
    {
        Require(BookingStatus.CheckedIn);
        Status = BookingStatus.CheckedOut;
        CheckedOutAt = DateTimeOffset.UtcNow;
        Raise(new BookingCheckedOut(Id, Reference));
        // A8.1: BookingCompleted is the business event Loyalty consumes to
        // increment the guest's completed-stay count and recompute their tier.
        Raise(new BookingCompleted(Id, Reference, GuestUserId));
    }

    private void Require(BookingStatus expected)
    {
        if (Status != expected)
        {
            throw new BusinessRuleViolationException(
                "booking.state",
                $"Cannot transition from {Status} when {expected} is required.");
        }
    }
}
