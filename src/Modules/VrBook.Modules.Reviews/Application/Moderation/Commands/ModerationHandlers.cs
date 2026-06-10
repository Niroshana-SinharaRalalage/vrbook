using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Reviews.Infrastructure.Persistence;

namespace VrBook.Modules.Reviews.Application.Moderation.Commands;

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
        // Owner verification belongs at the controller level via [Authorize(Roles="Owner,Admin")];
        // for tighter checks the controller can also verify property ownership.
        review.AddOwnerResponse(cmd.Body);
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class HideReviewHandler(ReviewsDbContext db) : IRequestHandler<HideReviewCommand>
{
    public async Task Handle(HideReviewCommand cmd, CancellationToken cancellationToken)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == cmd.ReviewId, cancellationToken)
            ?? throw new NotFoundException("Review", cmd.ReviewId);
        review.Hide();
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class RestoreReviewHandler(ReviewsDbContext db) : IRequestHandler<RestoreReviewCommand>
{
    public async Task Handle(RestoreReviewCommand cmd, CancellationToken cancellationToken)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == cmd.ReviewId, cancellationToken)
            ?? throw new NotFoundException("Review", cmd.ReviewId);
        review.Restore();
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class RejectReviewHandler(ReviewsDbContext db) : IRequestHandler<RejectReviewCommand>
{
    public async Task Handle(RejectReviewCommand cmd, CancellationToken cancellationToken)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == cmd.ReviewId, cancellationToken)
            ?? throw new NotFoundException("Review", cmd.ReviewId);
        review.Reject();
        await db.SaveChangesAsync(cancellationToken);
    }
}
