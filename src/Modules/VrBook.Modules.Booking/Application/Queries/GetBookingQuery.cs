using MediatR;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Booking.Application.Queries;

public sealed record GetBookingQuery(Guid Id) : IRequest<BookingDto>;
