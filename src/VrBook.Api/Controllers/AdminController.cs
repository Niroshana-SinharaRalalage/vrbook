using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;

namespace VrBook.Api.Controllers;

/// <summary>Admin — proposal §6.2.</summary>
[Route("api/v1/admin/users")]
[Tags("Admin — Users")]
[Authorize(Roles = "Owner,Admin")]
public sealed class AdminUsersController : StubController
{
    [HttpGet]
    [SwaggerOperation(Summary = "Search guests.")]
    [ProducesResponseType(typeof(OffsetPagedResult<UserDto>), StatusCodes.Status200OK)]
    public IActionResult Search([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int size = 20) =>
        NotImplementedYet("A1");
}

[Route("api/v1/admin/reports")]
[Tags("Admin — Reports")]
[Authorize(Roles = "Owner,Admin")]
public sealed class ReportsController : StubController
{
    [HttpGet("occupancy")]
    [ProducesResponseType(typeof(IReadOnlyList<OccupancyReportRow>), StatusCodes.Status200OK)]
    public IActionResult Occupancy([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? propertyId) =>
        NotImplementedYet("O1");

    [HttpGet("revenue")]
    [ProducesResponseType(typeof(IReadOnlyList<RevenueReportRow>), StatusCodes.Status200OK)]
    public IActionResult Revenue([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? propertyId) =>
        NotImplementedYet("O1");

    [HttpGet("adr")]
    [ProducesResponseType(typeof(IReadOnlyList<AdrReportRow>), StatusCodes.Status200OK)]
    public IActionResult Adr([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? propertyId) =>
        NotImplementedYet("O1");
}

[Route("api/v1/admin/toggles")]
[Tags("Admin — Feature toggles")]
[Authorize(Roles = "Owner,Admin")]
public sealed class TogglesController : StubController
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<FeatureToggleDto>), StatusCodes.Status200OK)]
    public IActionResult List() => NotImplementedYet("O1");

    [HttpPut("{key}")]
    [ProducesResponseType(typeof(FeatureToggleDto), StatusCodes.Status200OK)]
    public IActionResult Update(string key, [FromBody] UpdateFeatureToggleRequest request) =>
        NotImplementedYet("O1");
}

[Route("api/v1/admin/alerts")]
[Tags("Admin — Alerts")]
[Authorize(Roles = "Owner,Admin")]
public sealed class AlertsController : StubController
{
    [HttpGet]
    [SwaggerOperation(Summary = "Dashboard banner alerts (Sev3): sync stale, dispute opened, conflict pending.")]
    [ProducesResponseType(typeof(IReadOnlyList<AlertDto>), StatusCodes.Status200OK)]
    public IActionResult List() => NotImplementedYet("O1");

    [HttpPost("{id:guid}/dismiss")]
    public IActionResult Dismiss(Guid id) => NotImplementedYet("O1");
}
