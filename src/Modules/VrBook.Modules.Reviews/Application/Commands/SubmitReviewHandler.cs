using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Catalog.Infrastructure.Persistence;
using VrBook.Modules.Reviews.Application.Common;
using VrBook.Modules.Reviews.Domain;
using VrBook.Modules.Reviews.Infrastructure.Persistence;

namespace VrBook.Modules.Reviews.Application.Commands;

internal sealed class SubmitReviewHandler(
    ICurrentUser currentUser,
    IReviewRepository reviews,
    IBookingRepository bookings,
    ReviewsDbContext reviewsDb,
    CatalogDbContext catalogDb) : IRequestHandler<SubmitReviewCommand, ReviewDto>
{
    public async Task<ReviewDto> Handle(SubmitReviewCommand cmd, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign-in required to leave a review.");
        }

        var booking = await bookings.GetByIdAsync(cmd.BookingId, cancellationToken)
            ?? throw new NotFoundException("Booking", cmd.BookingId);
        if (booking.GuestUserId != currentUser.UserId.Value)
        {
            throw new ForbiddenException("Only the guest who stayed at the property can leave a review.");
        }
        if (booking.Status is not (BookingStatus.CheckedOut or BookingStatus.Completed))
        {
            throw new BusinessRuleViolationException(
                "review.eligible",
                $"Reviews can only be left after the stay. Current status: {booking.Status}.");
        }
        var existing = await reviews.GetByBookingIdAsync(cmd.BookingId, cancellationToken);
        if (existing is not null)
        {
            throw new BusinessRuleViolationException("review.once", "You have already reviewed this stay.");
        }

        var review = Review.Submit(
            bookingId: cmd.BookingId,
            propertyId: booking.PropertyId,
            guestUserId: currentUser.UserId.Value,
            guestDisplayName: booking.GuestDisplayName,
            rating: cmd.Rating,
            body: cmd.Body);

        await reviews.AddAsync(review, cancellationToken);
        await reviewsDb.SaveChangesAsync(cancellationToken);

        // Recompute aggregate + push to Catalog. Cross-context update intentionally
        // separate transaction - rolling avg eventual-consistency is fine for v1.
        var (avg, count) = await reviews.AggregateForPropertyAsync(booking.PropertyId, cancellationToken);
        // PropertyConfiguration maps RatingAvg/RatingCount to snake_case columns,
        // while Id keeps EF's default Pascal-quoted form. Match the actual table schema.
        await catalogDb.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE catalog.properties SET rating_avg = {avg}, rating_count = {count} WHERE "Id" = {booking.PropertyId}""",
            cancellationToken);

        return review.ToDto();
    }
}
