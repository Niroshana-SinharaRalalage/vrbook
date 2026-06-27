using VrBook.Domain.Common;

namespace VrBook.Modules.Booking.Domain;

/// <summary>
/// Postgres-side mirror of a Redis booking hold (§7.3). Redis is authoritative
/// for liveness (TTL-expiring SET NX); this row exists so a restart can
/// reconcile stale state and so the audit trail captures every hold that ever
/// existed even after Redis evicts the key. Consumed (or released) holds
/// transition to <see cref="HoldStatus.Consumed"/> or
/// <see cref="HoldStatus.Released"/>; expiry without consumption is detected
/// by a cleanup pass and marked <see cref="HoldStatus.Expired"/>.
/// </summary>
public sealed class BookingHold : AggregateRoot
{
    /// <summary>Tenant from the property. OPS.M.3c flipped to non-nullable (originally
    /// the Wave C Booking commit b4bfc34 claimed this flip but the Write tool silently
    /// no-op'd — caught and fixed retroactively in Slice OPS.M.4 Step 1).</summary>
    public Guid TenantId { get; private set; }

    public Guid PropertyId { get; private set; }
    public DateOnly Checkin { get; private set; }
    public DateOnly Checkout { get; private set; }
    public int Guests { get; private set; }
    public Guid? SessionId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public HoldStatus Status { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public DateTimeOffset? ReleasedAt { get; private set; }

    private BookingHold() { } // EF

    public static BookingHold Create(
        Guid tenantId,
        Guid id,
        Guid propertyId,
        DateOnly checkin,
        DateOnly checkout,
        int guests,
        Guid? sessionId,
        DateTimeOffset expiresAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId required.", nameof(tenantId));
        }
        if (checkout <= checkin)
        {
            throw new BusinessRuleViolationException(
                "booking.hold.date_range",
                "Checkout must be after checkin.");
        }
        return new BookingHold
        {
            Id = id,
            TenantId = tenantId,
            PropertyId = propertyId,
            Checkin = checkin,
            Checkout = checkout,
            Guests = guests,
            SessionId = sessionId,
            ExpiresAt = expiresAt,
            Status = HoldStatus.Active,
        };
    }

    public void MarkConsumed(DateTimeOffset at)
    {
        Status = HoldStatus.Consumed;
        ConsumedAt = at;
    }

    public void MarkReleased(DateTimeOffset at)
    {
        Status = HoldStatus.Released;
        ReleasedAt = at;
    }

    public void MarkExpired()
    {
        if (Status == HoldStatus.Active)
        {
            Status = HoldStatus.Expired;
        }
    }
}

public enum HoldStatus
{
    Active = 0,
    Consumed = 1,
    Released = 2,
    Expired = 3,
}
