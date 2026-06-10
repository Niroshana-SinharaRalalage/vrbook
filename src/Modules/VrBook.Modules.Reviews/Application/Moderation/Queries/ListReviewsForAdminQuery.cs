using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Modules.Reviews.Infrastructure.Persistence;

namespace VrBook.Modules.Reviews.Application.Moderation.Queries;

public sealed record ListReviewsForAdminQuery(ReviewStatus? Status) : IRequest<IReadOnlyList<ReviewDto>>;

internal sealed class ListReviewsForAdminHandler(ReviewsDbContext db)
    : IRequestHandler<ListReviewsForAdminQuery, IReadOnlyList<ReviewDto>>
{
    public async Task<IReadOnlyList<ReviewDto>> Handle(ListReviewsForAdminQuery request, CancellationToken cancellationToken)
    {
        var q = db.Reviews.AsNoTracking();
        if (request.Status.HasValue)
        {
            var s = request.Status.Value;
            q = q.Where(r => r.Status == s);
        }
        var rows = await q.OrderByDescending(r => r.PublishedAt ?? r.CreatedAt).ToListAsync(cancellationToken);
        return rows.Select(r => new ReviewDto(
            r.Id, r.BookingId, r.PropertyId, r.GuestUserId, r.GuestDisplayName,
            r.Rating, r.Body, r.Status, r.PublishedAt,
            r.ResponseBody is null ? null : new ReviewResponseDto(r.Id, r.ResponseBody, r.ResponseAt ?? r.CreatedAt),
            r.CreatedAt)).ToArray();
    }
}
