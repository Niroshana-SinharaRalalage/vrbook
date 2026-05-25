using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Filters;

namespace VrBook.Api.Middleware;

/// <summary>
/// Stamps the W3C trace identifier onto the response so clients can correlate
/// with App Insights without inspecting the request headers.
/// </summary>
public sealed class TraceContextFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context) { }

    public void OnActionExecuted(ActionExecutedContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString();
        if (!string.IsNullOrEmpty(traceId))
        {
            context.HttpContext.Response.Headers["traceparent"] =
                $"00-{traceId}-{Activity.Current!.SpanId}-01";
        }
    }
}
