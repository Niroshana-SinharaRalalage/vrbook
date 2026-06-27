using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Enums;
using VrBook.Contracts.Interfaces;
using VrBook.Domain.Common;
using VrBook.Modules.Reviews.Application.Moderation.Commands;
using VrBook.Modules.Reviews.Application.Moderation.Queries;
using VrBook.Modules.Reviews.Application.Queries;

namespace VrBook.Api.Controllers;

/// <summary>Reviews — A8.1. Owner-response endpoint + moderation queue land here.</summary>
[Route("api/v1/reviews")]
[Tags("Reviews")]
[Authorize]
public sealed class ReviewsController(IMediator mediator, ICurrentUser currentUser) : ControllerBase
{
    private Guid CallerTenantId() => currentUser.TenantId
        ?? throw new ForbiddenException("Owner action requires a tenant membership.");

    [HttpPost("{id:guid}/response")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Owner replies to a review (1:1).")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Respond(
        Guid id, [FromBody] SubmitReviewResponseRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new RespondToReviewCommand(id, request.Body, CallerTenantId()), cancellationToken);
        return NoContent();
    }
}

[Route("api/v1/properties/{propertyId:guid}/reviews")]
[Tags("Reviews")]
[AllowAnonymous]
public sealed class PropertyReviewsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "List published reviews for a property (paginated).")]
    [ProducesResponseType(typeof(PagedResult<ReviewDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ReviewDto>>> List(
        Guid propertyId,
        [FromQuery] string? cursor,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(
            new GetReviewsForPropertyQuery(propertyId, cursor, limit == 0 ? 20 : limit),
            cancellationToken);
        return Ok(page);
    }
}

/// <summary>A8.1 admin moderation surface for reviews.</summary>
[Route("api/v1/admin/reviews")]
[Tags("Reviews — Admin")]
[Authorize(Roles = "Admin")]
public sealed class ReviewsAdminController(IMediator mediator, ICurrentUser currentUser) : ControllerBase
{
    private Guid CallerTenantId() => currentUser.TenantId
        ?? throw new ForbiddenException("Admin action requires a tenant membership.");

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReviewDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ReviewDto>>> List(
        [FromQuery] ReviewStatus? status, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListReviewsForAdminQuery(status), cancellationToken));

    [HttpPost("{id:guid}/hide")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Hide(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new HideReviewCommand(id, CallerTenantId()), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RestoreReviewCommand(id, CallerTenantId()), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RejectReviewCommand(id, CallerTenantId()), cancellationToken);
        return NoContent();
    }
}

/// <summary>A8.1 — guest-facing loyalty status endpoint.</summary>
[Route("api/v1/me/loyalty")]
[Tags("Loyalty")]
[Authorize]
public sealed class MyLoyaltyController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "Get the current user's loyalty tier + discount + stays-to-next-tier.")]
    [ProducesResponseType(typeof(LoyaltyAccountDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<LoyaltyAccountDto>> Get(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new VrBook.Modules.Loyalty.Application.Accounts.Queries.GetMyLoyaltyQuery(), cancellationToken));
}
