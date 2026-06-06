using VrBook.Domain.Common;

namespace VrBook.Modules.Booking.Domain;

/// <summary>Half-open stay range. CheckoutDate is the day they leave (not a night).</summary>
public sealed class Stay : ValueObject
{
    public DateOnly CheckinDate { get; }
    public DateOnly CheckoutDate { get; }
    public int Nights => CheckoutDate.DayNumber - CheckinDate.DayNumber;

    public Stay(DateOnly checkin, DateOnly checkout)
    {
        if (checkout <= checkin)
        {
            throw new ArgumentException("CheckoutDate must be after CheckinDate.", nameof(checkout));
        }
        CheckinDate = checkin;
        CheckoutDate = checkout;
    }

    private Stay() { } // EF

    public bool Overlaps(Stay other) => CheckinDate < other.CheckoutDate && other.CheckinDate < CheckoutDate;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CheckinDate;
        yield return CheckoutDate;
    }
}
