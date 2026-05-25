using MediatR;
using Microsoft.Extensions.Logging;
using VrBook.Domain.Common;

namespace VrBook.Application.Common.Behaviors;

/// <summary>
/// Logs unhandled exceptions with full context before they propagate to the ProblemDetails
/// middleware. Domain exceptions are intentionally NOT treated as errors here — they
/// represent expected business outcomes (validation, conflict, not-found).
/// </summary>
public sealed class UnhandledExceptionBehavior<TRequest, TResponse>(
    ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (DomainException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unhandled exception in {RequestName}",
                typeof(TRequest).Name);
            throw;
        }
    }
}
