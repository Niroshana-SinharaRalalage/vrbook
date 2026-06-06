using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Domain.Common;
using VrBook.Modules.Booking.Infrastructure.Persistence;

namespace VrBook.Modules.Booking.Application.Queries;

internal sealed class GetPropertyAvailabilityHandler(IBookingRepository bookings)
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
