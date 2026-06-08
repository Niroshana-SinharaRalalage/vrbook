using Serilog.Events;
using VrBook.Domain.Common;
using FluentValidationException = FluentValidation.ValidationException;

namespace VrBook.Api.Observability;

/// <summary>
/// Suppresses Error-level Serilog events whose exception is a known domain exception
/// (anything deriving from <see cref="DomainException"/>, plus FluentValidation's
/// <see cref="FluentValidationException"/>). The framework's
/// <c>Microsoft.AspNetCore.Hosting.Diagnostics</c> logs these at Error level with a
/// misleading "responded 500" text even though Hellang's ProblemDetailsMiddleware has
/// already rewritten the response to 422/404/409/403/400. Our MediatR
/// <c>LoggingBehavior</c> already logs each at Warning with full context, so the
/// framework's Error-level repeat is pure noise.
///
/// Non-Error levels are never suppressed even if the exception is a domain exception —
/// our Warning-level logs from LoggingBehavior must reach the sink.
/// </summary>
public static class DomainExceptionLogFilter
{
    public static bool ShouldLog(LogEvent evt)
    {
        if (evt.Level != LogEventLevel.Error || evt.Exception is null)
        {
            return true;
        }
        return evt.Exception switch
        {
            DomainException => false,
            FluentValidationException => false,
            _ => true,
        };
    }
}
