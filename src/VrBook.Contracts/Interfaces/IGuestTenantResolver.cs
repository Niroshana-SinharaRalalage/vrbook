namespace VrBook.Contracts.Interfaces;

/// <summary>
/// Slice OPS.M.9.1 — resolves the tenant id for an anonymous request from
/// the URL's resource (property id / slug / booking id / outbound token /
/// guest user id). Closes audit findings #4–#7, #10, #11 in
/// <c>docs/OPS_M_10_2_AUDIT_FINDINGS.md</c>.
///
/// <para><b>Why this exists</b>: OPS.M.9 shipped with a closed-world
/// <c>app.tenant_id</c> GUC. Anonymous <c>[AllowAnonymous]</c> endpoints
/// inherit empty GUC, so RLS denies every read. This resolver looks up the
/// tenant from the URL's resource (under an internal RLS bypass scope),
/// then the consumer handler opens a <c>BackgroundTenantScope</c> around
/// its own queries so the existing per-statement
/// <c>TenantGucCommandInterceptor</c> stamps the resolved tenant id.</para>
///
/// <para><b>Caller pattern</b>:</para>
/// <code>
/// var tenantId = await resolver.ResolveFromPropertyIdAsync(id, ct)
///     ?? throw new NotFoundException("Property", id);
/// using var scope = BackgroundTenantScope.Enter(tenantId);
/// // ... DbContext reads/writes scoped to that tenant ...
/// </code>
///
/// <para><b>Allow-list</b>: the impl
/// (<c>VrBook.Infrastructure.Guests.GuestTenantResolver</c>) is the ONLY
/// new entry in <c>RlsBypassCallSiteAllowlistTests.AllowedFullNames</c>.
/// Consumer handlers DO NOT inject <c>IRlsBypassDbContextFactory&lt;&gt;</c>;
/// they inject this interface.</para>
///
/// <para>For the public marketplace search (anonymous SELECT on
/// <c>catalog.properties</c> with no resource id), DO NOT use this
/// resolver — that path is closed by the <c>OPS.M.9.1</c> public-read RLS
/// policy carve-out (F6b) instead.</para>
/// </summary>
public interface IGuestTenantResolver
{
    /// <summary>
    /// Resolve from a property id. Used by booking availability + place,
    /// public quote, reviews list, submit review (review's property →
    /// tenant). Returns <c>null</c> if no such property exists.
    /// </summary>
    Task<Guid?> ResolveFromPropertyIdAsync(Guid propertyId, CancellationToken ct = default);

    /// <summary>
    /// Resolve from a property slug. Reserved for the detail-page non-
    /// carve-out reads (today's <c>GetPropertyBySlugHandler</c> is covered
    /// by the public-read carve-out so this is rarely hit).
    /// </summary>
    Task<Guid?> ResolveFromSlugAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Resolve from a booking id. Used by guest get-booking / cancel /
    /// submit-review. Returns <c>null</c> if no such booking exists.
    /// </summary>
    Task<Guid?> ResolveFromBookingIdAsync(Guid bookingId, CancellationToken ct = default);

    /// <summary>
    /// Resolve from an outbound iCal feed token (the token IS the
    /// credential). Used by <c>GetOutboundFeedHandler</c>.
    /// </summary>
    Task<Guid?> ResolveFromOutboundTokenAsync(string outboundToken, CancellationToken ct = default);

    /// <summary>
    /// Resolve the DISTINCT set of tenant ids the guest user has any
    /// bookings under. Used by <c>MyBookingsHandler</c>. Returns an empty
    /// list if the guest has no bookings. Per OPS.M.9.1 §1.4, the handler
    /// iterates per-tenant and merges the result.
    /// </summary>
    Task<IReadOnlyList<Guid>> ResolveTenantsForGuestUserAsync(
        Guid guestUserId, CancellationToken ct = default);
}
