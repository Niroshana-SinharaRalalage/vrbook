using MediatR;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Reviews.Application.Common;
using VrBook.Modules.Reviews.Infrastructure.Persistence;

namespace VrBook.Modules.Reviews.Application.Queries;

public sealed record GetReviewsForPropertyQuery(Guid PropertyId, string? Cursor, int Limit)
    : IRequest<PagedResult<ReviewDto>>;

internal sealed class GetReviewsForPropertyHandler(
    IReviewRepository reviews,
    IGuestTenantResolver guestTenant)
    : IRequestHandler<GetReviewsForPropertyQuery, PagedResult<ReviewDto>>
{
    public async Task<PagedResult<ReviewDto>> Handle(GetReviewsForPropertyQuery request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 100);
        var skip = 0;
        if (!string.IsNullOrWhiteSpace(request.Cursor) && int.TryParse(request.Cursor, out var s) && s > 0)
        {
            skip = s;
        }

        // Slice OPS.M.9.1 F6c — closes audit #5. [AllowAnonymous] read on
        // reviews.reviews (RLS-protected); resolve tenant from the property
        // id (via the catalog public-read carve-out) and open a
        // BackgroundTenantScope so the per-statement interceptor stamps
        // app.tenant_id for the reviews query.
        var tenantId = await guestTenant.ResolveFromPropertyIdAsync(request.PropertyId, cancellationToken)
            ?? throw new NotFoundException("Property", request.PropertyId);
        using var tenantScope = BackgroundTenantScope.Enter(tenantId);

        var rows = await reviews.ListForPropertyAsync(request.PropertyId, skip, limit, cancellationToken);
        var items = rows.Select(r => r.ToDto()).ToArray();
        var next = items.Length == limit ? (skip + items.Length).ToString() : null;
        return new PagedResult<ReviewDto>(items, next, items.Length);
    }
}
