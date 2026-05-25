using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos;

namespace VrBook.Api.Controllers;

/// <summary>Pricing — proposal §6.2 + §11.2.</summary>
[Route("api/v1/properties/{propertyId:guid}/pricing")]
[Tags("Pricing")]
public sealed class PricingController : StubController
{
    [HttpGet]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Read pricing plan for a property.")]
    [ProducesResponseType(typeof(PricingPlanDto), StatusCodes.Status200OK)]
    public IActionResult Get(Guid propertyId) => NotImplementedYet("A3");

    [HttpPut]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Replace the pricing plan basics + fees.")]
    [ProducesResponseType(typeof(PricingPlanDto), StatusCodes.Status200OK)]
    public IActionResult Update(Guid propertyId, [FromBody] UpdatePricingPlanRequest request) =>
        NotImplementedYet("A3");

    [HttpPost("rules")]
    [Authorize(Roles = "Owner,Admin")]
    [ProducesResponseType(typeof(PricingRuleDto), StatusCodes.Status201Created)]
    public IActionResult AddRule(Guid propertyId, [FromBody] CreatePricingRuleRequest request) =>
        NotImplementedYet("A3");

    [HttpDelete("rules/{ruleId:guid}")]
    [Authorize(Roles = "Owner,Admin")]
    public IActionResult DeleteRule(Guid propertyId, Guid ruleId) => NotImplementedYet("A3");
}

[Route("api/v1/properties/{propertyId:guid}/quotes")]
[Tags("Pricing")]
[AllowAnonymous]
public sealed class QuotesController : StubController
{
    [HttpPost]
    [SwaggerOperation(Summary = "Compute a price quote for a date range. Rate-limited.")]
    [ProducesResponseType(typeof(QuoteDto), StatusCodes.Status200OK)]
    public IActionResult Compute(Guid propertyId, [FromBody] QuoteRequest request) =>
        NotImplementedYet("A3");
}
