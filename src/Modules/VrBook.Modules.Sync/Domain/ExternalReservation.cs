using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Domain.Common;

namespace VrBook.Modules.Sync.Domain;

/// <summary>
/// A reservation imported from an external channel (AirBnB, VRBO, Booking.com).
/// Unique by <c>(ChannelFeedId, ICalUid)</c>. Reservations that disappear from the
/// feed on a subsequent poll are marked cancelled rather than hard-deleted, so
/// historical conflict trails remain intact.
/// </summary>
public sealed class ExternalReservation : AggregateRoot
{
    /// <summary>Denorm from feed → property → tenant. Per OPS_M_3_PLAN §1.</summary>
    public Guid TenantId { get; private set; }

    public Guid ChannelFeedId { get; private set; }
    public Guid PropertyId { get; private set; }
    public ChannelKind Channel { get; private set; }
    public string ICalUid { get; private set; } = default!;
    public DateOnly Checkin { get; private set; }
    public DateOnly Checkout { get; private set; }
    public string? Summary { get; private set; }
    public string RawPayload { get; private set; } = default!;
    public DateTimeOffset ImportedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    /// <summary>True iff the reservation is still present in the source feed.</summary>
    public bool IsActive => CancelledAt is null;

    private ExternalReservation() { } // EF

    public static ExternalReservation Import(
        Guid tenantId,
        Guid channelFeedId,
        Guid propertyId,
        ChannelKind channel,
        string iCalUid,
        DateOnly checkin,
        DateOnly checkout,
        string? summary,
        string rawPayload)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId required.", nameof(tenantId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(iCalUid);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPayload);
        if (checkout <= checkin)
        {
            throw new BusinessRuleViolationException(
                "sync.reservation.date_range",
                "Checkout must be strictly after checkin.");
        }

        var er = new ExternalReservation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ChannelFeedId = channelFeedId,
            PropertyId = propertyId,
            Channel = channel,
            ICalUid = iCalUid.Trim(),
            Checkin = checkin,
            Checkout = checkout,
            Summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim(),
            RawPayload = rawPayload,
            ImportedAt = DateTimeOffset.UtcNow,
        };
        er.Raise(new ExternalReservationImported(
            er.Id, propertyId, channel, er.ICalUid, checkin, checkout));
        return er;
    }

    /// <summary>
    /// Owner re-pulled the feed and the same iCal UID now points to different dates
    /// or summary. Update in place; raise the imported-event so downstream consumers
    /// re-evaluate conflicts.
    /// </summary>
    public void Update(DateOnly checkin, DateOnly checkout, string? summary, string rawPayload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPayload);
        if (checkout <= checkin)
        {
            throw new BusinessRuleViolationException(
                "sync.reservation.date_range",
                "Checkout must be strictly after checkin.");
        }
        Checkin = checkin;
        Checkout = checkout;
        Summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        RawPayload = rawPayload;
        // Re-raise the import event with the new dates so conflict re-detection runs.
        Raise(new ExternalReservationImported(
            Id, PropertyId, Channel, ICalUid, checkin, checkout));
    }

    public void MarkCancelled()
    {
        if (CancelledAt is not null)
        {
            return; // idempotent — already cancelled
        }
        CancelledAt = DateTimeOffset.UtcNow;
        Raise(new ExternalReservationCancelled(Id, PropertyId, Channel, ICalUid));
    }

    /// <summary>
    /// Pure check: does this reservation overlap a given date range? Uses the
    /// half-open convention <c>[checkin, checkout)</c> consistent with the
    /// Stay value object in the Booking module.
    /// </summary>
    public bool OverlapsWith(DateOnly otherCheckin, DateOnly otherCheckout)
    {
        if (!IsActive)
        {
            return false;
        }
        return Checkin < otherCheckout && otherCheckin < Checkout;
    }
}
