using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using VrBook.Contracts.Interfaces;

namespace VrBook.Application.Common.Behaviors;

/// <summary>
/// Logs handler entry/exit with duration. Sensitive request bodies are NOT logged —
/// the ProblemDetails layer is responsible for PII filtering before serialization.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    ICurrentUser currentUser) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestName"] = requestName,
            ["UserId"] = currentUser.UserId,
            ["TraceId"] = Activity.Current?.TraceId.ToString(),
        });

        logger.LogInformation("Handling {RequestName}", requestName);

        try
        {
            var response = await next();
            logger.LogInformation(
                "Handled {RequestName} in {ElapsedMs}ms",
                requestName, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Handler {RequestName} failed after {ElapsedMs}ms — {Exception}",
                requestName, sw.ElapsedMilliseconds, ex.GetType().Name);
            throw;
        }
    }
}
