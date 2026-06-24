namespace VrBook.Contracts.Dtos.Reports;

public sealed record AdrReportDto(
    IReadOnlyList<AdrPoint> Series,
    AdrSummary Summary);

/// <summary>
/// One ADR point per day. <c>Adr</c> is <c>null</c> when there were no booked
/// nights on that day; rendering <c>0</c> would lie because no rate was charged.
/// Recharts is configured <c>connectNulls=false</c> so the line breaks at the gap.
/// </summary>
public sealed record AdrPoint(
    DateOnly Date,
    decimal? Adr,
    int BookedNights,
    decimal Revenue,
    string Currency);

public sealed record AdrSummary(
    decimal? AverageAdr,
    int TotalBookedNights,
    decimal TotalRevenue,
    string Currency);
