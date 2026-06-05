using MediatR;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Catalog.Application.Properties.Commands;

public sealed record UpdatePropertyCommand(Guid Id, UpdatePropertyRequest Request) : IRequest<PropertyDto>;
