using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos.Reports;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Reports.Application.Common;
using VrBook.Modules.Sync.Infrastructure.Persistence;

namespace VrBook.Modules.Reports.Application.Source.Queries;

public sealed record GetSourceReportQuery(
    DateOnly From,
    DateOnly To,
    Guid? PropertyId) : IRequest<SourceReportDto>, IReportRangeQuery;

public sealed class GetSourceReportQueryValidator : ReportRangeQueryValidator<GetSourceReportQuery>;

/// <summary>
/// One slice per source label. Direct slice comes from booking.bookings.Source
/// (currently always <see cref="BookingSource.Direct"/>); the four channel
/// slices come from sync.external_reservations.Channel. External rows filter
/// on <c>CancelledAt == null</c> directly, NOT <c>IsActive</c> — that's a
/// computed expression EF can't translate (see SLICE7_PLAN §2.3).
/// </summary>
internal sealed class GetSourceReportHandler(
    BookingDbContext booking,
    SyncDbContext sync,
    ICurrentUser currentUser,
    IPropertyOwnerLookup ownerLookup) : IRequestHandler<GetSourceReportQuery, SourceReportDto>
{
    public async Task<SourceReportDto> Handle(GetSourceReportQuery request, CancellationToken cancellationToken)
    {
        var scope = await ReportsAuthorization.ResolvePropertyScopeAsync(
            currentUser, ownerLookup, request.PropertyId, cancellationToken);
        var propertyIds = scope?.ToHashSet();
        var rangeStart = request.From;
        var rangeEndExclusive = request.To.AddDays(1);
        var activeBookingStatuses = new[]
        {
            BookingStatus.Confirmed,
            BookingStatus.CheckedIn,
            BookingStatus.CheckedOut,
            BookingStatus.Completed,
        };

        // Direct side - from Booking.
        var directQ = booking.Bookings.AsNoTracking()
            .Where(b => activeBookingStatuses.Contains(b.Status)
                && b.Stay.CheckinDate < rangeEndExclusive
                && b.Stay.CheckoutDate > rangeStart);
        if (propertyIds is not null)
        {
            directQ = directQ.Where(b => propertyIds.Contains(b.PropertyId));
        }
        var directRows = await directQ
            .Select(b => new { b.Stay.CheckinDate, b.Stay.CheckoutDate })
            .ToListAsync(cancellationToken);

        // Channel side - from Sync. Filter on CancelledAt == null directly.
        var extQ = sync.ExternalReservations.AsNoTracking()
            .Where(r => r.CancelledAt == null
                && r.Checkin < rangeEndExclusive
                && r.Checkout > rangeStart);
        if (propertyIds is not null)
        {
            extQ = extQ.Where(r => propertyIds.Contains(r.PropertyId));
        }
        var extRows = await extQ
            .Select(r => new { r.Channel, r.Checkin, r.Checkout })
            .ToListAsync(cancellationToken);

        var slices = new List<SourceSlice>();
        var directNights = CountInRangeNights(directRows.Select(r => (r.CheckinDate, r.CheckoutDate)), rangeStart, rangeEndExclusive);
        slices.Add(new SourceSlice("Direct", directRows.Count, directNights));

        foreach (var channel in new[] { ChannelKind.AirBnb, ChannelKind.Vrbo, ChannelKind.BookingCom, ChannelKind.Other })
        {
            var rowsForChannel = extRows.Where(r => r.Channel == channel).ToList();
            var nights = CountInRangeNights(
                rowsForChannel.Select(r => (r.Checkin, r.Checkout)),
                rangeStart,
                rangeEndExclusive);
            slices.Add(new SourceSlice(channel.ToString(), rowsForChannel.Count, nights));
        }

        return new SourceReportDto(
            slices,
            new SourceSummary(
                slices.Sum(s => s.Bookings),
                slices.Sum(s => s.Nights)));
    }

    private static int CountInRangeNights(
        IEnumerable<(DateOnly Checkin, DateOnly Checkout)> stays,
        DateOnly rangeStart,
        DateOnly rangeEndExclusive)
    {
        var total = 0;
        foreach (var (checkin, checkout) in stays)
        {
            var first = checkin < rangeStart ? rangeStart : checkin;
            var last = checkout > rangeEndExclusive ? rangeEndExclusive : checkout;
            if (last > first)
            {
                total += last.DayNumber - first.DayNumber;
            }
        }
        return total;
    }
}
