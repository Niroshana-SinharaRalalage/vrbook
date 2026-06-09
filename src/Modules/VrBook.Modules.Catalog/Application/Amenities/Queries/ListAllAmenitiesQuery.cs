using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Catalog.Application.Common;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Modules.Catalog.Application.Amenities.Queries;

/// <summary>Admin-facing list including disabled rows.</summary>
public sealed record ListAllAmenitiesQuery : IRequest<IReadOnlyList<AmenityDto>>;

internal sealed class ListAllAmenitiesHandler(IAmenityRepository amenities)
    : IRequestHandler<ListAllAmenitiesQuery, IReadOnlyList<AmenityDto>>
{
    public async Task<IReadOnlyList<AmenityDto>> Handle(ListAllAmenitiesQuery request, CancellationToken cancellationToken)
    {
        var rows = await amenities.ListAllAsync(cancellationToken);
        return rows.Select(a => a.ToDto()).ToArray();
    }
}
