using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Booking.Infrastructure.Persistence;

namespace VrBook.Modules.Booking.Application.Queries;

internal sealed class GetPropertyAvailabilityHandler(
    IBookingRepository bookings,
    IGuestTenantResolver guestTenant)
    : IRequestHandler<GetPropertyAvailabilityQuery, AvailabilityDto>
{
    public async Task<AvailabilityDto> Handle(GetPropertyAvailabilityQuery request, CancellationToken cancellationToken)
    {
        if (request.To <= request.From)
        {
            throw new BusinessRuleViolationException(
                "availability.range",
                "'to' must be after 'from'.");
        }
        var span = request.To.DayNumber - request.From.DayNumber;
        if (span > 366)
        {
            throw new BusinessRuleViolationException(
                "availability.range",
                "Range cannot exceed 366 days.");
        }

        // Slice OPS.M.9.1 F6d — closes audit #10. [AllowAnonymous] endpoint
        // reading booking.bookings + booking.availability_blocks; M.9 RLS
        // denied every read so the public availability calendar showed
        // everything as bookable even when occupied. Resolve tenant from
        // the property id (via catalog public-read carve-out), then open
        // a BackgroundTenantScope so the booking repo reads under the
        // property's tenant scope.
        var tenantId = await guestTenant.ResolveFromPropertyIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("Property", request.PropertyId);
        using var tenantScope = BackgroundTenantScope.Enter(tenantId);

        var ranges = await bookings.ListBlockedRangesAsync(request.PropertyId, request.From, request.To, cancellationToken);
        var blocks = ranges
            .Select(r => new BlockedRangeDto(
                Start: r.Checkin < request.From ? request.From : r.Checkin,
                End: r.Checkout > request.To ? request.To : r.Checkout))
            .OrderBy(b => b.Start)
            .ToArray();

        return new AvailabilityDto(request.PropertyId, request.From, request.To, blocks);
    }
}
