using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Booking.Application.Common;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Catalog.Application.Properties.Queries;
using VrBook.Modules.Payment.Application.Commands;

namespace VrBook.Modules.Booking.Application.Commands;

internal sealed class CancelBookingHandler(
    ICurrentUser currentUser,
    IMediator mediator,
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

        // Issue Stripe refund (cancels uncaptured PI; full refund if captured).
        // v1: full refund only. Cancellation-policy partial refunds land in A5.1.
        await mediator.Send(new RefundForBookingCommand(booking.Id, null, request.Reason), cancellationToken);
        return booking.ToDto();
    }
}

internal abstract class OwnerActionHandler(
    ICurrentUser currentUser,
    IMediator mediator,
    IBookingRepository bookings,
    BookingDbContext db)
{
    protected IMediator Mediator => mediator;

    protected async Task<BookingDto> TransitionAsync(Guid bookingId, Action<Domain.Booking> transition, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }

        var booking = await bookings.GetByIdAsync(bookingId, cancellationToken)
            ?? throw new NotFoundException("Booking", bookingId);

        // OPS.M.4 Step 3 — owner-equality check deleted. TenantAuthorizationBehavior
        // rejects mismatched-tenant commands at the pipeline; the controller's
        // [Authorize(Roles="Owner,Admin")] gates which roles can reach the endpoint.
        // The property lookup is dropped entirely — the booking already carries
        // PropertyId; no per-transition property fetch is needed once the owner
        // check is gone.
        transition(booking);
        await db.SaveChangesAsync(cancellationToken);
        return booking.ToDto();
    }
}

internal sealed class ConfirmBookingHandler(
    ICurrentUser currentUser, IMediator mediator, IBookingRepository bookings, BookingDbContext db)
    : OwnerActionHandler(currentUser, mediator, bookings, db), IRequestHandler<ConfirmBookingCommand, BookingDto>
{
    public async Task<BookingDto> Handle(ConfirmBookingCommand request, CancellationToken cancellationToken)
    {
        var dto = await TransitionAsync(request.Id, b => b.Confirm(), cancellationToken);
        // Capture the held funds. No-op when Stripe is unconfigured.
        await Mediator.Send(new CapturePaymentIntentForBookingCommand(request.Id), cancellationToken);
        return dto;
    }
}

internal sealed class RejectBookingHandler(
    ICurrentUser currentUser, IMediator mediator, IBookingRepository bookings, BookingDbContext db)
    : OwnerActionHandler(currentUser, mediator, bookings, db), IRequestHandler<RejectBookingCommand, BookingDto>
{
    public async Task<BookingDto> Handle(RejectBookingCommand request, CancellationToken cancellationToken)
    {
        var dto = await TransitionAsync(request.Id, b => b.Reject(request.Reason), cancellationToken);
        // Release the auth-hold (or refund if already captured).
        await Mediator.Send(new RefundForBookingCommand(request.Id, null, request.Reason), cancellationToken);
        return dto;
    }
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
