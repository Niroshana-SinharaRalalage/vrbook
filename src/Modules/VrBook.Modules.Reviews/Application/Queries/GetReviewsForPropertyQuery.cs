using MediatR;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Reviews.Application.Common;
using VrBook.Modules.Reviews.Infrastructure.Persistence;

namespace VrBook.Modules.Reviews.Application.Queries;

public sealed record GetReviewsForPropertyQuery(Guid PropertyId, string? Cursor, int Limit)
    : IRequest<PagedResult<ReviewDto>>;

internal sealed class GetReviewsForPropertyHandler(IReviewRepository reviews)
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
        var rows = await reviews.ListForPropertyAsync(request.PropertyId, skip, limit, cancellationToken);
        var items = rows.Select(r => r.ToDto()).ToArray();
        var next = items.Length == limit ? (skip + items.Length).ToString() : null;
        return new PagedResult<ReviewDto>(items, next, items.Length);
    }
}
