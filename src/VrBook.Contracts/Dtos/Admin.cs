using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Dtos;

public sealed record FeatureToggleDto(
    string Key,
    string Scope,            // "global" | "property" | "user"
    Guid? ScopeId,
    bool Enabled);

public sealed record UpdateFeatureToggleRequest(
    string Scope,
    Guid? ScopeId,
    bool Enabled);

public sealed record OccupancyReportRow(
    Guid PropertyId,
    string PropertyTitle,
    DateOnly Date,
    decimal OccupancyPct,
    int BookedNights,
    int AvailableNights);

public sealed record RevenueReportRow(
    Guid PropertyId,
    string PropertyTitle,
    DateOnly Date,
    BookingSource Source,
    decimal Gross,
    decimal Net,
    int Nights,
    string Currency);

public sealed record AdrReportRow(
    Guid PropertyId,
    string PropertyTitle,
    DateOnly Period,
    BookingSource Source,
    decimal Adr,
    int Nights);

public sealed record AlertDto(
    Guid Id,
    string Severity,        // "Sev2" | "Sev3"
    string Category,        // "sync" | "payment" | "dispute" | …
    string Title,
    string? Description,
    string? ResolveUrl,
    DateTimeOffset RaisedAt,
    DateTimeOffset? DismissedAt);

public sealed record BookingQueueRowDto(
    Guid BookingId,
    string Reference,
    string PropertyTitle,
    string GuestDisplayName,
    DateOnly CheckinDate,
    DateOnly CheckoutDate,
    DateTimeOffset TentativeUntil,
    BookingStatus Status);
