using MediatR;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Catalog.Application.Properties.Queries;

public sealed record GetPropertyBySlugQuery(string Slug) : IRequest<PropertyDto>;
