using VrBook.Domain.Common;

namespace VrBook.Modules.Catalog.Domain;

/// <summary>Check-in/out times for a property. All times local to the property.</summary>
public sealed class CheckInWindow : ValueObject
{
    public TimeOnly CheckinFrom { get; }
    public TimeOnly CheckinTo { get; }
    public TimeOnly CheckoutBy { get; }

    public CheckInWindow(TimeOnly checkinFrom, TimeOnly checkinTo, TimeOnly checkoutBy)
    {
        if (checkinFrom > checkinTo)
        {
            throw new ArgumentException("CheckinFrom must be <= CheckinTo", nameof(checkinFrom));
        }
        CheckinFrom = checkinFrom;
        CheckinTo = checkinTo;
        CheckoutBy = checkoutBy;
    }

    private CheckInWindow() { } // EF

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CheckinFrom;
        yield return CheckinTo;
        yield return CheckoutBy;
    }
}
