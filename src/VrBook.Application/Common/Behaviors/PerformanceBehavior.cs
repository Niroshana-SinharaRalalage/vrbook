using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace VrBook.Application.Common.Behaviors;

/// <summary>
/// Warns when any handler exceeds 500ms. Tunable via configuration if a slow handler
/// is genuinely necessary (e.g., reports). Critical-path handlers should never trip this.
/// </summary>
public sealed class PerformanceBehavior<TRequest, TResponse>(
    ILogger<PerformanceBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int SlowMs = 500;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();

        if (sw.ElapsedMilliseconds > SlowMs)
        {
            logger.LogWarning(
                "Slow handler: {RequestName} took {ElapsedMs}ms",
                typeof(TRequest).Name, sw.ElapsedMilliseconds);
        }

        return response;
    }
}
