using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos.Reports;
using VrBook.Modules.Reports.Application.Adr.Queries;
using VrBook.Modules.Reports.Application.Occupancy.Queries;
using VrBook.Modules.Reports.Application.Revenue.Queries;
using VrBook.Modules.Reports.Application.Source.Queries;

namespace VrBook.Api.Controllers;

/// <summary>
/// Slice 7 — owner-facing reports. Cross-property authorization is enforced
/// per-handler via <c>IPropertyOwnerLookup</c>; admins see all properties.
/// See <c>docs/SLICE7_PLAN.md</c> §2.4 + §3 C1.
/// </summary>
[Route("api/v1/admin/reports")]
[Tags("Admin — Reports")]
public sealed class ReportsController(IMediator mediator) : ControllerBase
{
    [HttpGet("occupancy")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Daily occupancy report.")]
    [ProducesResponseType(typeof(OccupancyReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<OccupancyReportDto>> Occupancy(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? propertyId,
        CancellationToken ct) =>
        Ok(await mediator.Send(new GetOccupancyReportQuery(from, to, propertyId), ct));

    [HttpGet("revenue")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Daily revenue report (Confirmed bookings bucket by ConfirmedAt::date).")]
    [ProducesResponseType(typeof(RevenueReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<RevenueReportDto>> Revenue(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? propertyId,
        CancellationToken ct) =>
        Ok(await mediator.Send(new GetRevenueReportQuery(from, to, propertyId), ct));

    [HttpGet("adr")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Daily Average Daily Rate (ADR). Zero-night days emit null.")]
    [ProducesResponseType(typeof(AdrReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdrReportDto>> Adr(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? propertyId,
        CancellationToken ct) =>
        Ok(await mediator.Send(new GetAdrReportQuery(from, to, propertyId), ct));

    [HttpGet("source")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Bookings + nights broken down by source channel.")]
    [ProducesResponseType(typeof(SourceReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SourceReportDto>> Source(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? propertyId,
        CancellationToken ct) =>
        Ok(await mediator.Send(new GetSourceReportQuery(from, to, propertyId), ct));
}
