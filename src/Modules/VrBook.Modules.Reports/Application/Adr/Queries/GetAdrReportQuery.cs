using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos.Reports;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Reports.Application.Common;

namespace VrBook.Modules.Reports.Application.Adr.Queries;

public sealed record GetAdrReportQuery(
    DateOnly From,
    DateOnly To,
    Guid? PropertyId) : IRequest<AdrReportDto>, IReportRangeQuery;

public sealed class GetAdrReportQueryValidator : ReportRangeQueryValidator<GetAdrReportQuery>;

/// <summary>
/// ADR = total stay revenue ÷ booked nights per day. The bookings that
/// contribute to the per-night denominator are the same as the Occupancy
/// numerator set (Confirmed+); revenue contribution is allocated per-night
/// (booking.Total ÷ stay nights), counted only on in-range nights.
/// Days with zero booked nights emit <c>Adr = null</c> so the chart breaks
/// the line (see SLICE7_PLAN §2.3).
/// </summary>
internal sealed class GetAdrReportHandler(
    BookingDbContext booking,
    ICurrentUser currentUser,
    IPropertyOwnerLookup ownerLookup) : IRequestHandler<GetAdrReportQuery, AdrReportDto>
{
    public async Task<AdrReportDto> Handle(GetAdrReportQuery request, CancellationToken cancellationToken)
    {
        var scope = await ReportsAuthorization.ResolvePropertyScopeAsync(
            currentUser, ownerLookup, request.PropertyId, cancellationToken);
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

        var q = booking.Bookings.AsNoTracking()
            .Where(b => activeStatuses.Contains(b.Status)
                && b.Stay.CheckinDate < rangeEndExclusive
                && b.Stay.CheckoutDate > rangeStart);
        if (propertyIds is not null)
        {
            q = q.Where(b => propertyIds.Contains(b.PropertyId));
        }
        var rows = await q
            .Select(b => new { b.Total, b.Currency, b.Stay.CheckinDate, b.Stay.CheckoutDate })
            .ToListAsync(cancellationToken);

        var currencies = rows.Select(r => r.Currency).Distinct().ToList();
        if (currencies.Count > 1)
        {
            throw new BusinessRuleViolationException(
                "reports.mixed_currency",
                $"Cannot aggregate ADR across currencies: {string.Join(", ", currencies)}.");
        }
        var currency = currencies.FirstOrDefault() ?? "USD";

        // Allocate stay revenue evenly per night, accumulate per in-range night.
        var revenueByDate = new Dictionary<DateOnly, decimal>();
        var nightsByDate = new Dictionary<DateOnly, int>();
        foreach (var r in rows)
        {
            var totalNights = r.CheckoutDate.DayNumber - r.CheckinDate.DayNumber;
            if (totalNights <= 0)
            {
                continue;
            }
            var perNight = r.Total / totalNights;
            var first = r.CheckinDate < rangeStart ? rangeStart : r.CheckinDate;
            var last = r.CheckoutDate > rangeEndExclusive ? rangeEndExclusive : r.CheckoutDate;
            for (var d = first; d < last; d = d.AddDays(1))
            {
                revenueByDate[d] = revenueByDate.GetValueOrDefault(d) + perNight;
                nightsByDate[d] = nightsByDate.GetValueOrDefault(d) + 1;
            }
        }

        var series = new List<AdrPoint>();
        decimal totalRev = 0m;
        var totalNightsAcc = 0;
        for (var d = rangeStart; d <= request.To; d = d.AddDays(1))
        {
            var rev = revenueByDate.GetValueOrDefault(d);
            var n = nightsByDate.GetValueOrDefault(d);
            decimal? adr = n == 0 ? null : Math.Round(rev / n, 2, MidpointRounding.AwayFromZero);
            series.Add(new AdrPoint(d, adr, n, Math.Round(rev, 2, MidpointRounding.AwayFromZero), currency));
            totalRev += rev;
            totalNightsAcc += n;
        }

        decimal? avgAdr = totalNightsAcc == 0
            ? null
            : Math.Round(totalRev / totalNightsAcc, 2, MidpointRounding.AwayFromZero);
        return new AdrReportDto(
            series,
            new AdrSummary(avgAdr, totalNightsAcc, Math.Round(totalRev, 2, MidpointRounding.AwayFromZero), currency));
    }
}
