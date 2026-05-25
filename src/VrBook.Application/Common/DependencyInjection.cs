using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common.Behaviors;

namespace VrBook.Application.Common;

/// <summary>
/// Registers cross-cutting application services: MediatR pipeline behaviors,
/// FluentValidation, and Mapster. Called by the Api host. Modules add their own
/// handlers via <see cref="IModuleRegistration"/>.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplicationCore(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            cfg.AddOpenBehavior(typeof(UnhandledExceptionBehavior<,>));
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));
        });

        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        return services;
    }

    /// <summary>
    /// Module-side convenience: call this from each module's AddXxxModule extension to
    /// pick up MediatR handlers + validators from the module assembly without forcing
    /// the host to know about the assembly.
    /// </summary>
    public static IServiceCollection AddModuleAssembly(this IServiceCollection services, Assembly moduleAssembly)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(moduleAssembly));
        services.AddValidatorsFromAssembly(moduleAssembly, includeInternalTypes: true);
        return services;
    }
}
