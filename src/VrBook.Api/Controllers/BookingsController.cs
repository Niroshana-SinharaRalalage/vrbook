using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Booking.Application.Commands;
using VrBook.Modules.Booking.Application.Holds.Commands;
using VrBook.Modules.Booking.Application.Queries;

namespace VrBook.Api.Controllers;

/// <summary>Booking — proposal §6.2, §7.</summary>
[Route("api/v1/bookings")]
[Tags("Booking")]
[Authorize]
public sealed class BookingsController(IMediator mediator) : ControllerBase
{
    // ---- Slice 0.1: 15-minute checkout hold (§9.3) ----
    [HttpPost("holds")]
    [SwaggerOperation(Summary = "Create a 15-minute hold on dates during checkout.")]
    [ProducesResponseType(typeof(HoldDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HoldDto>> CreateHold(
        [FromBody] CreateHoldRequest request, CancellationToken cancellationToken)
    {
        var hold = await mediator.Send(
            new CreateHoldCommand(request.PropertyId, request.Checkin, request.Checkout, request.Guests),
            cancellationToken);
        return CreatedAtAction(nameof(CreateHold), new { id = hold.Id }, hold);
    }

    [HttpDelete("holds/{holdId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReleaseHold(Guid holdId, CancellationToken cancellationToken)
    {
        await mediator.Send(new ReleaseHoldCommand(holdId), cancellationToken);
        return NoContent();
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Place a booking: Draft -> Tentative.")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<BookingDto>> Place([FromBody] PlaceBookingRequest request, CancellationToken cancellationToken)
    {
        var dto = await mediator.Send(new PlaceBookingCommand(request), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BookingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookingDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var dto = await mediator.Send(new GetBookingQuery(id), cancellationToken);
        return Ok(dto);
    }

    [HttpGet]
    [SwaggerOperation(Summary = "List the caller's bookings (cursor-paginated).")]
    [ProducesResponseType(typeof(PagedResult<BookingSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<BookingSummaryDto>>> MyBookings(
        [FromQuery] string? cursor, [FromQuery] int limit, CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new MyBookingsQuery(cursor, limit == 0 ? 20 : limit), cancellationToken);
        return Ok(page);
    }

    [HttpPost("{id:guid}/cancel")]
    [SwaggerOperation(Summary = "Guest cancellation. Tentative or Confirmed -> Cancelled.")]
    public async Task<ActionResult<BookingDto>> Cancel(Guid id, [FromBody] CancelBookingRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new CancelBookingCommand(id, request.Reason ?? string.Empty), cancellationToken));

    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Owner manual confirmation. Tentative -> Confirmed.")]
    public async Task<ActionResult<BookingDto>> Confirm(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ConfirmBookingCommand(id), cancellationToken));

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Owner rejection of a Tentative booking.")]
    public async Task<ActionResult<BookingDto>> Reject(Guid id, [FromBody] RejectBookingRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new RejectBookingCommand(id, request.Reason ?? string.Empty), cancellationToken));

    [HttpPost("{id:guid}/check-in")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<BookingDto>> CheckIn(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new CheckInBookingCommand(id), cancellationToken));

    [HttpPost("{id:guid}/check-out")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<ActionResult<BookingDto>> CheckOut(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new CheckOutBookingCommand(id), cancellationToken));

    [HttpPost("{id:guid}/review")]
    [SwaggerOperation(Summary = "Submit a post-stay review. Only after CheckedOut.")]
    [ProducesResponseType(typeof(ReviewDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ReviewDto>> SubmitReview(
        Guid id, [FromBody] SubmitReviewRequest request, CancellationToken cancellationToken)
    {
        var dto = await mediator.Send(
            new VrBook.Modules.Reviews.Application.Commands.SubmitReviewCommand(id, request.Rating, request.Body),
            cancellationToken);
        return CreatedAtAction(null, dto);
    }
}

[Route("api/v1/admin/bookings")]
[Tags("Booking — Admin")]
[Authorize(Roles = "Owner,Admin")]
public sealed class BookingAdminController : ControllerBase
{
    [HttpGet("queue")]
    [SwaggerOperation(Summary = "Tentative-bookings queue awaiting owner action.")]
    public IActionResult Queue() =>
        StatusCode(StatusCodes.Status501NotImplemented, new { detail = "Owner queue projection lands in A4.1." });

    [HttpPost("manual")]
    [SwaggerOperation(Summary = "Walk-in / phone booking, manually entered by owner.")]
    public IActionResult Manual([FromBody] ManualBookingRequest request) =>
        StatusCode(StatusCodes.Status501NotImplemented, new { detail = "Manual bookings land in A4.1." });
}
