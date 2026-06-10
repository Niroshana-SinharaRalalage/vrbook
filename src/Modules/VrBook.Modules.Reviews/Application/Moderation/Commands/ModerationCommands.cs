using MediatR;

namespace VrBook.Modules.Reviews.Application.Moderation.Commands;

public sealed record RespondToReviewCommand(Guid ReviewId, string Body) : IRequest;

public sealed record HideReviewCommand(Guid ReviewId) : IRequest;

public sealed record RestoreReviewCommand(Guid ReviewId) : IRequest;

public sealed record RejectReviewCommand(Guid ReviewId) : IRequest;
