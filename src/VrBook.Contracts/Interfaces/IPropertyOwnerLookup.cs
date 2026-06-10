namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Cross-module read used by Booking (and later Messaging/Notifications) to find
/// out which user owns a given property and its title — without joining
/// catalog.properties directly. Implementation lives in the Catalog module.
/// </summary>
public interface IPropertyOwnerLookup
{
    Task<PropertyOwnerSnapshot?> GetAsync(Guid propertyId, CancellationToken ct = default);
}

public sealed record PropertyOwnerSnapshot(
    Guid PropertyId,
    Guid OwnerUserId,
    string Title);
