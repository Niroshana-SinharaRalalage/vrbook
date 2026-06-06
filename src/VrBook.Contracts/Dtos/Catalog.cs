using VrBook.Contracts.Common;
using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Dtos;

/// <summary>Full property detail returned by GET /properties/{slug} and admin endpoints.</summary>
public sealed record PropertyDto(
    Guid Id,
    string Slug,
    string Title,
    string Description,
    PropertyType Type,
    Address Address,
    int MaxGuests,
    int Bedrooms,
    int Bathrooms,
    int Beds,
    TimeOnly CheckinFrom,
    TimeOnly CheckinTo,
    TimeOnly CheckoutBy,
    bool IsActive,
    bool ReviewsEnabled,
    bool DynamicPricingEnabled,
    bool MessagingEnabled,
    decimal? AverageRating,
    int RatingCount,
    IReadOnlyList<PropertyImageDto> Images,
    IReadOnlyList<AmenityDto> Amenities,
    IReadOnlyList<string> HouseRules);

/// <summary>Compact projection used in search results and dashboards.</summary>
public sealed record PropertySummaryDto(
    Guid Id,
    string Slug,
    string Title,
    PropertyType Type,
    string City,
    string Country,
    int MaxGuests,
    int Bedrooms,
    decimal? FromNightlyRate,
    string Currency,
    decimal? AverageRating,
    int RatingCount,
    string? PrimaryImageUrl);

public sealed record PropertyImageDto(
    Guid Id,
    string Url,
    string? Caption,
    int SortOrder,
    bool IsPrimary);

public sealed record AmenityDto(
    Guid Id,
    string Code,
    string Name,
    string? Icon,
    string Category);

public sealed record AvailabilityDayDto(
    DateOnly Date,
    bool Available,
    string? BlockReason);

/// <summary>One contiguous range that is NOT available to be booked. Half-open [start, end).</summary>
public sealed record BlockedRangeDto(DateOnly Start, DateOnly End);

public sealed record AvailabilityDto(
    Guid PropertyId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<BlockedRangeDto> Blocked);

public sealed record CreatePropertyRequest(
    string Title,
    string Description,
    PropertyType Type,
    Address Address,
    int MaxGuests,
    int Bedrooms,
    int Bathrooms,
    int Beds,
    TimeOnly CheckinFrom,
    TimeOnly CheckinTo,
    TimeOnly CheckoutBy,
    IReadOnlyList<string> HouseRules,
    IReadOnlyList<Guid> AmenityIds);

public sealed record UpdatePropertyRequest(
    string Title,
    string Description,
    Address Address,
    int MaxGuests,
    int Bedrooms,
    int Bathrooms,
    int Beds,
    TimeOnly CheckinFrom,
    TimeOnly CheckinTo,
    TimeOnly CheckoutBy,
    IReadOnlyList<string> HouseRules,
    IReadOnlyList<Guid> AmenityIds,
    bool ReviewsEnabled,
    bool DynamicPricingEnabled,
    bool MessagingEnabled,
    bool IsActive);

public sealed record ReorderImagesRequest(IReadOnlyList<Guid> OrderedImageIds);

public sealed record SearchPropertiesRequest(
    string? Destination,
    DateOnly? Checkin,
    DateOnly? Checkout,
    int? Guests,
    decimal? MinPrice,
    decimal? MaxPrice,
    IReadOnlyList<string>? AmenityCodes,
    decimal? MinRating,
    string? Sort,
    string? Cursor,
    int Limit = 20);
