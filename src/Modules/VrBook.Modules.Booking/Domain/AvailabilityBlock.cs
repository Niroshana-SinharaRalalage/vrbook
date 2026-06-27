using VrBook.Domain.Common;

namespace VrBook.Modules.Booking.Domain;

/// <summary>
/// Slice 3 — owner-created calendar block. Models "these dates are off the market
/// for reasons other than a booking" (maintenance, owner stay, AirBnB-side import
/// the owner manually mirrors, etc.). Range is half-open: <c>[StartDate, EndDate)</c>.
///
/// Lives in the booking schema because the placement guard in
/// <c>PlaceBookingHandler</c> needs to lock blocks inside the same serializable
/// transaction it uses for bookings; cross-schema FOR UPDATE complicates that.
///
/// <para>
/// <b>tenant_id</b> is nullable per REPLAN.md §10.1 forward-compat policy:
/// OPS.M.3 backfills it with the default tenant id and tightens to NOT NULL.
/// </para>
/// </summary>
public sealed class AvailabilityBlock : AggregateRoot
{
    public Guid PropertyId { get; private set; }

    /// <summary>Tenant inherited from the property. OPS.M.3c flipped to non-nullable.</summary>
    public Guid TenantId { get; private set; }

    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public string? Reason { get; private set; }

    private AvailabilityBlock() { }   // EF Core

    public static AvailabilityBlock Create(
        Guid tenantId,
        Guid propertyId,
        DateOnly startDate,
        DateOnly endDate,
        string? reason)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId required.", nameof(tenantId));
        }
        if (propertyId == Guid.Empty)
        {
            throw new ArgumentException("PropertyId required.", nameof(propertyId));
        }
        if (endDate <= startDate)
        {
            throw new ArgumentException("EndDate must be after StartDate.", nameof(endDate));
        }
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is { Length: > 200 })
        {
            throw new ArgumentException("Reason must be 200 characters or fewer.", nameof(reason));
        }

        return new AvailabilityBlock
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            TenantId = tenantId,
            StartDate = startDate,
            EndDate = endDate,
            Reason = trimmedReason,
        };
    }

    public bool Overlaps(DateOnly otherStart, DateOnly otherEnd) =>
        StartDate < otherEnd && otherStart < EndDate;
}
