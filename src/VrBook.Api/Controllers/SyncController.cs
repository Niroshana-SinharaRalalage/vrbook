using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos;

namespace VrBook.Api.Controllers;

/// <summary>Outbound iCal feed served publicly with a per-feed secret token.</summary>
[Route("api/v1/feeds")]
[Tags("Sync — Public Feed")]
[AllowAnonymous]
public sealed class FeedsController : StubController
{
    [HttpGet("{outboundToken}.ics")]
    [Produces("text/calendar")]
    [SwaggerOperation(Summary = "Outbound iCal feed. Token-secured. Cached in Redis (10 min TTL).")]
    public IActionResult Get(string outboundToken) => NotImplementedYet("A6",
        "Outbound feed renders confirmed + tentative bookings as VEVENTs. See proposal §8.2.");
}

/// <summary>Admin endpoints for managing inbound channel feeds (proposal §8 + §6.2).</summary>
[Route("api/v1/admin/channel-feeds")]
[Tags("Sync — Admin")]
[Authorize(Roles = "Owner,Admin")]
public sealed class ChannelFeedsController : StubController
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ChannelFeedDto>), StatusCodes.Status200OK)]
    public IActionResult List() => NotImplementedYet("A6");

    [HttpPost]
    [ProducesResponseType(typeof(ChannelFeedDto), StatusCodes.Status201Created)]
    public IActionResult Create([FromBody] CreateChannelFeedRequest request) =>
        NotImplementedYet("A6");

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ChannelFeedDto), StatusCodes.Status200OK)]
    public IActionResult Update(Guid id, [FromBody] UpdateChannelFeedRequest request) =>
        NotImplementedYet("A6");

    [HttpPost("{id:guid}/sync-now")]
    [SwaggerOperation(Summary = "Trigger an immediate sync run for a feed.")]
    public IActionResult SyncNow(Guid id) => NotImplementedYet("A6");
}

[Route("api/v1/admin/sync-conflicts")]
[Tags("Sync — Admin")]
[Authorize(Roles = "Owner,Admin")]
public sealed class SyncConflictsController : StubController
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SyncConflictDto>), StatusCodes.Status200OK)]
    public IActionResult List() => NotImplementedYet("A6");

    [HttpPost("{id:guid}/resolve")]
    [SwaggerOperation(Summary = "Resolve a conflict — keep_direct / cancel_direct / manual_override.")]
    public IActionResult Resolve(Guid id, [FromBody] ResolveConflictRequest request) =>
        NotImplementedYet("A6");
}
