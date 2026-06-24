namespace VrBook.Contracts.Dtos.Reports;

public sealed record RevenueReportDto(
    IReadOnlyList<RevenuePoint> Series,
    RevenueSummary Summary);

public sealed record RevenuePoint(
    DateOnly Date,
    decimal Revenue,
    string Currency);

public sealed record RevenueSummary(
    decimal TotalRevenue,
    string Currency,
    int ConfirmedBookings);
