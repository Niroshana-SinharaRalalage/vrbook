namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Cross-module read used by Booking (and later Messaging/Notifications) to find
/// out which user owns a given property and its title — without joining
/// catalog.properties directly. Implementation lives in the Catalog module.
/// </summary>
public interface IPropertyOwnerLookup
{
    Task<PropertyOwnerSnapshot?> GetAsync(Guid propertyId, CancellationToken ct = default);

    /// <summary>
    /// Slice 2 — owner-scoped reads (e.g. "the bookings on properties I own").
    /// Returns the property ids owned by <paramref name="ownerUserId"/>; empty
    /// when the user owns none.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListPropertyIdsOwnedByAsync(Guid ownerUserId, CancellationToken ct = default);
}

public sealed record PropertyOwnerSnapshot(
    Guid PropertyId,
    Guid OwnerUserId,
    string Title,
    Guid? TenantId = null);
