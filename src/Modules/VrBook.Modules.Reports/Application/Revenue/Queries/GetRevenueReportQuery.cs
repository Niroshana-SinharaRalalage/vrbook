using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos.Reports;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Reports.Application.Common;

namespace VrBook.Modules.Reports.Application.Revenue.Queries;

public sealed record GetRevenueReportQuery(
    DateOnly From,
    DateOnly To,
    Guid? PropertyId) : IRequest<RevenueReportDto>, IReportRangeQuery;

public sealed class GetRevenueReportQueryValidator : ReportRangeQueryValidator<GetRevenueReportQuery>;

internal sealed class GetRevenueReportHandler(
    BookingDbContext booking,
    ICurrentUser currentUser,
    IPropertyOwnerLookup ownerLookup) : IRequestHandler<GetRevenueReportQuery, RevenueReportDto>
{
    public async Task<RevenueReportDto> Handle(GetRevenueReportQuery request, CancellationToken cancellationToken)
    {
        var scope = await ReportsAuthorization.ResolvePropertyScopeAsync(
            currentUser, ownerLookup, request.PropertyId, cancellationToken);
        var propertyIds = scope?.ToHashSet();
        var rangeStart = request.From;
        var rangeEnd = request.To;
        var excludedStatuses = new[]
        {
            BookingStatus.Cancelled,
            BookingStatus.Rejected,
            BookingStatus.Refunded,
            BookingStatus.Disputed,
        };

        // Bucket by ConfirmedAt::date - the moment money was committed.
        // Tentative rows have ConfirmedAt == null and so naturally drop out
        // of the predicate.
        var rangeStartDt = new DateTimeOffset(rangeStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var rangeEndDtExclusive = new DateTimeOffset(rangeEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var q = booking.Bookings.AsNoTracking()
            .Where(b => b.ConfirmedAt != null
                && b.ConfirmedAt >= rangeStartDt
                && b.ConfirmedAt < rangeEndDtExclusive
                && !excludedStatuses.Contains(b.Status));
        if (propertyIds is not null)
        {
            q = q.Where(b => propertyIds.Contains(b.PropertyId));
        }

        var rows = await q
            .Select(b => new { b.ConfirmedAt, b.Total, b.Currency })
            .ToListAsync(cancellationToken);

        // Single-currency assertion - REPLAN Phase-1 simplification.
        var currencies = rows.Select(r => r.Currency).Distinct().ToList();
        if (currencies.Count > 1)
        {
            throw new BusinessRuleViolationException(
                "reports.mixed_currency",
                $"Cannot aggregate revenue across currencies: {string.Join(", ", currencies)}.");
        }
        var currency = currencies.FirstOrDefault() ?? "USD";

        var revenueByDate = rows
            .GroupBy(r => DateOnly.FromDateTime(r.ConfirmedAt!.Value.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Total));

        var series = new List<RevenuePoint>();
        decimal total = 0m;
        for (var d = rangeStart; d <= rangeEnd; d = d.AddDays(1))
        {
            var rev = revenueByDate.GetValueOrDefault(d);
            series.Add(new RevenuePoint(d, rev, currency));
            total += rev;
        }

        return new RevenueReportDto(
            series,
            new RevenueSummary(total, currency, rows.Count));
    }
}
