using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Catalog.Application.Common;
using VrBook.Modules.Catalog.Domain;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Modules.Catalog.Application.Properties.Queries;

internal sealed class SearchPropertiesHandler(
    CatalogDbContext db,
    IPropertyImageUrlBuilder urls) : IRequestHandler<SearchPropertiesQuery, PagedResult<PropertySummaryDto>>
{
    public async Task<PagedResult<PropertySummaryDto>> Handle(SearchPropertiesQuery request, CancellationToken cancellationToken)
    {
        var f = request.Filters;
        var limit = Math.Clamp(f.Limit, 1, 100);

        // Only active, non-deleted listings are publicly searchable.
        IQueryable<Property> q = db.Properties
            .Include(p => p.Images)
            .Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(f.Destination))
        {
            var like = $"%{f.Destination.Trim()}%";
            q = q.Where(p =>
                EF.Functions.ILike(p.Address.City, like) ||
                EF.Functions.ILike(p.Title, like) ||
                EF.Functions.ILike(p.Address.Country, like));
        }

        if (f.Guests is { } guests && guests > 0)
        {
            q = q.Where(p => p.Capacity.MaxGuests >= guests);
        }

        if (f.MinRating is { } minRating)
        {
            q = q.Where(p => p.RatingAvg != null && p.RatingAvg >= minRating);
        }

        // Filter by amenities (require ALL requested codes to be present).
        if (f.AmenityCodes is { Count: > 0 })
        {
            var codes = f.AmenityCodes.Select(c => c.Trim().ToLowerInvariant()).ToArray();
            var requiredIds = await db.Amenities.Where(a => codes.Contains(a.Code)).Select(a => a.Id).ToArrayAsync(cancellationToken);
            foreach (var aid in requiredIds)
            {
                var captured = aid;
                q = q.Where(p => db.Set<Dictionary<string, object>>("property_amenities")
                    .Any(j => (Guid)j["property_id"] == p.Id && (Guid)j["amenity_id"] == captured));
            }
        }

        // Sort + simple offset pagination via cursor=skip.
        q = (f.Sort ?? "newest").ToLowerInvariant() switch
        {
            "title" => q.OrderBy(p => p.Title),
            "rating" => q.OrderByDescending(p => p.RatingAvg ?? 0m).ThenByDescending(p => p.RatingCount),
            _ => q.OrderByDescending(p => p.CreatedAt),
        };

        var skip = 0;
        if (!string.IsNullOrWhiteSpace(f.Cursor) && int.TryParse(f.Cursor, out var parsedSkip) && parsedSkip > 0)
        {
            skip = parsedSkip;
        }

        var total = await q.CountAsync(cancellationToken);
        var page = await q.Skip(skip).Take(limit).ToListAsync(cancellationToken);
        var items = page.Select(p => p.ToSummary(urls.ToUrl)).ToArray();

        var nextCursor = (skip + page.Count) < total ? (skip + page.Count).ToString() : null;
        return new PagedResult<PropertySummaryDto>(items, nextCursor, total);
    }
}
