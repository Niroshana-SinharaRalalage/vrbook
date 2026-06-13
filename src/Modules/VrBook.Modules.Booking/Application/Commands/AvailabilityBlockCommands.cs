using MediatR;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Booking.Application.Commands;

public sealed record CreateAvailabilityBlockCommand(
    Guid PropertyId,
    CreateAvailabilityBlockRequest Request) : IRequest<AvailabilityBlockDto>;

public sealed record DeleteAvailabilityBlockCommand(
    Guid PropertyId,
    Guid BlockId) : IRequest<Unit>;
