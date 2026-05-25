using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos;

namespace VrBook.Api.Controllers;

/// <summary>Payments — proposal §6.2 + §9.</summary>
[Route("api/v1/payments")]
[Tags("Payment")]
[Authorize]
public sealed class PaymentsController : StubController
{
    [HttpPost("intents")]
    [SwaggerOperation(Summary = "Create a Stripe Payment Intent for a booking.")]
    [ProducesResponseType(typeof(CreatePaymentIntentResponse), StatusCodes.Status201Created)]
    public IActionResult CreateIntent([FromBody] CreatePaymentIntentRequest request) =>
        NotImplementedYet("A5");

    [HttpPost("intents/{id:guid}/confirm")]
    [SwaggerOperation(Summary = "Server-side confirm (when 3DS or off-session requires it).")]
    public IActionResult Confirm(Guid id) => NotImplementedYet("A5");

    [HttpGet("intents/{id:guid}")]
    [ProducesResponseType(typeof(PaymentIntentDto), StatusCodes.Status200OK)]
    public IActionResult GetIntent(Guid id) => NotImplementedYet("A5");

    [HttpPost("refunds")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Issue a refund. Amount defaults to policy-driven calculation.")]
    [ProducesResponseType(typeof(RefundDto), StatusCodes.Status201Created)]
    public IActionResult IssueRefund([FromBody] IssueRefundRequest request) => NotImplementedYet("A5");
}

/// <summary>Stripe webhook endpoint. Signature-verified, NEVER behind [Authorize].</summary>
[Route("api/v1/payments/webhooks/stripe")]
[Tags("Payment — Webhooks")]
[AllowAnonymous]
public sealed class StripeWebhookController : StubController
{
    [HttpPost]
    [Consumes("application/json")]
    [SwaggerOperation(Summary = "Stripe webhook. Verifies signature, logs to webhook_events (idempotent), dispatches to handlers.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Handle() => NotImplementedYet("A5",
        "Signature verification + idempotency via payment.webhook_events. See proposal §9.7.");
}
