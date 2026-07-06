using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Catalog.Application.Common;
using VrBook.Modules.Catalog.Domain;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Modules.Catalog.Application.Properties.Queries;

/// <summary>
/// Slice 1 — admin list of the caller's properties. Unlike <see cref="SearchPropertiesQuery"/>
/// which only returns active listings to the public, this query returns the
/// owner's drafts AND published listings so the admin UI can show "isActive"
/// state.
///
/// <para>Visibility:</para>
/// <list type="bullet">
///   <item>Owner (IsAdmin = false): only properties they own.</item>
///   <item>Tenant Admin (IsAdmin = true): all properties **within their own
///   tenant** — RLS scopes the read by <c>app.tenant_id</c>. Slice
///   OPS.M.10.2 F9 (audit #24) updated this comment to match reality;
///   the previous "all properties across all owners" phrasing was
///   pre-M.9 and implied cross-tenant visibility that doesn't exist.</item>
///   <item>PlatformAdmin: a real cross-tenant bypass is not wired here.
///   PlatformAdmin lands on <c>TenantsPlatformController</c> + the
///   M.8 <c>is_platform_admin</c> GUC for the few endpoints that need
///   it. Not this endpoint.</item>
/// </list>
/// </summary>
public sealed record ListMyPropertiesQuery() : IRequest<IReadOnlyList<AdminPropertySummaryDto>>;

internal sealed class ListMyPropertiesHandler(
    ICurrentUser currentUser,
    CatalogDbContext db,
    IPropertyImageUrlBuilder urls) : IRequestHandler<ListMyPropertiesQuery, IReadOnlyList<AdminPropertySummaryDto>>
{
    public async Task<IReadOnlyList<AdminPropertySummaryDto>> Handle(
        ListMyPropertiesQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required.");
        }

        IQueryable<Property> q = db.Properties
            .AsNoTracking()
            .Include(p => p.Images);

        // Slice OPS.M.15.5 — tenant-scoped role check. tenant_admin sees
        // every property in the tenant (RLS scopes the query); every other
        // authenticated caller sees only what they own.
        var isTenantAdmin = currentUser.TenantId is { } callerTid
            && currentUser.HasTenantRole(callerTid, "tenant_admin");
        if (!isTenantAdmin)
        {
            q = q.Where(p => p.OwnerUserId == currentUser.UserId.Value);
        }

        var rows = await q.OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);
        return rows.Select(p => new AdminPropertySummaryDto(
            p.Id,
            p.Slug,
            p.Title,
            p.Type,
            p.Address.City,
            p.Address.Country,
            p.Capacity.MaxGuests,
            p.Capacity.Bedrooms,
            p.IsActive,
            p.OwnerUserId,
            p.CreatedAt,
            p.Images.OrderBy(i => i.SortOrder).Select(i => urls.ToUrl(i.BlobPath)).FirstOrDefault()))
            .ToArray();
    }
}
