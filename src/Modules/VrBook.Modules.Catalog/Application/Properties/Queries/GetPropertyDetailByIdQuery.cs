using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Catalog.Application.Common;
using VrBook.Modules.Catalog.Domain;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Modules.Catalog.Application.Properties.Queries;

/// <summary>
/// Slice 1 — admin/owner full-detail lookup by id. The existing
/// <see cref="GetPropertyByIdQuery"/> returns a stripped <c>PropertyBasicInfo</c>
/// for cross-module reads; that shape is insufficient for the edit page which
/// needs Address, Capacity, HouseRules, Amenities, etc.
/// </summary>
public sealed record GetPropertyDetailByIdQuery(Guid Id) : IRequest<PropertyDto>;

internal sealed class GetPropertyDetailByIdHandler(
    IPropertyRepository properties,
    IAmenityRepository amenities,
    IPropertyImageUrlBuilder urls,
    ITenantStripeReadinessLookup tenantReadiness,
    ILogger<GetPropertyDetailByIdHandler> logger,
    CatalogDbContext db) : IRequestHandler<GetPropertyDetailByIdQuery, PropertyDto>
{
    public async Task<PropertyDto> Handle(GetPropertyDetailByIdQuery request, CancellationToken cancellationToken)
    {
        var p = await properties.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Property", request.Id);

        var amenityIds = await db.Set<Dictionary<string, object>>("property_amenities")
            .Where(j => (Guid)j["property_id"] == p.Id)
            .Select(j => (Guid)j["amenity_id"])
            .ToArrayAsync(cancellationToken);

        var amenityDtos = (await amenities.GetByIdsAsync(amenityIds, cancellationToken))
            .Select(a => a.ToDto())
            .ToArray();

        var dto = p.ToDto(amenityDtos, urls.ToUrl);

        // VRB-212 — project the tenant's Stripe readiness so the settings UI can enable/
        // explain the publish toggle. This is best-effort UI enrichment: the binding gate is
        // enforced fail-closed in UpdatePropertyHandler, so a lookup failure here must NOT 500
        // the core detail read — degrade fail-safe to CanActivate=false ("readiness unknown").
        ActivationBlock? block;
        try
        {
            var readiness = await tenantReadiness.GetAsync(p.TenantId, cancellationToken);
            block = readiness is null
                ? new ActivationBlock("property.tenant_not_payment_ready", "Tenant payment readiness is unknown.")
                : Property.CheckActivation(readiness.Status, readiness.ChargesEnabled, readiness.PayoutsEnabled, p.Images.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Activation-readiness projection failed for property {PropertyId} (tenant {TenantId}); " +
                "degrading to CanActivate=false.", p.Id, p.TenantId);
            block = new ActivationBlock("property.tenant_not_payment_ready", "Tenant payment readiness is unknown.");
        }

        return dto with { CanActivate = block is null, ActivationBlockedReason = block?.Message };
    }
}
