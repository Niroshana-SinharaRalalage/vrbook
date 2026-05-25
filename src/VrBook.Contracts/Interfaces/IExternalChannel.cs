using VrBook.Contracts.Enums;

namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Inbound + outbound calendar channel abstraction (proposal §8.4). Phase 1 only has
/// <c>AirBnBICalChannel</c>; VRBO / Booking.com implementations are Phase 2.
/// </summary>
public interface IExternalChannel
{
    ChannelKind Kind { get; }

    Task<IReadOnlyList<ExternalReservationDto>> PullAsync(
        ChannelFeedConfig config,
        CancellationToken ct = default);

    Task<string> RenderOutboundFeedAsync(
        Guid propertyId,
        IReadOnlyList<OutboundReservation> reservations,
        CancellationToken ct = default);
}

public sealed record ChannelFeedConfig(
    Guid ChannelFeedId,
    Guid PropertyId,
    string InboundUrl,
    string? ETag,
    DateTimeOffset? LastModified);

public sealed record ExternalReservationDto(
    string ICalUid,
    DateOnly Checkin,
    DateOnly Checkout,
    string? Summary,
    string RawPayload);

public sealed record OutboundReservation(
    string Uid,
    DateOnly Checkin,
    DateOnly Checkout,
    string Summary,
    bool IsTentative,
    DateTimeOffset LastModified);

/// <summary>
/// Booking → Sync boundary. Booking asks "is there a conflict?" before transitioning
/// to <c>Confirmed</c>. Implementation lives in the Sync module.
/// </summary>
public interface IExternalChannelConflictChecker
{
    Task<bool> HasOverlapAsync(
        Guid propertyId,
        DateOnly checkin,
        DateOnly checkout,
        CancellationToken ct = default);
}
