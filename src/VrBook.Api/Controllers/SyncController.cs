using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Sync.Application.ChannelFeeds.Commands;
using VrBook.Modules.Sync.Application.ChannelFeeds.Queries;
using VrBook.Modules.Sync.Application.Conflicts.Commands;
using VrBook.Modules.Sync.Application.Conflicts.Queries;

namespace VrBook.Api.Controllers;

/// <summary>Outbound iCal feed served publicly. The opaque OutboundToken
/// (set when the ChannelFeed was created) is the only credential — owners can
/// share this URL with their AirBnB / VRBO / Booking.com or with Google Calendar.</summary>
[Route("api/v1/feeds")]
[Tags("Sync — Public Feed")]
[AllowAnonymous]
public sealed class FeedsController(IMediator mediator) : ControllerBase
{
    [HttpGet("{outboundToken}.ics")]
    [Produces("text/calendar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string outboundToken, CancellationToken cancellationToken)
    {
        var ics = await mediator.Send(
            new VrBook.Modules.Sync.Application.Feeds.Queries.GetOutboundFeedQuery(outboundToken),
            cancellationToken);
        return Content(ics, "text/calendar; charset=utf-8");
    }
}

/// <summary>Admin CRUD for inbound iCal channel feeds (A6 stages 2+3).</summary>
[Route("api/v1/admin/channel-feeds")]
[Tags("Sync — Admin")]
[Authorize(Roles = "Admin")]
public sealed class ChannelFeedsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ChannelFeedDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChannelFeedDto>>> List(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListChannelFeedsQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ChannelFeedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChannelFeedDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetChannelFeedQuery(id), cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(ChannelFeedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ChannelFeedDto>> Create(
        [FromBody] CreateChannelFeedRequest request, CancellationToken cancellationToken)
    {
        var dto = await mediator.Send(new CreateChannelFeedCommand(
            request.PropertyId, request.Channel, request.InboundUrl, request.PollIntervalMinutes),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ChannelFeedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChannelFeedDto>> Update(
        Guid id, [FromBody] UpdateChannelFeedRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new UpdateChannelFeedCommand(
            id, request.InboundUrl, request.PollIntervalMinutes, request.IsEnabled),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteChannelFeedCommand(id), cancellationToken);
        return NoContent();
    }
}

/// <summary>Admin view of conflicts + owner resolution endpoint (A6.8).</summary>
[Route("api/v1/admin/sync-conflicts")]
[Tags("Sync — Admin")]
[Authorize(Roles = "Admin")]
public sealed class SyncConflictsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SyncConflictDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SyncConflictDto>>> ListPending(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ListPendingConflictsQuery(), cancellationToken));

    [HttpPost("{id:guid}/resolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Resolve(
        Guid id, [FromBody] ResolveConflictRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new ResolveConflictCommand(id, request.Resolution, request.Notes), cancellationToken);
        return NoContent();
    }
}
