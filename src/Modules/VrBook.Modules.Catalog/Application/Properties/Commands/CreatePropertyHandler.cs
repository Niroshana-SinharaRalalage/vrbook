using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Catalog.Application.Common;
using VrBook.Modules.Catalog.Domain;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Modules.Catalog.Application.Properties.Commands;

internal sealed class CreatePropertyHandler(
    ICurrentUser currentUser,
    IPropertyRepository properties,
    IAmenityRepository amenities,
    IUnitOfWork uow,
    IPropertyImageUrlBuilder urls) : IRequestHandler<CreatePropertyCommand, PropertyDto>
{
    public async Task<PropertyDto> Handle(CreatePropertyCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required to create a property.");
        }

        var r = request.Request;
        var address = new Address(
            r.Address.Street, r.Address.City, r.Address.State, r.Address.PostalCode,
            r.Address.CountryCode, r.Address.Latitude, r.Address.Longitude);
        var capacity = new Capacity(r.MaxGuests, r.Bedrooms, r.Bathrooms, r.Beds);
        var checkIn = new CheckInWindow(r.CheckinFrom, r.CheckinTo, r.CheckoutBy);

        // Resolve a unique slug. Suffix with -2, -3, ... on collision.
        var baseSlug = Slug.FromTitle(r.Title);
        var slug = baseSlug;
        var suffix = 1;
        while (await properties.SlugExistsAsync(slug, cancellationToken))
        {
            suffix++;
            slug = $"{baseSlug}-{suffix}";
        }

        // Validate amenity ids exist before persisting the join.
        var validAmenities = await amenities.GetByIdsAsync(r.AmenityIds, cancellationToken);
        var validIds = validAmenities.Select(a => a.Id).ToArray();

        var p = Property.Create(
            ownerUserId: currentUser.UserId.Value,
            title: r.Title,
            description: r.Description,
            type: r.Type,
            address: address,
            capacity: capacity,
            checkIn: checkIn,
            houseRules: r.HouseRules,
            amenityIds: validIds,
            slug: slug);

        await properties.AddAsync(p, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        var amenityDtos = validAmenities.Select(a => a.ToDto()).ToArray();
        return p.ToDto(amenityDtos, urls.ToUrl);
    }
}
