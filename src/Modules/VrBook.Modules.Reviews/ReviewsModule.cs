using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;

namespace VrBook.Modules.Reviews;

/// <summary>
/// Module bootstrap for the <c>Reviews</c> bounded context. The Api host calls
/// <c>services.AddReviewsModule(configuration)</c> from Program.cs. This A0 stub
/// registers nothing meaningful — downstream agents replace it with the real
/// implementation. See proposal §20.2 for the per-agent scope.
/// </summary>
public sealed class ReviewsModule : IModuleRegistration
{
    public string Name => "reviews";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // TODO(agent): register the module's DbContext, MediatR handlers, validators, and
        // context-specific services. To pick up MediatR handlers + FluentValidation
        // validators from this assembly, call:
        //
        //   services.AddModuleAssembly(typeof(ReviewsModule).Assembly);
        return services;
    }
}

public static class ReviewsModuleRegistration
{
    public static IServiceCollection AddReviewsModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new ReviewsModule().AddModule(services, configuration);
}
