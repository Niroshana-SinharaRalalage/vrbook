using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Infrastructure.Persistence;
using VrBook.Modules.Booking.Infrastructure.Persistence;
using VrBook.Modules.Catalog.Infrastructure.Persistence;
using VrBook.Modules.Reviews.Application.Common;
using VrBook.Modules.Reviews.Domain;
using VrBook.Modules.Reviews.Infrastructure.Persistence;

namespace VrBook.Modules.Reviews.Application.Commands;

internal sealed class SubmitReviewHandler(
    ICurrentUser currentUser,
    IGuestTenantResolver guestTenant,
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

        // Slice OPS.M.9.1 F6c — closes audit #6. The handler is called from
        // the guest persona which has no ICurrentUser.TenantId; M.9 RLS
        // would deny the booking lookup AND the catalog tenant probe AND
        // the reviews INSERT. Resolve tenant from BookingId first (the
        // resolver opens its own scoped bypass for the lookup) and then
        // run the rest of the handler under a single BackgroundTenantScope.
        // Both Reviews + Catalog DbContexts read app.tenant_id via the
        // shared AsyncLocal, so the cross-context UPDATE inherits the
        // same scope without re-resolving.
        //
        // Removes the M.3-era '...0001' fallback Guid that the audit
        // (#6 footgun) flagged.
        var tenantId = await guestTenant.ResolveFromBookingIdAsync(cmd.BookingId, cancellationToken)
            ?? throw new NotFoundException("Booking", cmd.BookingId);
        using var tenantScope = BackgroundTenantScope.Enter(tenantId);

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
