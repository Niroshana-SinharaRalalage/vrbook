using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Booking.Domain;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Catalog.Application.Properties.Queries;

namespace VrBook.Modules.Booking.Application.Queries;

/// <summary>
/// Slice 0.6 — single aggregator that the /admin/calendar screen calls. Merges
/// direct bookings (Booking schema) + external reservations from iCal feeds
/// (Sync schema via <see cref="IExternalChannelConflictChecker"/>) + active
/// checkout holds (Booking schema) into one DTO bounded by a date window.
///
/// Range semantics: half-open <c>[from, to)</c>. Anything that overlaps that
/// window is returned; the UI clips bars to the visible month.
/// </summary>
public sealed record GetPropertyCalendarQuery(
    Guid PropertyId,
    DateOnly From,
    DateOnly To) : IRequest<PropertyCalendarDto>;

internal sealed class GetPropertyCalendarHandler(
    ICurrentUser currentUser,
    IMediator mediator,
    BookingDbContext db,
    IExternalChannelConflictChecker externalChannel) : IRequestHandler<GetPropertyCalendarQuery, PropertyCalendarDto>
{
    public async Task<PropertyCalendarDto> Handle(GetPropertyCalendarQuery request, CancellationToken cancellationToken)
    {
        // OPS.M.10.2 C2 (#12 High) — add the same auth gate the sibling
        // ListAvailabilityBlocksHandler:23-34 carries. Previously this
        // handler had ZERO app-layer authorization check; it relied solely
        // on M.9 RLS to filter bookings/blocks. The IExternalChannelConflictChecker
        // call still runs and may leak conflicts for cross-tenant property
        // ids depending on its internal scoping.
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }

        var property = await mediator.Send(new GetPropertyByIdQuery(request.PropertyId), cancellationToken)
            ?? throw new NotFoundException("Property", request.PropertyId);

        if (property.OwnerUserId != currentUser.UserId.Value && !currentUser.IsAdmin)
        {
            throw new ForbiddenException("Only the property owner can view the calendar.");
        }

        // Direct bookings overlapping the window. Anything not Cancelled/Rejected
        // (matches the conflict-check rule).
        // Slice OPS.M.16 — AwaitingTurnover=true iff Status==CheckedOut so the
        // admin calendar can render a turnover-day overlay on Checkout without
        // the DTO having to lie about the booking's actual dates.
        var bookings = await db.Bookings
            .AsNoTracking()
            .Where(b => b.PropertyId == request.PropertyId)
            .Where(b => b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Rejected && b.Status != BookingStatus.Refunded)
            .Where(b => b.Stay.CheckinDate < request.To && request.From < b.Stay.CheckoutDate)
            .Select(b => new CalendarBookingEntry(
                b.Id,
                b.Reference,
                b.Stay.CheckinDate,
                b.Stay.CheckoutDate,
                b.Status,
                b.GuestDisplayName,
                b.Status == BookingStatus.CheckedOut))
            .ToListAsync(cancellationToken);

        // External (AirBnB / VRBO) reservations via the Sync module's checker.
        var externals = await externalChannel.FindOverlappingAsync(
            request.PropertyId, request.From, request.To, cancellationToken);
        var externalEntries = externals
            .Select(e => new CalendarExternalEntry(
                e.ExternalReservationId,
                e.Channel,
                e.Checkin,
                e.Checkout,
                e.Summary))
            .ToArray();

        // Active holds (Slice 0.1) that overlap the window. Released/Consumed/Expired
        // holds are not shown.
        var holds = await db.BookingHolds
            .AsNoTracking()
            .Where(h => h.PropertyId == request.PropertyId && h.Status == HoldStatus.Active)
            .Where(h => h.Checkin < request.To && request.From < h.Checkout)
            .Select(h => new CalendarHoldEntry(h.Id, h.Checkin, h.Checkout, h.ExpiresAt))
            .ToListAsync(cancellationToken);

        // Slice 3: owner-created manual blocks (maintenance, off-platform reservations).
        var blocks = await db.AvailabilityBlocks
            .AsNoTracking()
            .Where(x => x.PropertyId == request.PropertyId)
            .Where(x => x.StartDate < request.To && request.From < x.EndDate)
            .Select(x => new CalendarBlockEntry(x.Id, x.StartDate, x.EndDate, x.Reason))
            .ToListAsync(cancellationToken);

        return new PropertyCalendarDto(
            request.PropertyId,
            request.From,
            request.To,
            bookings,
            externalEntries,
            holds,
            blocks);
    }
}
