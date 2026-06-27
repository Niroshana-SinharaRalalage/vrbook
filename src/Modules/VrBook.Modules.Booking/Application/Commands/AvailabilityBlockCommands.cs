using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Booking.Application.Commands;

// OPS.M.4 — both block commands are owner-driven and gain ITenantScoped.
// Controller stamps TenantId from currentUser.TenantId.

public sealed record CreateAvailabilityBlockCommand(
    Guid PropertyId,
    CreateAvailabilityBlockRequest Request,
    Guid TenantId) : IRequest<AvailabilityBlockDto>, ITenantScoped;

public sealed record DeleteAvailabilityBlockCommand(
    Guid PropertyId,
    Guid BlockId,
    Guid TenantId) : IRequest<Unit>, ITenantScoped;
