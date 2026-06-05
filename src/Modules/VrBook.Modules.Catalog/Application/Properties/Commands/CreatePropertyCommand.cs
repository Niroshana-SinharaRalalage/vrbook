using MediatR;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Catalog.Application.Properties.Commands;

public sealed record CreatePropertyCommand(CreatePropertyRequest Request) : IRequest<PropertyDto>;
