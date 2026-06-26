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
        // Slice 5: idempotent submit. If the guest already reviewed this stay
        // (e.g. clicked the review.request email link twice), return the
        // existing review with 200 instead of throwing. The unique index on
        // (BookingId) in ReviewConfiguration is the backstop against
        // concurrent races. See SLICE5_PLAN §2.4.
        var existing = await reviews.GetByBookingIdAsync(cmd.BookingId, cancellationToken);
        if (existing is not null)
        {
            return existing.ToDto();
        }

        // OPS.M.3 — review inherits tenancy from the booking's property
        // (guests are tenant-less; the review belongs to the property's tenant).
        // Use SqlQuery (parameterised FormattableString) to avoid the EF1002
        // injection warning. Once Catalog 3c flips to NOT NULL we can switch
        // to property.TenantId.Value via a normal load.
        var propertyId = booking.PropertyId;
        var tenantIdRaw = await catalogDb.Database
            .SqlQuery<Guid?>($"SELECT tenant_id AS \"Value\" FROM catalog.properties WHERE \"Id\" = {propertyId}")
            .FirstOrDefaultAsync(cancellationToken);
        var tenantId = tenantIdRaw ?? new Guid("00000000-0000-0000-0000-000000000001");

        var review = Review.Submit(
            tenantId: tenantId,
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
