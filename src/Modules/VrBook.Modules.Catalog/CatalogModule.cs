using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;

namespace VrBook.Modules.Catalog;

/// <summary>
/// Module bootstrap for the <c>Catalog</c> bounded context. The Api host calls
/// <c>services.AddCatalogModule(configuration)</c> from Program.cs. This A0 stub
/// registers nothing meaningful — downstream agents replace it with the real
/// implementation. See proposal §20.2 for the per-agent scope.
/// </summary>
public sealed class CatalogModule : IModuleRegistration
{
    public string Name => "catalog";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // TODO(agent): register the module's DbContext, MediatR handlers, validators, and
        // context-specific services. To pick up MediatR handlers + FluentValidation
        // validators from this assembly, call:
        //
        //   services.AddModuleAssembly(typeof(CatalogModule).Assembly);
        return services;
    }
}

public static class CatalogModuleRegistration
{
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new CatalogModule().AddModule(services, configuration);
}
