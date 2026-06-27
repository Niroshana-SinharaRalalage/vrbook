using MediatR;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Reviews.Application.Moderation.Commands;

// OPS.M.4 — owner replies + admin moderation actions are tenant-scoped.
// Controller stamps TenantId from currentUser.TenantId; the behavior gates.

public sealed record RespondToReviewCommand(Guid ReviewId, string Body, Guid TenantId) : IRequest, ITenantScoped;

public sealed record HideReviewCommand(Guid ReviewId, Guid TenantId) : IRequest, ITenantScoped;

public sealed record RestoreReviewCommand(Guid ReviewId, Guid TenantId) : IRequest, ITenantScoped;

public sealed record RejectReviewCommand(Guid ReviewId, Guid TenantId) : IRequest, ITenantScoped;
