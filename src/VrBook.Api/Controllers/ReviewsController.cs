using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Reviews.Application.Queries;

namespace VrBook.Api.Controllers;

/// <summary>Reviews — proposal §6.2 + §11.1.</summary>
[Route("api/v1/reviews")]
[Tags("Reviews")]
[Authorize]
public sealed class ReviewsController : ControllerBase
{
    [HttpPost("{id:guid}/response")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Owner replies to a review (1:1). Lands in A6.1.")]
    [ProducesResponseType(typeof(ReviewResponseDto), StatusCodes.Status201Created)]
    public IActionResult Respond(Guid id, [FromBody] SubmitReviewResponseRequest request) =>
        StatusCode(StatusCodes.Status501NotImplemented, new { detail = "Owner response lands in A6.1." });
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

[Route("api/v1/admin/reviews")]
[Tags("Reviews — Admin")]
[Authorize(Roles = "Owner,Admin")]
public sealed class ReviewsAdminController : ControllerBase
{
    [HttpGet("moderation")]
    public IActionResult Pending() =>
        StatusCode(StatusCodes.Status501NotImplemented, new { detail = "Moderation queue lands in A6.1." });

    [HttpPost("{id:guid}/approve")]
    public IActionResult Approve(Guid id, [FromBody] ModerateReviewRequest request) =>
        StatusCode(StatusCodes.Status501NotImplemented, new { detail = "Moderation queue lands in A6.1." });

    [HttpPost("{id:guid}/reject")]
    public IActionResult Reject(Guid id, [FromBody] ModerateReviewRequest request) =>
        StatusCode(StatusCodes.Status501NotImplemented, new { detail = "Moderation queue lands in A6.1." });
}
