using VrBook.Domain.Common;

namespace VrBook.Modules.Catalog.Domain;

/// <summary>
/// Postal address + geo coordinates for a property. Immutable value object;
/// re-create when any field changes.
/// </summary>
public sealed class Address : ValueObject
{
    public string Street { get; }
    public string City { get; }
    public string State { get; }
    public string PostalCode { get; }
    public string Country { get; }
    public decimal Latitude { get; }
    public decimal Longitude { get; }

    public Address(
        string street,
        string city,
        string state,
        string postalCode,
        string country,
        decimal latitude,
        decimal longitude)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(street);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(country);
        if (latitude < -90m || latitude > 90m)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude));
        }
        if (longitude < -180m || longitude > 180m)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude));
        }
        Street = street.Trim();
        City = city.Trim();
        State = state?.Trim() ?? string.Empty;
        PostalCode = postalCode?.Trim() ?? string.Empty;
        Country = country.Trim();
        Latitude = latitude;
        Longitude = longitude;
    }

    private Address() { Street = City = State = PostalCode = Country = string.Empty; } // EF

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return State;
        yield return PostalCode;
        yield return Country;
        yield return Latitude;
        yield return Longitude;
    }
}
