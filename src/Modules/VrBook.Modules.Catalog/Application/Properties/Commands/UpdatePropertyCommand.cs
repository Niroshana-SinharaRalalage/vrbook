using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Catalog.Application.Properties.Commands;

// OPS.M.4 — owner edits one of their tenant's properties. Controller stamps
// TenantId from currentUser.TenantId; behavior rejects cross-tenant attempts.
public sealed record UpdatePropertyCommand(Guid Id, UpdatePropertyRequest Request, Guid TenantId)
    : IRequest<PropertyDto>, ITenantScoped;
