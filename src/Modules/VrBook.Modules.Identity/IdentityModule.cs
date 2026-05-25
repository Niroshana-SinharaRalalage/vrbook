using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;

namespace VrBook.Modules.Identity;

/// <summary>
/// Module bootstrap for the <c>Identity</c> bounded context. The Api host calls
/// <c>services.AddIdentityModule(configuration)</c> from Program.cs. This A0 stub
/// registers nothing meaningful — downstream agents replace it with the real
/// implementation. See proposal §20.2 for the per-agent scope.
/// </summary>
public sealed class IdentityModule : IModuleRegistration
{
    public string Name => "identity";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // TODO(agent): register the module's DbContext, MediatR handlers, validators, and
        // context-specific services. To pick up MediatR handlers + FluentValidation
        // validators from this assembly, call:
        //
        //   services.AddModuleAssembly(typeof(IdentityModule).Assembly);
        return services;
    }
}

public static class IdentityModuleRegistration
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new IdentityModule().AddModule(services, configuration);
}
