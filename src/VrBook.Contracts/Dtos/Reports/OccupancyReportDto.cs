namespace VrBook.Contracts.Dtos.Reports;

public sealed record OccupancyReportDto(
    IReadOnlyList<OccupancyPoint> Series,
    OccupancySummary Summary);

public sealed record OccupancyPoint(
    DateOnly Date,
    int BookedNights,
    int AvailableNights,
    decimal? OccupancyPct);

public sealed record OccupancySummary(
    int TotalBookedNights,
    int TotalAvailableNights,
    decimal? AverageOccupancyPct);
