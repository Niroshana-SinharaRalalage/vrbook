using MediatR;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Catalog.Application.Properties.Queries;

public sealed record SearchPropertiesQuery(SearchPropertiesRequest Filters) : IRequest<PagedResult<PropertySummaryDto>>;
