using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;

namespace VrBook.Modules.Loyalty;

/// <summary>
/// Module bootstrap for the <c>Loyalty</c> bounded context. The Api host calls
/// <c>services.AddLoyaltyModule(configuration)</c> from Program.cs. This A0 stub
/// registers nothing meaningful — downstream agents replace it with the real
/// implementation. See proposal §20.2 for the per-agent scope.
/// </summary>
public sealed class LoyaltyModule : IModuleRegistration
{
    public string Name => "loyalty";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // TODO(agent): register the module's DbContext, MediatR handlers, validators, and
        // context-specific services. To pick up MediatR handlers + FluentValidation
        // validators from this assembly, call:
        //
        //   services.AddModuleAssembly(typeof(LoyaltyModule).Assembly);
        return services;
    }
}

public static class LoyaltyModuleRegistration
{
    public static IServiceCollection AddLoyaltyModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new LoyaltyModule().AddModule(services, configuration);
}
