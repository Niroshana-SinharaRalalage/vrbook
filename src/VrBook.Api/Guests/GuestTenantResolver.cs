using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Catalog.Infrastructure.Persistence;
using VrBook.Modules.Sync.Infrastructure.Persistence;

namespace VrBook.Api.Guests;

/// <summary>
/// Slice OPS.M.9.1 F6a — default <see cref="IGuestTenantResolver"/> impl
/// (see <c>docs/OPS_M_9_1_GUEST_RESOLVER_PLAN.md</c> §2.2).
///
/// <para>This class is the ONE allowed bypass call site for anonymous-tenant
/// resolution; it's enumerated in
/// <c>RlsBypassCallSiteAllowlistTests.AllowedFullNames</c>. The bypass is
/// scoped per method (open → one query → dispose) so it cannot leak across
/// the caller's await chain.</para>
///
/// <para>Lifetime: <c>Scoped</c>. Matches the bypass factory lifetime
/// (OPS.M.9 §4.4). One resolver per request scope; multiple resolves per
/// request reuse the AsyncLocal infrastructure.</para>
/// </summary>
public sealed class GuestTenantResolver(
    IRlsBypassDbContextFactory<CatalogDbContext> catalogBypass,
    IRlsBypassDbContextFactory<BookingDbContext> bookingBypass,
    IRlsBypassDbContextFactory<SyncDbContext> syncBypass,
    ILogger<GuestTenantResolver> logger) : IGuestTenantResolver
{
    public async Task<Guid?> ResolveFromPropertyIdAsync(Guid propertyId, CancellationToken ct = default)
    {
        await using var bypass = await catalogBypass.CreateForBypassAsync(
            "guest-tenant-resolver.from-property-id", ct);
        var result = await bypass.Db.Properties
            .AsNoTracking()
            .Where(p => p.Id == propertyId)
            .Select(p => (Guid?)p.TenantId)
            .FirstOrDefaultAsync(ct);
        logger.LogDebug(
            "GuestTenantResolver.FromPropertyId({PropertyId}) → {TenantId}",
            propertyId, result);
        return result;
    }

    public async Task<Guid?> ResolveFromSlugAsync(string slug, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        await using var bypass = await catalogBypass.CreateForBypassAsync(
            "guest-tenant-resolver.from-slug", ct);
        var result = await bypass.Db.Properties
            .AsNoTracking()
            .Where(p => p.Slug == slug)
            .Select(p => (Guid?)p.TenantId)
            .FirstOrDefaultAsync(ct);
        logger.LogDebug(
            "GuestTenantResolver.FromSlug({Slug}) → {TenantId}", slug, result);
        return result;
    }

    public async Task<Guid?> ResolveFromBookingIdAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var bypass = await bookingBypass.CreateForBypassAsync(
            "guest-tenant-resolver.from-booking-id", ct);
        var result = await bypass.Db.Bookings
            .AsNoTracking()
            .Where(b => b.Id == bookingId)
            .Select(b => (Guid?)b.TenantId)
            .FirstOrDefaultAsync(ct);
        logger.LogDebug(
            "GuestTenantResolver.FromBookingId({BookingId}) → {TenantId}",
            bookingId, result);
        return result;
    }

    public async Task<Guid?> ResolveFromOutboundTokenAsync(string outboundToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboundToken);
        await using var bypass = await syncBypass.CreateForBypassAsync(
            "guest-tenant-resolver.from-outbound-token", ct);
        var result = await bypass.Db.ChannelFeeds
            .AsNoTracking()
            .Where(f => f.OutboundToken == outboundToken)
            .Select(f => (Guid?)f.TenantId)
            .FirstOrDefaultAsync(ct);
        // Deliberately omit the outbound token from structured logs (it's a
        // credential). Only log presence + result.
        logger.LogDebug(
            "GuestTenantResolver.FromOutboundToken(<len={Len}>) → {TenantId}",
            outboundToken.Length, result);
        return result;
    }

    public async Task<IReadOnlyList<Guid>> ResolveTenantsForGuestUserAsync(
        Guid guestUserId, CancellationToken ct = default)
    {
        await using var bypass = await bookingBypass.CreateForBypassAsync(
            "guest-tenant-resolver.tenants-for-guest", ct);
        var result = await bypass.Db.Bookings
            .AsNoTracking()
            .Where(b => b.GuestUserId == guestUserId)
            .Select(b => b.TenantId)
            .Distinct()
            .Take(20)
            .ToArrayAsync(ct);
        logger.LogDebug(
            "GuestTenantResolver.TenantsForGuest({GuestUserId}) → {Count} tenant(s)",
            guestUserId, result.Length);
        return result;
    }
}
