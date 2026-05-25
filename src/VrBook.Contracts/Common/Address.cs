namespace VrBook.Contracts.Common;

/// <summary>
/// Postal address with geocoded coordinates. ISO-3166 alpha-2 for <see cref="CountryCode"/>.
/// </summary>
public sealed record Address(
    string Street,
    string City,
    string State,
    string PostalCode,
    string CountryCode,
    decimal Latitude,
    decimal Longitude);
