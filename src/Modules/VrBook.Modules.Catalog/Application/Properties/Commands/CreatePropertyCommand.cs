using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Catalog.Application.Properties.Commands;

// OPS.M.4 — owner creates a property under their own tenant. Controller stamps
// TenantId from currentUser.TenantId; TenantAuthorizationBehavior gates.
public sealed record CreatePropertyCommand(CreatePropertyRequest Request, Guid TenantId)
    : IRequest<PropertyDto>, ITenantScoped;
