using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Dtos;

public sealed record ChannelFeedDto(
    Guid Id,
    Guid PropertyId,
    string PropertyTitle,
    ChannelKind Channel,
    string InboundUrl,
    string OutboundFeedUrl,
    int PollIntervalMinutes,
    bool IsEnabled,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastAttemptAt,
    string? LastError);

public sealed record CreateChannelFeedRequest(
    Guid PropertyId,
    ChannelKind Channel,
    string InboundUrl,
    int PollIntervalMinutes = 30);

public sealed record UpdateChannelFeedRequest(
    string InboundUrl,
    int PollIntervalMinutes,
    bool IsEnabled);

public sealed record SyncRunDto(
    Guid Id,
    Guid ChannelFeedId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    SyncRunStatus Status,
    int EventsSeen,
    int EventsNew,
    int EventsUpdated,
    int EventsCancelled,
    string? Error);

/// <summary>A detected overlap between an external reservation and a direct booking.</summary>
public sealed record SyncConflictDto(
    Guid Id,
    Guid PropertyId,
    string PropertyTitle,
    Guid ExternalReservationId,
    string ExternalSummary,
    DateOnly ExternalCheckin,
    DateOnly ExternalCheckout,
    Guid BookingId,
    string BookingReference,
    DateOnly BookingCheckin,
    DateOnly BookingCheckout,
    SyncConflictResolution Resolution,
    string? ResolutionNotes,
    DateTimeOffset DetectedAt,
    DateTimeOffset? ResolvedAt);

/// <summary>POST /admin/sync-conflicts/{id}/resolve</summary>
public sealed record ResolveConflictRequest(
    SyncConflictResolution Resolution,
    string Notes);
