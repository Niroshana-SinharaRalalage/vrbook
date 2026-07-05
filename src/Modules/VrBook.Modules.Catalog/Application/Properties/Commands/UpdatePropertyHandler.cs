using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Catalog.Application.Common;
using VrBook.Modules.Catalog.Domain;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Modules.Catalog.Application.Properties.Commands;

/// <summary>
/// Update handler that bypasses EF Core change tracking and uses ExecuteUpdate
/// / raw inserts. EF tracking of the Property aggregate (which holds three
/// owned value objects + a collection of HouseRules + an opaque amenity join)
/// triggered DbUpdateConcurrencyException on EF Core 8 + Npgsql for reasons we
/// couldn't pin down in the time budget. This handler is deliberately
/// procedural to keep PUT predictable until that's revisited.
/// </summary>
internal sealed class UpdatePropertyHandler(
    ICurrentUser currentUser,
    IAmenityRepository amenities,
    CatalogDbContext db,
    IPropertyImageUrlBuilder urls) : IRequestHandler<UpdatePropertyCommand, PropertyDto>
{
    public async Task<PropertyDto> Handle(UpdatePropertyCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }

        var r = request.Request ?? throw new ArgumentException("Request body is required.", nameof(request));
        if (r.Address is null)
        {
            throw new ArgumentException("Address is required.", nameof(request));
        }

        // OPS.M.4 Step 3 — owner-equality check deleted. TenantAuthorizationBehavior
        // rejects commands whose TenantId does not match currentUser.TenantId at the
        // pipeline; the controller stamps TenantId server-side so tenant-A users
        // cannot edit a tenant-B property. RBAC (Owner/Admin role) is on the
        // controller. Existence is still validated for the 404 contract.
        //
        // Slice OPS.M.10.2 F7 (audit #18) — defense-in-depth: also verify
        // the loaded property's TenantId matches the command's TenantId
        // BEFORE the raw-SQL UPDATE/DELETE on the child tables
        // (catalog.house_rules, catalog.property_amenities) runs. M.4's
        // tenant gate compares caller==command; this check compares
        // command==row, closing the (today RLS-protected) gap where a
        // future RLS regression on catalog.properties would let a
        // cross-tenant property's children get mutated. Throws as
        // NotFoundException to preserve the existing 404 contract.
        var existing = await db.Properties.AsNoTracking()
            .Where(p => p.Id == request.Id)
            .Select(p => new { p.Id, p.Slug, p.TenantId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Property", request.Id);
        if (existing.TenantId != request.TenantId)
        {
            throw new NotFoundException("Property", request.Id);
        }

        // Validate domain VOs by constructing them - throws if invariants violated.
        var address = new Address(
            r.Address.Street, r.Address.City, r.Address.State, r.Address.PostalCode,
            r.Address.CountryCode, r.Address.Latitude, r.Address.Longitude);
        var capacity = new Capacity(r.MaxGuests, r.Bedrooms, r.Bathrooms, r.Beds);
        var checkIn = new CheckInWindow(r.CheckinFrom, r.CheckinTo, r.CheckoutBy);

        // Validate amenity ids exist.
        var validAmenities = (r.AmenityIds is null || r.AmenityIds.Count == 0)
            ? Array.Empty<Amenity>()
            : (await amenities.GetByIdsAsync(r.AmenityIds, cancellationToken)).ToArray();

        var now = DateTimeOffset.UtcNow;

        // Update Property + owned-type columns via raw ExecuteUpdate. No
        // tracking, no concurrency check, no row_version drama.
        // Slice OPS.M.16 — validate turnover-hours range before the raw
        // UPDATE so the caller gets a 400 rather than the row silently
        // clamping to a bad value.
        if (r.TurnoverHours < 0 || r.TurnoverHours > 168)
        {
            throw new BusinessRuleViolationException(
                "property.turnover_hours_out_of_range",
                $"TurnoverHours must be between 0 and 168 (one week); got {r.TurnoverHours}.");
        }

        var updated = await db.Properties.Where(p => p.Id == request.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Title, r.Title.Trim())
                .SetProperty(p => p.Description, r.Description.Trim())
                .SetProperty(p => p.IsActive, r.IsActive)
                .SetProperty(p => p.ReviewsEnabled, r.ReviewsEnabled)
                .SetProperty(p => p.DynamicPricingEnabled, r.DynamicPricingEnabled)
                .SetProperty(p => p.MessagingEnabled, r.MessagingEnabled)
                .SetProperty(p => p.TurnoverHours, r.TurnoverHours)
                .SetProperty(p => p.UpdatedAt, now)
                .SetProperty(p => p.UpdatedBy, (Guid?)currentUser.UserId.Value),
                cancellationToken);
        if (updated == 0)
        {
            throw new NotFoundException("Property", request.Id);
        }

        // Owned VO columns can't go through ExecuteUpdate (EF doesn't translate
        // owned-type setters in ExecuteUpdate yet); use a raw UPDATE.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE catalog.properties
SET street = {address.Street},
    city = {address.City},
    state = {address.State},
    postal_code = {address.PostalCode},
    country = {address.Country},
    latitude = {address.Latitude},
    longitude = {address.Longitude},
    max_guests = {capacity.MaxGuests},
    bedrooms = {capacity.Bedrooms},
    bathrooms = {capacity.Bathrooms},
    beds = {capacity.Beds},
    checkin_from = {checkIn.CheckinFrom},
    checkin_to = {checkIn.CheckinTo},
    checkout_by = {checkIn.CheckoutBy}
WHERE ""Id"" = {request.Id}", cancellationToken);

        // Replace house rules (DELETE + INSERT) via raw SQL.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM catalog.house_rules WHERE property_id = {request.Id}", cancellationToken);
        var ruleIndex = 0;
        foreach (var rule in (r.HouseRules ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var rid = Guid.NewGuid();
            var trimmed = rule.Trim();
            var order = ruleIndex++;
            await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO catalog.house_rules (""Id"", property_id, rule_text, sort_order)
VALUES ({rid}, {request.Id}, {trimmed}, {order})", cancellationToken);
        }

        // Replace amenity join rows.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM catalog.property_amenities WHERE property_id = {request.Id}", cancellationToken);
        foreach (var a in validAmenities)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO catalog.property_amenities (property_id, amenity_id)
VALUES ({request.Id}, {a.Id})", cancellationToken);
        }

        // Re-read the updated property for the response (fresh, untracked).
        var fresh = await db.Properties.AsNoTracking()
            .Include(p => p.Images)
            .Include(p => p.HouseRules)
            .FirstAsync(p => p.Id == request.Id, cancellationToken);

        var amenityDtos = validAmenities.Select(a => a.ToDto()).ToArray();
        return fresh.ToDto(amenityDtos, urls.ToUrl);
    }
}
