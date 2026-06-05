using MediatR;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Catalog.Application.Amenities.Queries;

public sealed record ListAmenitiesQuery : IRequest<IReadOnlyList<AmenityDto>>;
