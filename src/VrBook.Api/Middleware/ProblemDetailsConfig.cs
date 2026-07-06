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

            // Slice OPS.M.12 — specific mapping for the admin-vs-social gate.
            // Must land BEFORE the generic ForbiddenException mapper below;
            // Hellang matches first-registered-first.
            opts.Map<AdminSocialIdpRejectedException>(ex => new ProblemDetails
            {
                Type = ProblemTypes.AdminSocialIdpRejected,
                Title = "Admin authority requires Entra local sign-in.",
                Status = StatusCodes.Status403Forbidden,
                Detail = "Sign out and sign in with your Entra credentials " +
                         "(email + OTP or password). Social sign-in is available " +
                         "for the guest experience only.",
                Extensions =
                {
                    ["rule"] = ex.Rule,
                    ["identityProvider"] = ex.IdentityProvider,
                    // AttemptedTenantIds deliberately omitted from response
                    // body — audit-log only. The ILogger.LogWarning in the
                    // middleware carries the full list for Log Analytics.
                },
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
