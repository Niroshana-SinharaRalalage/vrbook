namespace VrBook.Contracts.Dtos.Reports;

public sealed record SourceReportDto(
    IReadOnlyList<SourceSlice> Slices,
    SourceSummary Summary);

/// <summary>
/// One slice per source label. Revenue is intentionally absent — see
/// <c>docs/SLICE7_PLAN.md</c> §2.3 (Source) for the cohort-consistency reasoning.
/// </summary>
public sealed record SourceSlice(
    string Source,
    int Bookings,
    int Nights);

public sealed record SourceSummary(
    int TotalBookings,
    int TotalNights);
