using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;

namespace VrBook.Api.Controllers;

/// <summary>Messaging — proposal §6.2 + §10.</summary>
[Route("api/v1/threads")]
[Tags("Messaging")]
[Authorize]
public sealed class ThreadsController : StubController
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ThreadDto>), StatusCodes.Status200OK)]
    public IActionResult MyThreads() => NotImplementedYet("A7");

    [HttpGet("{id:guid}/messages")]
    [ProducesResponseType(typeof(PagedResult<MessageDto>), StatusCodes.Status200OK)]
    public IActionResult Messages(Guid id, [FromQuery] string? cursor, [FromQuery] int limit = 50) =>
        NotImplementedYet("A7");

    [HttpPost("{id:guid}/messages")]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status201Created)]
    public IActionResult Send(Guid id, [FromBody] SendMessageRequest request) => NotImplementedYet("A7");

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Read(Guid id, [FromBody] MarkReadRequest request) => NotImplementedYet("A7");

    [HttpPost("{id:guid}/attachments")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(MessageAttachmentDto), StatusCodes.Status201Created)]
    public IActionResult Attach(Guid id, IFormFile file) => NotImplementedYet("A7");
}

[Route("api/v1/realtime")]
[Tags("Messaging")]
[Authorize]
public sealed class RealtimeController : StubController
{
    [HttpGet("negotiate")]
    [SwaggerOperation(Summary = "SignalR negotiate handshake. Returns Service URL + scoped access token.")]
    [ProducesResponseType(typeof(RealtimeNegotiateResponse), StatusCodes.Status200OK)]
    public IActionResult Negotiate() => NotImplementedYet("A7",
        "SignalR Serverless: API generates connection info, client opens WebSocket to SignalR Service.");
}
