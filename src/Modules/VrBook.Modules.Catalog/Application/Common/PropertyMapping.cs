using VrBook.Contracts.Dtos;
using VrBook.Modules.Catalog.Domain;
using ContractsAddress = VrBook.Contracts.Common.Address;

namespace VrBook.Modules.Catalog.Application.Common;

internal static class PropertyMapping
{
    public static PropertyDto ToDto(this Property p, IReadOnlyList<AmenityDto> amenities, Func<string, string> blobToUrl) =>
        new(
            Id: p.Id,
            Slug: p.Slug,
            Title: p.Title,
            Description: p.Description,
            Type: p.Type,
            Address: new ContractsAddress(
                p.Address.Street,
                p.Address.City,
                p.Address.State,
                p.Address.PostalCode,
                p.Address.Country,
                p.Address.Latitude,
                p.Address.Longitude),
            MaxGuests: p.Capacity.MaxGuests,
            Bedrooms: p.Capacity.Bedrooms,
            Bathrooms: p.Capacity.Bathrooms,
            Beds: p.Capacity.Beds,
            CheckinFrom: p.CheckInWindow.CheckinFrom,
            CheckinTo: p.CheckInWindow.CheckinTo,
            CheckoutBy: p.CheckInWindow.CheckoutBy,
            IsActive: p.IsActive,
            ReviewsEnabled: p.ReviewsEnabled,
            DynamicPricingEnabled: p.DynamicPricingEnabled,
            MessagingEnabled: p.MessagingEnabled,
            AverageRating: p.RatingAvg,
            RatingCount: p.RatingCount,
            Images: p.Images
                .OrderBy(i => i.SortOrder)
                .Select(i => new PropertyImageDto(i.Id, blobToUrl(i.BlobPath), i.Caption, i.SortOrder, i.IsPrimary))
                .ToArray(),
            Amenities: amenities,
            HouseRules: p.HouseRules.OrderBy(h => h.SortOrder).Select(h => h.RuleText).ToArray());

    public static AmenityDto ToDto(this Amenity a) =>
        new(a.Id, a.Code, a.Name, a.Icon, a.Category);

    public static PropertySummaryDto ToSummary(this Property p, Func<string, string> blobToUrl)
    {
        var primary = p.Images.OrderBy(i => !i.IsPrimary).ThenBy(i => i.SortOrder).FirstOrDefault();
        return new PropertySummaryDto(
            Id: p.Id,
            Slug: p.Slug,
            Title: p.Title,
            Type: p.Type,
            City: p.Address.City,
            Country: p.Address.Country,
            MaxGuests: p.Capacity.MaxGuests,
            Bedrooms: p.Capacity.Bedrooms,
            FromNightlyRate: null,
            Currency: "USD",
            AverageRating: p.RatingAvg,
            RatingCount: p.RatingCount,
            PrimaryImageUrl: primary is null ? null : blobToUrl(primary.BlobPath));
    }
}
