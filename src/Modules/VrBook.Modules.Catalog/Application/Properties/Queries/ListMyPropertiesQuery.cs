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
/// state. Admins (IsAdmin claim) see all properties across all owners.
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

        if (!currentUser.IsAdmin)
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
