using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Catalog.Application.Common;
using VrBook.Modules.Catalog.Domain;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Modules.Catalog.Application.Properties.Commands;

internal sealed class UpdatePropertyHandler(
    ICurrentUser currentUser,
    IPropertyRepository properties,
    IAmenityRepository amenities,
    CatalogDbContext db,
    IPropertyImageUrlBuilder urls) : IRequestHandler<UpdatePropertyCommand, PropertyDto>
{
    public async Task<PropertyDto> Handle(UpdatePropertyCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }

        var p = await properties.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Property", request.Id);

        if (p.OwnerUserId != currentUser.UserId.Value && !currentUser.IsAdmin)
        {
            throw new ForbiddenException("You are not the owner of this property.");
        }

        var r = request.Request ?? throw new ArgumentException("Request body is required.", nameof(request));
        if (r.Address is null)
        {
            throw new ArgumentException("Address is required.", nameof(request));
        }

        var address = new Address(
            r.Address.Street, r.Address.City, r.Address.State, r.Address.PostalCode,
            r.Address.CountryCode, r.Address.Latitude, r.Address.Longitude);
        var capacity = new Capacity(r.MaxGuests, r.Bedrooms, r.Bathrooms, r.Beds);
        var checkIn = new CheckInWindow(r.CheckinFrom, r.CheckinTo, r.CheckoutBy);

        p.UpdateBasics(
            r.Title, r.Description, address, capacity, checkIn,
            r.ReviewsEnabled, r.DynamicPricingEnabled, r.MessagingEnabled);
        p.ReplaceHouseRules(r.HouseRules ?? Array.Empty<string>());

        var validAmenities = (r.AmenityIds is null || r.AmenityIds.Count == 0)
            ? Array.Empty<Amenity>()
            : (await amenities.GetByIdsAsync(r.AmenityIds, cancellationToken)).ToArray();
        p.ReplaceAmenities(validAmenities.Select(a => a.Id));

        if (r.IsActive && !p.IsActive)
        {
            p.Activate();
        }
        else if (!r.IsActive && p.IsActive)
        {
            p.Deactivate("Deactivated by owner.");
        }

        await db.SaveChangesAsync(cancellationToken);

        // Replace the amenity join rows: delete all existing, then insert the
        // newly-validated set. Cheaper + simpler than diffing for low cardinality.
        var existingJoin = await db.Set<Dictionary<string, object>>("property_amenities")
            .Where(j => (Guid)j["property_id"] == p.Id)
            .ToListAsync(cancellationToken);
        db.Set<Dictionary<string, object>>("property_amenities").RemoveRange(existingJoin);

        foreach (var aid in validAmenities.Select(a => a.Id))
        {
            db.Set<Dictionary<string, object>>("property_amenities").Add(new Dictionary<string, object>
            {
                ["property_id"] = p.Id,
                ["amenity_id"] = aid,
            });
        }
        await db.SaveChangesAsync(cancellationToken);

        var amenityDtos = validAmenities.Select(a => a.ToDto()).ToArray();
        return p.ToDto(amenityDtos, urls.ToUrl);
    }
}
