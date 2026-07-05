using MediatR;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Booking.Application.Common;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Catalog.Application.Properties.Queries;
using VrBook.Modules.Payment.Application.Commands;

namespace VrBook.Modules.Booking.Application.Commands;

internal sealed class CancelBookingHandler(
    ICurrentUser currentUser,
    IGuestTenantResolver guestTenant,
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

        // Slice OPS.M.9.1 F6d — closes audit #11 (Cancel sub-path). Guest
        // persona has no ICurrentUser.TenantId; without a scope, RLS denies
        // the booking lookup AND the CancelByGuest UPDATE. Resolve tenant
        // from BookingId, then run the rest of the handler under the
        // scope. The dispatched RefundForBookingCommand carries
        // booking.TenantId so the M.4 gate fires against the booking's
        // tenant (unchanged from F4).
        var tenantId = await guestTenant.ResolveFromBookingIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Booking", request.Id);
        using var tenantScope = BackgroundTenantScope.Enter(tenantId);

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
        // OPS.M.10.2 F11.7.5.1 — RefundForBookingCommand is ITenantScoped with
        // booking.TenantId. The guest has no ICurrentUser.TenantId; the M.4
        // gate's new BackgroundTenantScope fallback (added in F11.7.5.1)
        // consults the AsyncLocal scope opened above on line 36 with the
        // booking's tenant id. command.TenantId == scope.TenantId, so the
        // gate authorizes the refund. The C4 (#2 High) comment that previously
        // lived here mis-stated the gate's behavior — it compares against the
        // CALLER's tenant, not the command's — and the bug surfaced as a 403
        // on every guest cancel during the F11.7 walk.
        await mediator.Send(
            new RefundForBookingCommand(booking.Id, null, request.Reason, booking.TenantId),
            cancellationToken);
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
    protected IBookingRepository Bookings => bookings; // Slice OPS.M.16 — subclasses (CheckOutBookingHandler) need to pre-load the booking so they can dispatch cross-module lookups before the transition.

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
    ICurrentUser currentUser,
    IMediator mediator,
    IBookingRepository bookings,
    BookingDbContext db,
    ILogger<ConfirmBookingHandler> logger)
    : OwnerActionHandler(currentUser, mediator, bookings, db), IRequestHandler<ConfirmBookingCommand, BookingDto>
{
    public async Task<BookingDto> Handle(ConfirmBookingCommand request, CancellationToken cancellationToken)
    {
        var dto = await TransitionAsync(request.Id, b => b.Confirm(), cancellationToken);
        // Capture the held funds. No-op when Stripe is unconfigured.
        // Slice OPS.M.10.2 F11.7.5.7 — mirror PlaceBookingHandler's F11.7.2
        // pattern: the booking has already transitioned to Confirmed at this
        // point (TransitionAsync's SaveChanges committed at line 90). If the
        // PaymentIntent capture fails (Stripe transient, stub account not
        // recognized, no PI yet on stubbed staging) we MUST NOT bubble the
        // failure up — that would return 404/500 to the owner, who would
        // re-click Confirm, hit a Confirmed-state-machine guard with 422,
        // and conclude the action failed despite the booking being durably
        // Confirmed. Owner can retry capture from the /admin/bookings/{id}
        // detail page once Stripe state is healthy.
        try
        {
            await Mediator.Send(new CapturePaymentIntentForBookingCommand(request.Id), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "PaymentIntent capture failed for booking {BookingId} after Confirm transition; the booking is durably Confirmed. Owner can retry capture from /admin/bookings/<id>.",
                request.Id);
        }
        return dto;
    }
}

internal sealed class RejectBookingHandler(
    ICurrentUser currentUser,
    IMediator mediator,
    IBookingRepository bookings,
    BookingDbContext db,
    ILogger<RejectBookingHandler> logger)
    : OwnerActionHandler(currentUser, mediator, bookings, db), IRequestHandler<RejectBookingCommand, BookingDto>
{
    public async Task<BookingDto> Handle(RejectBookingCommand request, CancellationToken cancellationToken)
    {
        var dto = await TransitionAsync(request.Id, b => b.Reject(request.Reason), cancellationToken);
        // Release the auth-hold (or refund if already captured).
        // OPS.M.10.2 C4 (#2 High) — RejectBookingCommand is itself ITenantScoped
        // so request.TenantId is the caller's verified tenant id, same as the
        // booking's tenant id (M.4 already enforced equality).
        // OPS.M.10.2 F11.7.5.7 — same tolerance pattern as Confirm. The
        // booking is durably Rejected after TransitionAsync's SaveChanges;
        // a Stripe-side refund failure should NOT be surfaced as a 404/500
        // on /reject.
        try
        {
            await Mediator.Send(
                new RefundForBookingCommand(request.Id, null, request.Reason, request.TenantId),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Refund dispatch failed for booking {BookingId} after Reject transition; the booking is durably Rejected. Owner can retry refund from /admin/bookings/<id>.",
                request.Id);
        }
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
    ICurrentUser currentUser,
    IMediator mediator,
    IBookingRepository bookings,
    BookingDbContext db,
    IPropertyOwnerLookup propertyOwners)
    : OwnerActionHandler(currentUser, mediator, bookings, db), IRequestHandler<CheckOutBookingCommand, BookingDto>
{
    public async Task<BookingDto> Handle(CheckOutBookingCommand request, CancellationToken cancellationToken)
    {
        // Slice OPS.M.16 — CheckOut needs the property's default turnover
        // window to snapshot Booking.CompletionDueAt. Read the property via
        // the existing cross-module IPropertyOwnerLookup rather than
        // embedding the Catalog DbContext. The snapshot is stored on the
        // booking, so any subsequent property config change doesn't shift
        // in-flight completion times (§2.2 snapshot semantics).
        var booking = await Bookings.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Booking", request.Id);
        var property = await propertyOwners.GetAsync(booking.PropertyId, cancellationToken)
            ?? throw new NotFoundException("Property", booking.PropertyId);
        return await TransitionAsync(
            request.Id,
            b => b.CheckOut(property.TurnoverHours),
            cancellationToken);
    }
}

internal sealed class CompleteBookingHandler(
    ICurrentUser currentUser, IMediator mediator, IBookingRepository bookings, BookingDbContext db)
    : OwnerActionHandler(currentUser, mediator, bookings, db), IRequestHandler<CompleteBookingCommand, BookingDto>
{
    // Slice OPS.M.16 — manual completion by admin.
    public Task<BookingDto> Handle(CompleteBookingCommand request, CancellationToken cancellationToken) =>
        TransitionAsync(request.Id, b => b.CompleteManually(), cancellationToken);
}

internal sealed class ScheduleCompletionHandler(
    ICurrentUser currentUser, IMediator mediator, IBookingRepository bookings, BookingDbContext db)
    : OwnerActionHandler(currentUser, mediator, bookings, db), IRequestHandler<ScheduleCompletionCommand, BookingDto>
{
    // Slice OPS.M.16 — reschedule the auto-completion window on a
    // CheckedOut booking. Domain caps at 168h; validation echoed on the
    // API layer for the 400/422 payload distinction.
    public Task<BookingDto> Handle(ScheduleCompletionCommand request, CancellationToken cancellationToken) =>
        TransitionAsync(
            request.Id,
            b => b.ScheduleCompletion(request.HoursFromCheckedOutAt),
            cancellationToken);
}
