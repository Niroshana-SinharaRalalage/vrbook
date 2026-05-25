using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;

namespace VrBook.Modules.Sync;

/// <summary>
/// Module bootstrap for the <c>Sync</c> bounded context. The Api host calls
/// <c>services.AddSyncModule(configuration)</c> from Program.cs. This A0 stub
/// registers nothing meaningful — downstream agents replace it with the real
/// implementation. See proposal §20.2 for the per-agent scope.
/// </summary>
public sealed class SyncModule : IModuleRegistration
{
    public string Name => "sync";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // TODO(agent): register the module's DbContext, MediatR handlers, validators, and
        // context-specific services. To pick up MediatR handlers + FluentValidation
        // validators from this assembly, call:
        //
        //   services.AddModuleAssembly(typeof(SyncModule).Assembly);
        return services;
    }
}

public static class SyncModuleRegistration
{
    public static IServiceCollection AddSyncModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new SyncModule().AddModule(services, configuration);
}
