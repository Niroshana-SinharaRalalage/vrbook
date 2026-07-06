using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Reviews.Infrastructure.Persistence;

namespace VrBook.Modules.Reviews.Application.Moderation.Commands;

// Slice OPS.M.10.2 F9 (audit #23) — defense-in-depth post-load tenant
// equality across all four moderation handlers. M.4 already gated
// caller==cmd.TenantId via ITenantScoped; this check binds the loaded
// row to the command. Throws NotFoundException on mismatch to preserve
// the existing 404 contract and avoid leaking row existence.

internal sealed class RespondToReviewHandler(ReviewsDbContext db, ICurrentUser currentUser)
    : IRequestHandler<RespondToReviewCommand>
{
    public async Task Handle(RespondToReviewCommand cmd, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            throw new ForbiddenException("Sign in to respond to reviews.");
        }
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == cmd.ReviewId, cancellationToken)
            ?? throw new NotFoundException("Review", cmd.ReviewId);
        if (review.TenantId != cmd.TenantId)
        {
            throw new NotFoundException("Review", cmd.ReviewId);
        }
        // Owner verification belongs at the controller level via [Authorize(Roles="Owner,Admin")];
        // for tighter checks the controller can also verify property ownership.
        review.AddOwnerResponse(cmd.Body);
        await db.SaveChangesAsync(cancellationToken);
    }
}

// Slice OPS.M.17 (M.15 follow-up B) — moderation actions require
// tenant_admin membership. Pre-M.15.3 the controller carried
// [Authorize(Roles="Admin")]; that gate is gone, so each handler gates
// explicitly. RespondToReviewHandler above is EXCLUDED from this pattern
// — it's an owner-response endpoint (property-ownership check belongs
// there instead of tenant_admin); a separate follow-up covers that.
internal static class ReviewModerationAuthorization
{
    public static void RequireTenantAdmin(ICurrentUser currentUser, Guid tenantId)
    {
        if (!currentUser.HasTenantRole(tenantId, "tenant_admin"))
        {
            throw new ForbiddenException(
                "Review moderation requires tenant_admin role in the tenant.");
        }
    }
}

internal sealed class HideReviewHandler(ReviewsDbContext db, ICurrentUser currentUser) : IRequestHandler<HideReviewCommand>
{
    public async Task Handle(HideReviewCommand cmd, CancellationToken cancellationToken)
    {
        ReviewModerationAuthorization.RequireTenantAdmin(currentUser, cmd.TenantId);

        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == cmd.ReviewId, cancellationToken)
            ?? throw new NotFoundException("Review", cmd.ReviewId);
        if (review.TenantId != cmd.TenantId)
        {
            throw new NotFoundException("Review", cmd.ReviewId);
        }
        review.Hide();
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class RestoreReviewHandler(ReviewsDbContext db, ICurrentUser currentUser) : IRequestHandler<RestoreReviewCommand>
{
    public async Task Handle(RestoreReviewCommand cmd, CancellationToken cancellationToken)
    {
        ReviewModerationAuthorization.RequireTenantAdmin(currentUser, cmd.TenantId);

        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == cmd.ReviewId, cancellationToken)
            ?? throw new NotFoundException("Review", cmd.ReviewId);
        if (review.TenantId != cmd.TenantId)
        {
            throw new NotFoundException("Review", cmd.ReviewId);
        }
        review.Restore();
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class RejectReviewHandler(ReviewsDbContext db, ICurrentUser currentUser) : IRequestHandler<RejectReviewCommand>
{
    public async Task Handle(RejectReviewCommand cmd, CancellationToken cancellationToken)
    {
        ReviewModerationAuthorization.RequireTenantAdmin(currentUser, cmd.TenantId);

        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == cmd.ReviewId, cancellationToken)
            ?? throw new NotFoundException("Review", cmd.ReviewId);
        if (review.TenantId != cmd.TenantId)
        {
            throw new NotFoundException("Review", cmd.ReviewId);
        }
        review.Reject();
        await db.SaveChangesAsync(cancellationToken);
    }
}
