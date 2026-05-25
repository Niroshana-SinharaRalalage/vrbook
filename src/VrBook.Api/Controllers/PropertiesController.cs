using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Common;
using VrBook.Contracts.Dtos;

namespace VrBook.Api.Controllers;

/// <summary>Catalog properties — proposal §6.2.</summary>
[Route("api/v1/properties")]
[Tags("Catalog")]
public sealed class PropertiesController : StubController
{
    [HttpGet]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Public property search.")]
    [ProducesResponseType(typeof(PagedResult<PropertySummaryDto>), StatusCodes.Status200OK)]
    public IActionResult Search([FromQuery] SearchPropertiesRequest request) => NotImplementedYet("A2");

    [HttpGet("{slug}")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Get a property by slug.")]
    [ProducesResponseType(typeof(PropertyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetBySlug(string slug) => NotImplementedYet("A2");

    [HttpGet("{id:guid}/availability")]
    [AllowAnonymous]
    [SwaggerOperation(Summary = "Day-by-day availability for a property.")]
    [ProducesResponseType(typeof(IReadOnlyList<AvailabilityDayDto>), StatusCodes.Status200OK)]
    public IActionResult Availability(Guid id, [FromQuery] DateOnly from, [FromQuery] DateOnly to) =>
        NotImplementedYet("A2", "Backed by IBookingAvailabilityReader (A4-owned).");

    [HttpPost]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Create a property.")]
    [ProducesResponseType(typeof(PropertyDto), StatusCodes.Status201Created)]
    public IActionResult Create([FromBody] CreatePropertyRequest request) => NotImplementedYet("A2");

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Owner,Admin")]
    [ProducesResponseType(typeof(PropertyDto), StatusCodes.Status200OK)]
    public IActionResult Update(Guid id, [FromBody] UpdatePropertyRequest request) => NotImplementedYet("A2");

    [HttpPost("{id:guid}/images")]
    [Authorize(Roles = "Owner,Admin")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(PropertyImageDto), StatusCodes.Status201Created)]
    public IActionResult UploadImage(Guid id, IFormFile file) => NotImplementedYet("A2");

    [HttpPut("{id:guid}/images/order")]
    [Authorize(Roles = "Owner,Admin")]
    public IActionResult ReorderImages(Guid id, [FromBody] ReorderImagesRequest request) =>
        NotImplementedYet("A2");

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    [Authorize(Roles = "Owner,Admin")]
    public IActionResult DeleteImage(Guid id, Guid imageId) => NotImplementedYet("A2");
}

[Route("api/v1/amenities")]
[Tags("Catalog")]
[AllowAnonymous]
public sealed class AmenitiesController : StubController
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AmenityDto>), StatusCodes.Status200OK)]
    public IActionResult List() => NotImplementedYet("A2");
}
