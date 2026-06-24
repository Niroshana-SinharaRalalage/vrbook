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
    [SwaggerOperation(Summary = "Add a pricing rule to the plan.")]
    [ProducesResponseType(typeof(PricingRuleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PricingRuleDto>> AddRule(
        Guid propertyId,
        [FromBody] CreatePricingRuleRequest request,
        CancellationToken cancellationToken)
    {
        var dto = await mediator.Send(new AddPricingRuleCommand(propertyId, request), cancellationToken);
        return Created($"/api/v1/properties/{propertyId}/pricing/rules/{dto.Id}", dto);
    }

    [HttpPut("rules/{ruleId:guid}")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Replace a pricing rule's fields. Re-emits PricingRuleAdded/Removed.")]
    [ProducesResponseType(typeof(PricingRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PricingRuleDto>> UpdateRule(
        Guid propertyId,
        Guid ruleId,
        [FromBody] CreatePricingRuleRequest request,
        CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new UpdatePricingRuleCommand(propertyId, ruleId, request), cancellationToken));

    [HttpDelete("rules/{ruleId:guid}")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Remove a pricing rule. Idempotent on unknown id.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteRule(Guid propertyId, Guid ruleId, CancellationToken cancellationToken)
    {
        await mediator.Send(new RemovePricingRuleCommand(propertyId, ruleId), cancellationToken);
        return NoContent();
    }

    [HttpPatch("rules/{ruleId:guid}/enabled")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Toggle a rule's IsEnabled flag. Does NOT raise PricingRuleAdded/Removed.")]
    [ProducesResponseType(typeof(PricingRuleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PricingRuleDto>> SetRuleEnabled(
        Guid propertyId,
        Guid ruleId,
        [FromBody] SetRuleEnabledRequest request,
        CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new SetPricingRuleEnabledCommand(propertyId, ruleId, request.IsEnabled), cancellationToken));

    [HttpPost("rules/reorder")]
    [Authorize(Roles = "Owner,Admin")]
    [SwaggerOperation(Summary = "Rewrite rule priorities 0..N-1. Last-write-wins on concurrent drag.")]
    [ProducesResponseType(typeof(PricingPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PricingPlanDto>> ReorderRules(
        Guid propertyId,
        [FromBody] ReorderRulesRequest request,
        CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new ReorderPricingRulesCommand(propertyId, request.RuleIds), cancellationToken));
}

public sealed record SetRuleEnabledRequest([property: System.Text.Json.Serialization.JsonRequired] bool IsEnabled);
public sealed record ReorderRulesRequest(IReadOnlyList<Guid> RuleIds);

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
