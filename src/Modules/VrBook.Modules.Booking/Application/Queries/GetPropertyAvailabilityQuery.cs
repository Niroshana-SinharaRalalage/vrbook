using MediatR;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Booking.Application.Queries;

public sealed record GetPropertyAvailabilityQuery(Guid PropertyId, DateOnly From, DateOnly To)
    : IRequest<AvailabilityDto>;
