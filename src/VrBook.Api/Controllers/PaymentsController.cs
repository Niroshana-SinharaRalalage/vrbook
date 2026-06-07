using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Payment.Application.Commands;
using VrBook.Modules.Payment.Application.Queries;

namespace VrBook.Api.Controllers;

/// <summary>Payments — proposal §6.2 + §9.</summary>
[Route("api/v1/payments")]
[Tags("Payment")]
[Authorize]
public sealed class PaymentsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Return the PaymentIntent already created for a booking (Booking module creates it
    /// on Place automatically). Returns 404 if no PI exists yet — i.e. Stripe is unconfigured
    /// or the booking pre-dates A5.
    /// </summary>
    [HttpGet("intents/by-booking/{bookingId:guid}")]
    [ProducesResponseType(typeof(CreatePaymentIntentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreatePaymentIntentResponse>> GetForBooking(Guid bookingId, CancellationToken cancellationToken)
    {
        var pi = await mediator.Send(new GetPaymentIntentForBookingQuery(bookingId), cancellationToken);
        return pi is null ? NotFound() : Ok(pi);
    }

    [HttpPost("refunds")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Issue a refund. v1 = full refund only; amount param is ignored.")]
    public async Task<IActionResult> IssueRefund([FromBody] IssueRefundRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new RefundForBookingCommand(request.BookingId, null, request.Reason), cancellationToken);
        return NoContent();
    }
}

/// <summary>Stripe webhook endpoint. Signature-verified, NEVER behind [Authorize].</summary>
[Route("api/v1/payments/webhooks/stripe")]
[Tags("Payment — Webhooks")]
[AllowAnonymous]
public sealed class StripeWebhookController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Handle(CancellationToken cancellationToken)
    {
        // Stripe signature verification operates on the raw HTTP body. We cannot
        // round-trip via model binding without losing bytes-equivalence.
#pragma warning disable S6932
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var signature = Request.Headers["Stripe-Signature"].ToString();
#pragma warning restore S6932
        var ok = await mediator.Send(new HandleStripeWebhookCommand(payload, signature), cancellationToken);
        return ok ? Ok() : BadRequest();
    }
}
