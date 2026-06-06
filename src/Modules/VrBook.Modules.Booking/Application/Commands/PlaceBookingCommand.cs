using MediatR;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Booking.Application.Commands;

public sealed record PlaceBookingCommand(PlaceBookingRequest Request) : IRequest<BookingDto>;
