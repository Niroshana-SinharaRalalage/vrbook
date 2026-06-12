using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Booking.Application.Queries;
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
    [SwaggerOperation(Summary = "Blocked date ranges for a property in [from, to).")]
    [ProducesResponseType(typeof(AvailabilityDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AvailabilityDto>> Availability(
        Guid id, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetPropertyAvailabilityQuery(id, from, to), cancellationToken));

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

/// <summary>Slice 1 — admin list of the caller's properties (or all for admins).</summary>
[Route("api/v1/admin/properties")]
[Tags("Catalog — Admin")]
[Authorize(Roles = "Owner,Admin")]
public sealed class AdminPropertiesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminPropertySummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AdminPropertySummaryDto>>> ListMine(
        CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new VrBook.Modules.Catalog.Application.Properties.Queries.ListMyPropertiesQuery(),
            cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PropertyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PropertyDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        // Use the FULL-detail query (Address/Capacity/HouseRules/Amenities/Images)
        // because the edit page needs all of it. The lighter
        // GetPropertyByIdQuery -> PropertyBasicInfo is reserved for cross-module
        // reads where only Slug/Title/IsActive are needed.
        var dto = await mediator.Send(
            new VrBook.Modules.Catalog.Application.Properties.Queries.GetPropertyDetailByIdQuery(id),
            cancellationToken);
        return Ok(dto);
    }
}

/// <summary>Slice 0.6 — multi-source availability aggregator for /admin/calendar.</summary>
[Route("api/v1/properties/{propertyId:guid}/calendar")]
[Tags("Booking")]
[Authorize]
public sealed class PropertyCalendarController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PropertyCalendarDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PropertyCalendarDto>> Get(
        Guid propertyId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        if (to <= from)
        {
            return BadRequest(new { detail = "`to` must be after `from`." });
        }
        var dto = await mediator.Send(
            new VrBook.Modules.Booking.Application.Queries.GetPropertyCalendarQuery(propertyId, from, to),
            cancellationToken);
        return Ok(dto);
    }
}

/// <summary>Admin CRUD for the amenity catalog (A2.2).</summary>
[Route("api/v1/admin/amenities")]
[Tags("Catalog — Admin")]
[Authorize(Roles = "Admin")]
public sealed class AdminAmenitiesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AmenityDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AmenityDto>>> ListAll(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new VrBook.Modules.Catalog.Application.Amenities.Queries.ListAllAmenitiesQuery(),
            cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(AmenityDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AmenityDto>> Create(
        [FromBody] CreateAmenityRequest request, CancellationToken cancellationToken)
    {
        var dto = await mediator.Send(
            new VrBook.Modules.Catalog.Application.Amenities.Commands.CreateAmenityCommand(
                request.Code, request.Name, request.Icon, request.Category),
            cancellationToken);
        return CreatedAtAction(nameof(ListAll), new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AmenityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AmenityDto>> Update(
        Guid id, [FromBody] UpdateAmenityRequest request, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new VrBook.Modules.Catalog.Application.Amenities.Commands.UpdateAmenityCommand(
                id, request.Name, request.Icon, request.Category),
            cancellationToken));

    [HttpPost("{id:guid}/disable")]
    [ProducesResponseType(typeof(AmenityDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AmenityDto>> Disable(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new VrBook.Modules.Catalog.Application.Amenities.Commands.DisableAmenityCommand(id),
            cancellationToken));

    [HttpPost("{id:guid}/enable")]
    [ProducesResponseType(typeof(AmenityDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AmenityDto>> Enable(Guid id, CancellationToken cancellationToken) =>
        Ok(await mediator.Send(
            new VrBook.Modules.Catalog.Application.Amenities.Commands.EnableAmenityCommand(id),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await mediator.Send(
            new VrBook.Modules.Catalog.Application.Amenities.Commands.DeleteAmenityCommand(id),
            cancellationToken);
        return NoContent();
    }
}
