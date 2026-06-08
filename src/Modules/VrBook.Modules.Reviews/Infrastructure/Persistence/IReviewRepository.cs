using Microsoft.EntityFrameworkCore;
using VrBook.Modules.Reviews.Domain;

namespace VrBook.Modules.Reviews.Infrastructure.Persistence;

public interface IReviewRepository
{
    Task<Review?> GetByBookingIdAsync(Guid bookingId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Review>> ListForPropertyAsync(Guid propertyId, int skip, int take, CancellationToken cancellationToken = default);
    Task<(decimal? Avg, int Count)> AggregateForPropertyAsync(Guid propertyId, CancellationToken cancellationToken = default);
    Task AddAsync(Review review, CancellationToken cancellationToken = default);
}

internal sealed class ReviewRepository(ReviewsDbContext db) : IReviewRepository
{
    public Task<Review?> GetByBookingIdAsync(Guid bookingId, CancellationToken cancellationToken = default) =>
        db.Reviews.FirstOrDefaultAsync(r => r.BookingId == bookingId, cancellationToken);

    public async Task<IReadOnlyList<Review>> ListForPropertyAsync(Guid propertyId, int skip, int take, CancellationToken cancellationToken = default) =>
        await db.Reviews.AsNoTracking()
            .Where(r => r.PropertyId == propertyId)
            .OrderByDescending(r => r.PublishedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<(decimal? Avg, int Count)> AggregateForPropertyAsync(Guid propertyId, CancellationToken cancellationToken = default)
    {
        var data = await db.Reviews.AsNoTracking()
            .Where(r => r.PropertyId == propertyId)
            .GroupBy(r => 1)
            .Select(g => new { Avg = (decimal?)g.Average(r => r.Rating), Count = g.Count() })
            .FirstOrDefaultAsync(cancellationToken);
        return data is null ? (null, 0) : (data.Avg, data.Count);
    }

    public Task AddAsync(Review review, CancellationToken cancellationToken = default)
    {
        db.Reviews.Add(review);
        return Task.CompletedTask;
    }
}
