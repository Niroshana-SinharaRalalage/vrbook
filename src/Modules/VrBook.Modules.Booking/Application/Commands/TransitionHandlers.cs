using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Booking.Application.Common;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Catalog.Application.Properties.Queries;

namespace VrBook.Modules.Booking.Application.Commands;

internal sealed class CancelBookingHandler(
    ICurrentUser currentUser,
    IBookingRepository bookings,
    BookingDbContext db) : IRequestHandler<CancelBookingCommand, BookingDto>
{
    public async Task<BookingDto> Handle(CancelBookingCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }

        var booking = await bookings.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Booking", request.Id);
        if (booking.GuestUserId != currentUser.UserId.Value && !currentUser.IsAdmin)
        {
            throw new ForbiddenException("Only the guest who placed the booking can cancel it.");
        }
        booking.CancelByGuest(request.Reason);
        await db.SaveChangesAsync(cancellationToken);
        return booking.ToDto();
    }
}

internal abstract class OwnerActionHandler(
    ICurrentUser currentUser,
    IMediator mediator,
    IBookingRepository bookings,
    BookingDbContext db)
{
    protected async Task<BookingDto> TransitionAsync(Guid bookingId, Action<Domain.Booking> transition, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }

        var booking = await bookings.GetByIdAsync(bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking", bookingId);

        var property = await mediator.Send(new GetPropertyByIdQuery(booking.PropertyId), cancellationToken)
            ?? throw new NotFoundException("Property", booking.PropertyId);

        var isOwner = property.OwnerUserId == currentUser.UserId.Value;
        if (!isOwner && !currentUser.IsAdmin)
        {
            throw new ForbiddenException("Only the property owner can perform this action.");
        }
        transition(booking);
        await db.SaveChangesAsync(cancellationToken);
        return booking.ToDto();
    }
}

internal sealed class ConfirmBookingHandler(
    ICurrentUser currentUser, IMediator mediator, IBookingRepository bookings, BookingDbContext db)
    : OwnerActionHandler(currentUser, mediator, bookings, db), IRequestHandler<ConfirmBookingCommand, BookingDto>
{
    public Task<BookingDto> Handle(ConfirmBookingCommand request, CancellationToken cancellationToken) =>
        TransitionAsync(request.Id, b => b.Confirm(), cancellationToken);
}

internal sealed class RejectBookingHandler(
    ICurrentUser currentUser, IMediator mediator, IBookingRepository bookings, BookingDbContext db)
    : OwnerActionHandler(currentUser, mediator, bookings, db), IRequestHandler<RejectBookingCommand, BookingDto>
{
    public Task<BookingDto> Handle(RejectBookingCommand request, CancellationToken cancellationToken) =>
        TransitionAsync(request.Id, b => b.Reject(request.Reason), cancellationToken);
}

internal sealed class CheckInBookingHandler(
    ICurrentUser currentUser, IMediator mediator, IBookingRepository bookings, BookingDbContext db)
    : OwnerActionHandler(currentUser, mediator, bookings, db), IRequestHandler<CheckInBookingCommand, BookingDto>
{
    public Task<BookingDto> Handle(CheckInBookingCommand request, CancellationToken cancellationToken) =>
        TransitionAsync(request.Id, b => b.CheckIn(), cancellationToken);
}

internal sealed class CheckOutBookingHandler(
    ICurrentUser currentUser, IMediator mediator, IBookingRepository bookings, BookingDbContext db)
    : OwnerActionHandler(currentUser, mediator, bookings, db), IRequestHandler<CheckOutBookingCommand, BookingDto>
{
    public Task<BookingDto> Handle(CheckOutBookingCommand request, CancellationToken cancellationToken) =>
        TransitionAsync(request.Id, b => b.CheckOut(), cancellationToken);
}
