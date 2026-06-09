using VrBook.Contracts.Enums;
using VrBook.Contracts.Events;
using VrBook.Domain.Common;

namespace VrBook.Modules.Sync.Domain;

/// <summary>
/// Records an overlap between a direct booking and an external (AirBnB / VRBO)
/// reservation for the same property. Created by the sync worker when it detects
/// the overlap; resolved by the owner via the admin dashboard.
///
/// Proposal §8.3 resolution semantics:
///   <see cref="SyncConflictResolution.OwnerKeptDirect"/>  — owner keeps the direct booking; external entry is ignored
///   <see cref="SyncConflictResolution.OwnerCancelledDirect"/> — owner cancels the direct booking; refund per policy
///   <see cref="SyncConflictResolution.AutoCancelled"/>    — workflow auto-cancelled inside the tentative window
///   <see cref="SyncConflictResolution.ManualOverride"/>   — owner is negotiating off-platform; conflict parked
/// </summary>
public sealed class SyncConflict : AggregateRoot
{
    public Guid PropertyId { get; private set; }
    public Guid BookingId { get; private set; }
    public Guid ExternalReservationId { get; private set; }
    public ChannelKind Channel { get; private set; }
    public SyncConflictResolution Resolution { get; private set; }
    public string? ResolutionNotes { get; private set; }
    public DateTimeOffset DetectedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    public bool IsResolved => Resolution != SyncConflictResolution.Pending;

    private SyncConflict() { } // EF

    public static SyncConflict Detect(
        Guid propertyId,
        Guid bookingId,
        Guid externalReservationId,
        ChannelKind channel)
    {
        var c = new SyncConflict
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            BookingId = bookingId,
            ExternalReservationId = externalReservationId,
            Channel = channel,
            Resolution = SyncConflictResolution.Pending,
            DetectedAt = DateTimeOffset.UtcNow,
        };
        c.Raise(new SyncConflictDetected(c.Id, propertyId, bookingId, externalReservationId, channel));
        return c;
    }

    public void Resolve(SyncConflictResolution resolution, string notes)
    {
        if (resolution == SyncConflictResolution.Pending)
        {
            throw new BusinessRuleViolationException(
                "sync.conflict.resolution",
                "Cannot resolve a conflict to the Pending state.");
        }
        if (IsResolved)
        {
            throw new BusinessRuleViolationException(
                "sync.conflict.already_resolved",
                $"Conflict is already {Resolution} and cannot be resolved again.");
        }
        Resolution = resolution;
        ResolutionNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ResolvedAt = DateTimeOffset.UtcNow;
    }
}
