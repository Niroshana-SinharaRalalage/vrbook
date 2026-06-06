using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using VrBook.Contracts.Dtos;
using VrBook.Modules.Pricing.Application.Plans.Commands;
using VrBook.Modules.Pricing.Application.Plans.Queries;
using VrBook.Modules.Pricing.Application.Quotes.Commands;

namespace VrBook.Api.Controllers;

/// <summary>Pricing — proposal §6.2 + §11.2.</summary>
[Route("api/v1/properties/{propertyId:guid}/pricing")]
[Tags("Pricing")]
public sealed class PricingController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Read pricing plan for a property.")]
    [ProducesResponseType(typeof(PricingPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PricingPlanDto>> Get(Guid propertyId, CancellationToken cancellationToken)
    {
        var dto = await mediator.Send(new GetPricingPlanQuery(propertyId), cancellationToken);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpPut]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Replace the pricing plan basics + fees.")]
    [ProducesResponseType(typeof(PricingPlanDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PricingPlanDto>> Update(Guid propertyId, [FromBody] UpdatePricingPlanRequest request, CancellationToken cancellationToken)
    {
        var dto = await mediator.Send(new UpdatePricingPlanCommand(propertyId, request), cancellationToken);
        return Ok(dto);
    }

    [HttpPost("rules")]
    [Authorize(Roles = "Owner,Admin")]
    [ProducesResponseType(typeof(PricingRuleDto), StatusCodes.Status201Created)]
    public IActionResult AddRule(Guid propertyId, [FromBody] CreatePricingRuleRequest request) =>
        StatusCode(StatusCodes.Status501NotImplemented, new { detail = "Pricing rules land in A3.1." });

    [HttpDelete("rules/{ruleId:guid}")]
    [Authorize(Roles = "Owner,Admin")]
    public IActionResult DeleteRule(Guid propertyId, Guid ruleId) =>
        StatusCode(StatusCodes.Status501NotImplemented, new { detail = "Pricing rules land in A3.1." });
}

[Route("api/v1/properties/{propertyId:guid}/quotes")]
[Tags("Pricing")]
[AllowAnonymous]
public sealed class QuotesController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [SwaggerOperation(Summary = "Compute a price quote for a date range.")]
    [ProducesResponseType(typeof(QuoteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<QuoteDto>> Compute(Guid propertyId, [FromBody] QuoteRequest request, CancellationToken cancellationToken)
    {
        var dto = await mediator.Send(new ComputeQuoteCommand(propertyId, request), cancellationToken);
        return Ok(dto);
    }
}
