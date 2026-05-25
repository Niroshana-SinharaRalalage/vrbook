using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace VrBook.Application.Common;

/// <summary>
/// Modular monolith — each bounded context implements this so the Api host can discover
/// and wire them via reflection (or explicitly in Program.cs). Each module is responsible
/// for registering its DbContext, MediatR handlers, validators, and infra services.
/// </summary>
public interface IModuleRegistration
{
    /// <summary>Stable module key (e.g., "identity", "catalog"). Used for logging and toggles.</summary>
    string Name { get; }

    /// <summary>Add the module's services to the host DI container.</summary>
    IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration);
}
