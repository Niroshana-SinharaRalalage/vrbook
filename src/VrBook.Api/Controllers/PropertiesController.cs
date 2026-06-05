using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Catalog.Application.Properties.Commands;
using VrBook.Modules.Catalog.Application.Properties.Queries;

namespace VrBook.Api.Controllers;

/// <summary>Catalog properties — proposal §6.2.</summary>
[Route("api/v1/properties")]
[Tags("Catalog")]
public sealed class PropertiesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Public property search.")]
    [ProducesResponseType(typeof(PagedResult<PropertySummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PropertySummaryDto>>> Search([FromQuery] SearchPropertiesRequest request, CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new SearchPropertiesQuery(request), cancellationToken);
        return Ok(page);
    }

    [HttpGet("{slug}")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Get a property by slug.")]
    [ProducesResponseType(typeof(PropertyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PropertyDto>> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        var dto = await mediator.Send(new GetPropertyBySlugQuery(slug), cancellationToken);
        return Ok(dto);
    }

    [HttpGet("{id:guid}/availability")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Day-by-day availability for a property.")]
    [ProducesResponseType(typeof(IReadOnlyList<AvailabilityDayDto>), StatusCodes.Status200OK)]
    public IActionResult Availability(Guid id, [FromQuery] DateOnly from, [FromQuery] DateOnly to) =>
        StatusCode(StatusCodes.Status501NotImplemented, new
        {
            type = "https://httpstatuses.io/501",
            title = "Not implemented yet",
            detail = "Backed by IBookingAvailabilityReader, owned by Agent A4 (Booking).",
        });

    [HttpPost]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Create a property.")]
    [ProducesResponseType(typeof(PropertyDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<PropertyDto>> Create([FromBody] CreatePropertyRequest request, CancellationToken cancellationToken)
    {
        var dto = await mediator.Send(new CreatePropertyCommand(request), cancellationToken);
        return CreatedAtAction(nameof(GetBySlug), new { slug = dto.Slug }, dto);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Owner,Admin")]
    [ProducesResponseType(typeof(PropertyDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PropertyDto>> Update(Guid id, [FromBody] UpdatePropertyRequest request, CancellationToken cancellationToken)
    {
        var dto = await mediator.Send(new UpdatePropertyCommand(id, request), cancellationToken);
        return Ok(dto);
    }

    // ---- Image management is deferred to A2.1 (multipart + Blob signer). ----
    [HttpPost("{id:guid}/images")]
    [Authorize(Roles = "Owner,Admin")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(PropertyImageDto), StatusCodes.Status201Created)]
    public IActionResult UploadImage(Guid id, IFormFile file) =>
        StatusCode(StatusCodes.Status501NotImplemented, new { detail = "Image upload lands in A2.1." });

    [HttpPut("{id:guid}/images/order")]
    [Authorize(Roles = "Owner,Admin")]
    public IActionResult ReorderImages(Guid id, [FromBody] ReorderImagesRequest request) =>
        StatusCode(StatusCodes.Status501NotImplemented, new { detail = "Image ordering lands in A2.1." });

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    [Authorize(Roles = "Owner,Admin")]
    public IActionResult DeleteImage(Guid id, Guid imageId) =>
        StatusCode(StatusCodes.Status501NotImplemented, new { detail = "Image deletion lands in A2.1." });
}

[Route("api/v1/amenities")]
[Tags("Catalog")]
[AllowAnonymous]
public sealed class AmenitiesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AmenityDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AmenityDto>>> List(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new VrBook.Modules.Catalog.Application.Amenities.Queries.ListAmenitiesQuery(), cancellationToken));
}
