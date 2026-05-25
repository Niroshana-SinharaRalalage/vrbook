using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos;

namespace VrBook.Api.Controllers;

/// <summary>Reviews — proposal §6.2 + §11.1.</summary>
[Route("api/v1/reviews")]
[Tags("Reviews")]
[Authorize]
public sealed class ReviewsController : StubController
{
    [HttpPost("{id:guid}/response")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Owner replies to a review (1:1).")]
    [ProducesResponseType(typeof(ReviewResponseDto), StatusCodes.Status201Created)]
    public IActionResult Respond(Guid id, [FromBody] SubmitReviewResponseRequest request) =>
        NotImplementedYet("A8");
}

[Route("api/v1/properties/{propertyId:guid}/reviews")]
[Tags("Reviews")]
[AllowAnonymous]
public sealed class PropertyReviewsController : StubController
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReviewDto>), StatusCodes.Status200OK)]
    public IActionResult List(Guid propertyId) => NotImplementedYet("A8");
}

[Route("api/v1/admin/reviews")]
[Tags("Reviews — Admin")]
[Authorize(Roles = "Owner,Admin")]
public sealed class ReviewsAdminController : StubController
{
    [HttpGet("moderation")]
    [ProducesResponseType(typeof(IReadOnlyList<ReviewDto>), StatusCodes.Status200OK)]
    public IActionResult Pending() => NotImplementedYet("A8");

    [HttpPost("{id:guid}/approve")]
    public IActionResult Approve(Guid id, [FromBody] ModerateReviewRequest request) =>
        NotImplementedYet("A8");

    [HttpPost("{id:guid}/reject")]
    public IActionResult Reject(Guid id, [FromBody] ModerateReviewRequest request) =>
        NotImplementedYet("A8");
}
