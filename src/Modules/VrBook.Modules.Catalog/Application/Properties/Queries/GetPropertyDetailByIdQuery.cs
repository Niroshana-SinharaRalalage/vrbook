using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Domain.Common;
using VrBook.Modules.Catalog.Application.Common;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Modules.Catalog.Application.Properties.Queries;

/// <summary>
/// Slice 1 — admin/owner full-detail lookup by id. The existing
/// <see cref="GetPropertyByIdQuery"/> returns a stripped <c>PropertyBasicInfo</c>
/// for cross-module reads; that shape is insufficient for the edit page which
/// needs Address, Capacity, HouseRules, Amenities, etc.
/// </summary>
public sealed record GetPropertyDetailByIdQuery(Guid Id) : IRequest<PropertyDto>;

internal sealed class GetPropertyDetailByIdHandler(
    IPropertyRepository properties,
    IAmenityRepository amenities,
    IPropertyImageUrlBuilder urls,
    CatalogDbContext db) : IRequestHandler<GetPropertyDetailByIdQuery, PropertyDto>
{
    public async Task<PropertyDto> Handle(GetPropertyDetailByIdQuery request, CancellationToken cancellationToken)
    {
        var p = await properties.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Property", request.Id);

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
