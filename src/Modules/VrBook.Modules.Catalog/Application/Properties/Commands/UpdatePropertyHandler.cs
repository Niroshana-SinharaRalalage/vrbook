using MediatR;
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
    IUnitOfWork uow,
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

        var r = request.Request;
        var address = new Address(
            r.Address.Street, r.Address.City, r.Address.State, r.Address.PostalCode,
            r.Address.CountryCode, r.Address.Latitude, r.Address.Longitude);
        var capacity = new Capacity(r.MaxGuests, r.Bedrooms, r.Bathrooms, r.Beds);
        var checkIn = new CheckInWindow(r.CheckinFrom, r.CheckinTo, r.CheckoutBy);

        p.UpdateBasics(
            r.Title, r.Description, address, capacity, checkIn,
            r.ReviewsEnabled, r.DynamicPricingEnabled, r.MessagingEnabled);
        p.ReplaceHouseRules(r.HouseRules);

        var validAmenities = await amenities.GetByIdsAsync(r.AmenityIds, cancellationToken);
        p.ReplaceAmenities(validAmenities.Select(a => a.Id));

        if (r.IsActive && !p.IsActive)
        {
            p.Activate();
        }
        else if (!r.IsActive && p.IsActive)
        {
            p.Deactivate("Deactivated by owner.");
        }

        await uow.SaveChangesAsync(cancellationToken);

        var amenityDtos = validAmenities.Select(a => a.ToDto()).ToArray();
        return p.ToDto(amenityDtos, urls.ToUrl);
    }
}
