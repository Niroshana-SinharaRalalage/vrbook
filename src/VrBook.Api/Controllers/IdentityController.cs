using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos;

namespace VrBook.Api.Controllers;

/// <summary>Identity — proposal §6.2.</summary>
[Route("api/v1/me")]
[Authorize]
[Tags("Identity")]
public sealed class IdentityController : StubController
{
    [HttpGet]
    [SwaggerOperation(Summary = "Get the current user's profile.")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public IActionResult Get() => NotImplementedYet("A1");

    [HttpPut]
    [SwaggerOperation(Summary = "Update the current user's profile.")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public IActionResult Update([FromBody] UpdateProfileRequest request) => NotImplementedYet("A1");

    [HttpDelete]
    [SwaggerOperation(Summary = "Self-deactivate (GDPR-ready).")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Deactivate() => NotImplementedYet("A1");
}
