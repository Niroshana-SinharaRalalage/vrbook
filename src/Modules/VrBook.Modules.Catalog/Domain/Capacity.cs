using VrBook.Domain.Common;

namespace VrBook.Modules.Catalog.Domain;

/// <summary>
/// Bed/room/guest capacity of a property. Owner-supplied at create time;
/// validated to be self-consistent (no zero-bed listings, etc).
/// </summary>
public sealed class Capacity : ValueObject
{
    public int MaxGuests { get; }
    public int Bedrooms { get; }
    public int Bathrooms { get; }
    public int Beds { get; }

    public Capacity(int maxGuests, int bedrooms, int bathrooms, int beds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxGuests, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(bedrooms);
        ArgumentOutOfRangeException.ThrowIfNegative(bathrooms);
        ArgumentOutOfRangeException.ThrowIfLessThan(beds, 1);
        MaxGuests = maxGuests;
        Bedrooms = bedrooms;
        Bathrooms = bathrooms;
        Beds = beds;
    }

    private Capacity() { } // EF

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MaxGuests;
        yield return Bedrooms;
        yield return Bathrooms;
        yield return Beds;
    }
}
