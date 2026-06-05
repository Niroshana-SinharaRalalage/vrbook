using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Domain.Common;
using VrBook.Modules.Catalog.Application.Common;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Modules.Catalog.Application.Properties.Queries;

internal sealed class GetPropertyBySlugHandler(
    IPropertyRepository properties,
    IAmenityRepository amenities,
    IPropertyImageUrlBuilder urls,
    CatalogDbContext db) : IRequestHandler<GetPropertyBySlugQuery, PropertyDto>
{
    public async Task<PropertyDto> Handle(GetPropertyBySlugQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Slug))
        {
            throw new NotFoundException("Property", request.Slug);
        }

        var p = await properties.GetBySlugAsync(request.Slug, cancellationToken)
            ?? throw new NotFoundException("Property", request.Slug);

        var amenityIds = await db.Set<Dictionary<string, object>>("property_amenities")
            .Where(j => (Guid)j["property_id"] == p.Id)
            .Select(j => (Guid)j["amenity_id"])
            .ToArrayAsync(cancellationToken);

        var amenityDtos = (await amenities.GetByIdsAsync(amenityIds, cancellationToken))
            .Select(a => a.ToDto())
            .ToArray();

        return p.ToDto(amenityDtos, urls.ToUrl);
    }
}
