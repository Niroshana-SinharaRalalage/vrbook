using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Catalog.Application.Common;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Modules.Catalog.Application.Amenities.Queries;

internal sealed class ListAmenitiesHandler(IAmenityRepository amenities) : IRequestHandler<ListAmenitiesQuery, IReadOnlyList<AmenityDto>>
{
    public async Task<IReadOnlyList<AmenityDto>> Handle(ListAmenitiesQuery request, CancellationToken cancellationToken)
    {
        var rows = await amenities.ListAsync(cancellationToken);
        return rows.Select(a => a.ToDto()).ToArray();
    }
}
