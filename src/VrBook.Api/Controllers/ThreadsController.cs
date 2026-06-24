using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Messaging.Application.Threads.Commands;
using VrBook.Modules.Messaging.Application.Threads.Queries;

namespace VrBook.Api.Controllers;

/// <summary>Messaging endpoints — A7 v1. Attachments (A7.5) and SignalR
/// negotiate (A7.6) remain stubbed.</summary>
[Route("api/v1/threads")]
[Tags("Messaging")]
[Authorize]
public sealed class ThreadsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ThreadDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ThreadDto>>> MyThreads(
        [FromQuery] Guid? bookingId,
        CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListMyThreadsQuery(bookingId), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ThreadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ThreadDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetThreadQuery(id), cancellationToken));

    [HttpGet("{id:guid}/messages")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<MessageDto>>> Messages(
        Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListMessagesQuery(id), cancellationToken));

    [HttpPost("{id:guid}/messages")]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MessageDto>> Send(
        Guid id, [FromBody] SendMessageRequest request, CancellationToken cancellationToken)
    {
        var msg = await mediator.Send(new SendMessageCommand(id, request.Body), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = msg.ThreadId }, msg);
    }

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Read(
        Guid id, [FromBody] MarkReadRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new MarkReadCommand(id, request.UpToMessageId), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/attachments")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MessageAttachmentDto), StatusCodes.Status501NotImplemented)]
    public IActionResult Attach(Guid id, IFormFile file) =>
        StatusCode(StatusCodes.Status501NotImplemented, new
        {
            type = "https://httpstatuses.io/501",
            title = "Attachments not yet implemented",
            detail = "Attachments (A7.5) require Blob storage + SAS URL wiring — deferred from A7 v1.",
            agent = "A7.5",
        });
}

[Route("api/v1/realtime")]
[Tags("Messaging")]
[Authorize]
public sealed class RealtimeController(
    VrBook.Contracts.Interfaces.IRealtimeNotifier notifier,
    VrBook.Contracts.Interfaces.ICurrentUser currentUser,
    Microsoft.Extensions.Logging.ILogger<RealtimeController> logger) : ControllerBase
{
    [HttpGet("negotiate")]
    [SwaggerOperation(Summary = "SignalR negotiate handshake. Returns Service URL + scoped access token.")]
    [ProducesResponseType(typeof(RealtimeNegotiateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RealtimeNegotiateResponse>> Negotiate(CancellationToken ct)
    {
        if (currentUser.UserId is not { } uid)
        {
            return Unauthorized();
        }

        try
        {
            // 1-second CTS guard so a hung SignalR endpoint doesn't hold the
            // negotiate request (SLICE7_PLAN §2.6). The token TTL itself is
            // 1h - this CTS only bounds the negotiate-API hop, not the token.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            var result = await notifier.NegotiateForUserAsync(uid, cts.Token);
            return Ok(new RealtimeNegotiateResponse(result.Url, result.AccessToken, result.ExpiresAt));
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or HttpRequestException)
        {
            logger.LogWarning(ex, "SignalR negotiate failed for user {UserId}", uid);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { detail = "realtime.unavailable" });
        }
    }
}
