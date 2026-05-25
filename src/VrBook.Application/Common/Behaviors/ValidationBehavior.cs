using FluentValidation;
using MediatR;
using VrBook.Domain.Common;

namespace VrBook.Application.Common.Behaviors;

/// <summary>
/// Runs all <see cref="IValidator{T}"/> registered for the incoming request before
/// the handler executes. Throws <see cref="BusinessRuleViolationException"/> on failure —
/// which the ProblemDetails middleware turns into a 400 with the standard error envelope.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToArray();

        if (failures.Length > 0)
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}
