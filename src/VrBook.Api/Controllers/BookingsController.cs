using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;

namespace VrBook.Api.Controllers;

/// <summary>Booking — proposal §6.2, §7.</summary>
[Route("api/v1/bookings")]
[Tags("Booking")]
[Authorize]
public sealed class BookingsController : StubController
{
    [HttpPost("holds")]
    [SwaggerOperation(Summary = "Create a 15-minute Redis hold on dates during checkout. Idempotent.")]
    [ProducesResponseType(typeof(HoldDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult CreateHold([FromBody] CreateHoldRequest request) => NotImplementedYet("A4");

    [HttpDelete("holds/{holdId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult ReleaseHold(Guid holdId) => NotImplementedYet("A4");

    [HttpPost]
    [SwaggerOperation(Summary = "Place a booking: Draft -> Tentative on successful payment authorization.")]
    [ProducesResponseType(typeof(PlaceBookingResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult Place([FromBody] PlaceBookingRequest request) => NotImplementedYet("A4");

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Get(Guid id) => NotImplementedYet("A4");

    [HttpGet]
    [SwaggerOperation(Summary = "List the caller's bookings (cursor-paginated).")]
    [ProducesResponseType(typeof(PagedResult<BookingSummaryDto>), StatusCodes.Status200OK)]
    public IActionResult MyBookings([FromQuery] string? cursor, [FromQuery] int limit = 20) =>
        NotImplementedYet("A4");

    [HttpPost("{id:guid}/cancel")]
    [SwaggerOperation(Summary = "Guest cancellation. Computes refund per cancellation policy.")]
    public IActionResult Cancel(Guid id, [FromBody] CancelBookingRequest request) =>
        NotImplementedYet("A4");

    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Owner manual confirmation. Tentative -> Confirmed.")]
    public IActionResult Confirm(Guid id) => NotImplementedYet("A4");

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Owner rejection of a Tentative booking. Triggers refund.")]
    public IActionResult Reject(Guid id, [FromBody] RejectBookingRequest request) =>
        NotImplementedYet("A4");

    [HttpPost("{id:guid}/check-in")]
    [Authorize(Roles = "Owner,Admin")]
    public IActionResult CheckIn(Guid id) => NotImplementedYet("A4");

    [HttpPost("{id:guid}/check-out")]
    [Authorize(Roles = "Owner,Admin")]
    public IActionResult CheckOut(Guid id) => NotImplementedYet("A4");

    [HttpPost("{id:guid}/review")]
    [SwaggerOperation(Summary = "Submit a post-stay review. Only after CheckedOut.")]
    public IActionResult SubmitReview(Guid id, [FromBody] SubmitReviewRequest request) =>
        NotImplementedYet("A8");
}

[Route("api/v1/admin/bookings")]
[Tags("Booking — Admin")]
[Authorize(Roles = "Owner,Admin")]
public sealed class BookingAdminController : StubController
{
    [HttpGet("queue")]
    [SwaggerOperation(Summary = "Tentative-bookings queue awaiting owner action.")]
    [ProducesResponseType(typeof(IReadOnlyList<BookingQueueRowDto>), StatusCodes.Status200OK)]
    public IActionResult Queue() => NotImplementedYet("A4");

    [HttpPost("manual")]
    [SwaggerOperation(Summary = "Walk-in / phone booking, manually entered by owner.")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status201Created)]
    public IActionResult Manual([FromBody] ManualBookingRequest request) => NotImplementedYet("A4");
}
