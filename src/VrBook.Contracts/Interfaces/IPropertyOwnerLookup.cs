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

/// <summary>
/// OPS.M.4 Step 3c — <c>TenantId</c> is now non-nullable. OPS.M.3 §14 deviation 3
/// kept it <c>Guid?</c> as a forward-compat seam during Wave A/B/C; that
/// constraint dissolved once every <c>catalog.properties</c> row was backfilled
/// in Wave B and the column flipped <c>NOT NULL</c> in Wave C. Slice OPS.M.4
/// tightens the contract and deletes the <c>?? new Guid("…0001")</c> widening
/// sites at every consumer.
/// </summary>
public sealed record PropertyOwnerSnapshot(
    Guid PropertyId,
    Guid OwnerUserId,
    string Title,
    Guid TenantId,
    int TurnoverHours = 24); // Slice OPS.M.16 — property-default turnover window, read by Booking.CheckOut() to snapshot CompletionDueAt.
