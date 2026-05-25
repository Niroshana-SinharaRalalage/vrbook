using Microsoft.AspNetCore.Mvc;
using VrBook.Contracts.Common;

namespace VrBook.Api.Controllers;

/// <summary>
/// Base for A0 stubbed controllers. Every action returns RFC 7807 Problem with status 501.
/// Downstream agents replace the action bodies with real MediatR dispatches.
/// </summary>
[ApiController]
public abstract class StubController : ControllerBase
{
    /// <summary>
    /// Returns a 501 Not Implemented response that names the agent who will implement it.
    /// </summary>
    protected IActionResult NotImplementedYet(string agent, string? note = null)
    {
        var pd = new ProblemDetails
        {
            Type = ProblemTypes.Base + "/not-implemented",
            Title = "Endpoint stubbed in A0 scaffold.",
            Status = StatusCodes.Status501NotImplemented,
            Detail = note ?? $"This endpoint is owned by Agent {agent} per BookingApp_Proposal.md §20.",
            Instance = HttpContext.Request.Path,
        };
        pd.Extensions["agent"] = agent;
        pd.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(StatusCodes.Status501NotImplemented, pd);
    }
}
