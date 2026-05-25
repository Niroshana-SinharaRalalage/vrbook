using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;

namespace VrBook.Modules.Notifications;

/// <summary>
/// Module bootstrap for the <c>Notifications</c> bounded context. The Api host calls
/// <c>services.AddNotificationsModule(configuration)</c> from Program.cs. This A0 stub
/// registers nothing meaningful — downstream agents replace it with the real
/// implementation. See proposal §20.2 for the per-agent scope.
/// </summary>
public sealed class NotificationsModule : IModuleRegistration
{
    public string Name => "notifications";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // TODO(agent): register the module's DbContext, MediatR handlers, validators, and
        // context-specific services. To pick up MediatR handlers + FluentValidation
        // validators from this assembly, call:
        //
        //   services.AddModuleAssembly(typeof(NotificationsModule).Assembly);
        return services;
    }
}

public static class NotificationsModuleRegistration
{
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new NotificationsModule().AddModule(services, configuration);
}
