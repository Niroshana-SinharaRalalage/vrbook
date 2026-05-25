using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;

namespace VrBook.Modules.Admin;

/// <summary>
/// Module bootstrap for the <c>Admin</c> bounded context. The Api host calls
/// <c>services.AddAdminModule(configuration)</c> from Program.cs. This A0 stub
/// registers nothing meaningful — downstream agents replace it with the real
/// implementation. See proposal §20.2 for the per-agent scope.
/// </summary>
public sealed class AdminModule : IModuleRegistration
{
    public string Name => "admin";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // TODO(agent): register the module's DbContext, MediatR handlers, validators, and
        // context-specific services. To pick up MediatR handlers + FluentValidation
        // validators from this assembly, call:
        //
        //   services.AddModuleAssembly(typeof(AdminModule).Assembly);
        return services;
    }
}

public static class AdminModuleRegistration
{
    public static IServiceCollection AddAdminModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new AdminModule().AddModule(services, configuration);
}
