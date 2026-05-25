using FluentValidation;
using Hellang.Middleware.ProblemDetails;
using Microsoft.AspNetCore.Mvc;
using VrBook.Contracts.Common;
using VrBook.Domain.Common;

namespace VrBook.Api.Middleware;

public static class ProblemDetailsConfig
{
    /// <summary>
    /// Wire RFC 7807 error responses for domain + framework exceptions.
    /// Keep the type URIs stable — clients (the Frontend in particular) switch on them.
    /// </summary>
    public static IServiceCollection AddProblemDetailsConfigured(this IServiceCollection services)
    {
        services.AddProblemDetails(opts =>
        {
            opts.IncludeExceptionDetails = (ctx, _) =>
                ctx.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();

            opts.Map<NotFoundException>(ex => new ProblemDetails
            {
                Type = ProblemTypes.NotFound,
                Title = "Resource not found.",
                Status = StatusCodes.Status404NotFound,
                Detail = ex.Message,
            });

            opts.Map<ConflictException>(ex => new ProblemDetails
            {
                Type = ProblemTypes.Conflict,
                Title = "Conflict.",
                Status = StatusCodes.Status409Conflict,
                Detail = ex.Message,
            });

            opts.Map<ForbiddenException>(ex => new ProblemDetails
            {
                Type = ProblemTypes.Forbidden,
                Title = "Forbidden.",
                Status = StatusCodes.Status403Forbidden,
                Detail = ex.Message,
            });

            opts.Map<BusinessRuleViolationException>(ex => new ProblemDetails
            {
                Type = ProblemTypes.Validation,
                Title = "Business rule violation.",
                Status = StatusCodes.Status422UnprocessableEntity,
                Detail = ex.Message,
                Extensions = { ["rule"] = ex.Rule },
            });

            opts.Map<ValidationException>(ex =>
            {
                var errors = ex.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

                return new ValidationProblemDetails(errors)
                {
                    Type = ProblemTypes.Validation,
                    Title = "One or more validation errors occurred.",
                    Status = StatusCodes.Status400BadRequest,
                };
            });

            opts.MapToStatusCode<UnauthorizedAccessException>(StatusCodes.Status401Unauthorized);

            // Default fallback — produced for unhandled exceptions.
            opts.Rethrow<NotSupportedException>();
        });

        return services;
    }
}
