using MediatR;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Booking.Application.Commands;

public sealed record CancelBookingCommand(Guid Id, string Reason) : IRequest<BookingDto>;
public sealed record ConfirmBookingCommand(Guid Id) : IRequest<BookingDto>;
public sealed record RejectBookingCommand(Guid Id, string Reason) : IRequest<BookingDto>;
public sealed record CheckInBookingCommand(Guid Id) : IRequest<BookingDto>;
public sealed record CheckOutBookingCommand(Guid Id) : IRequest<BookingDto>;
