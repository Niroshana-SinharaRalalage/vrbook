using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos.Reports;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Reports.Application.Common;

namespace VrBook.Modules.Reports.Application.Occupancy.Queries;

public sealed record GetOccupancyReportQuery(
    DateOnly From,
    DateOnly To,
    Guid? PropertyId) : IRequest<OccupancyReportDto>, IReportRangeQuery;

public sealed class GetOccupancyReportQueryValidator : ReportRangeQueryValidator<GetOccupancyReportQuery>;

internal sealed class GetOccupancyReportHandler(
    BookingDbContext booking,
    ICurrentUser currentUser,
    IPropertyOwnerLookup ownerLookup) : IRequestHandler<GetOccupancyReportQuery, OccupancyReportDto>
{
    public async Task<OccupancyReportDto> Handle(GetOccupancyReportQuery request, CancellationToken cancellationToken)
    {
        var scope = await ReportsAuthorization.ResolvePropertyScopeAsync(
            currentUser, ownerLookup, request.PropertyId, cancellationToken);

        // Property-id set used both for the numerator filter and as the
        // denominator size. Admin viewing all = scope is null; collect via
        // a probe query against the booking set's distinct PropertyIds.
        var propertyIds = scope?.ToHashSet();
        var rangeStart = request.From;
        var rangeEndExclusive = request.To.AddDays(1);
        var activeStatuses = new[]
        {
            BookingStatus.Confirmed,
            BookingStatus.CheckedIn,
            BookingStatus.CheckedOut,
            BookingStatus.Completed,
        };

        // Pull bookings overlapping the range. Stay range is half-open
        // [CheckinDate, CheckoutDate); we want any booking whose stay covers
        // at least one day in [from, to+1).
        var bookingsQ = booking.Bookings.AsNoTracking()
            .Where(b => activeStatuses.Contains(b.Status)
                && b.Stay.CheckinDate < rangeEndExclusive
                && b.Stay.CheckoutDate > rangeStart);
        if (propertyIds is not null)
        {
            bookingsQ = bookingsQ.Where(b => propertyIds.Contains(b.PropertyId));
        }
        var bookings = await bookingsQ
            .Select(b => new { b.PropertyId, b.Stay.CheckinDate, b.Stay.CheckoutDate })
            .ToListAsync(cancellationToken);

        // Pull availability blocks overlapping the range.
        var blocksQ = booking.AvailabilityBlocks.AsNoTracking()
            .Where(bl => bl.StartDate < rangeEndExclusive && bl.EndDate > rangeStart);
        if (propertyIds is not null)
        {
            blocksQ = blocksQ.Where(bl => propertyIds.Contains(bl.PropertyId));
        }
        var blocks = await blocksQ
            .Select(bl => new { bl.PropertyId, bl.StartDate, bl.EndDate })
            .ToListAsync(cancellationToken);

        // If admin viewing all, the denominator size = distinct property ids
        // touched by either bookings or blocks in the range (best-effort proxy
        // for "properties on the platform during this window"). Acceptable for
        // Phase-1; OPS.M will replace with a tenant-scoped property query.
        var totalProperties = propertyIds?.Count
            ?? bookings.Select(b => b.PropertyId).Concat(blocks.Select(bl => bl.PropertyId)).Distinct().Count();

        // Fan bookings to in-range nights -> per-day booked count.
        var bookedNightsByDate = new Dictionary<DateOnly, int>();
        foreach (var b in bookings)
        {
            var first = b.CheckinDate < rangeStart ? rangeStart : b.CheckinDate;
            var last = b.CheckoutDate > rangeEndExclusive ? rangeEndExclusive : b.CheckoutDate;
            for (var d = first; d < last; d = d.AddDays(1))
            {
                bookedNightsByDate[d] = bookedNightsByDate.GetValueOrDefault(d) + 1;
            }
        }

        // Fan blocks to in-range nights -> per-day blocked-property count.
        var blockedByDate = new Dictionary<DateOnly, HashSet<Guid>>();
        foreach (var bl in blocks)
        {
            var first = bl.StartDate < rangeStart ? rangeStart : bl.StartDate;
            var last = bl.EndDate > rangeEndExclusive ? rangeEndExclusive : bl.EndDate;
            for (var d = first; d < last; d = d.AddDays(1))
            {
                if (!blockedByDate.TryGetValue(d, out var set))
                {
                    set = new HashSet<Guid>();
                    blockedByDate[d] = set;
                }
                set.Add(bl.PropertyId);
            }
        }

        // Build the series.
        var series = new List<OccupancyPoint>();
        var totalBooked = 0;
        var totalAvailable = 0;
        for (var d = rangeStart; d <= request.To; d = d.AddDays(1))
        {
            var booked = bookedNightsByDate.GetValueOrDefault(d);
            var blocked = blockedByDate.TryGetValue(d, out var set) ? set.Count : 0;
            var available = Math.Max(0, totalProperties - blocked);
            decimal? pct = available == 0
                ? null
                : Math.Round((decimal)booked / available * 100m, 2, MidpointRounding.AwayFromZero);
            series.Add(new OccupancyPoint(d, booked, available, pct));
            totalBooked += booked;
            totalAvailable += available;
        }

        decimal? avgPct = totalAvailable == 0
            ? null
            : Math.Round((decimal)totalBooked / totalAvailable * 100m, 2, MidpointRounding.AwayFromZero);
        return new OccupancyReportDto(series, new OccupancySummary(totalBooked, totalAvailable, avgPct));
    }
}
