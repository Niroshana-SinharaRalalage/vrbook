using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;

namespace VrBook.Modules.Booking;

/// <summary>
/// Module bootstrap for the <c>Booking</c> bounded context. The Api host calls
/// <c>services.AddBookingModule(configuration)</c> from Program.cs. This A0 stub
/// registers nothing meaningful — downstream agents replace it with the real
/// implementation. See proposal §20.2 for the per-agent scope.
/// </summary>
public sealed class BookingModule : IModuleRegistration
{
    public string Name => "booking";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // TODO(agent): register the module's DbContext, MediatR handlers, validators, and
        // context-specific services. To pick up MediatR handlers + FluentValidation
        // validators from this assembly, call:
        //
        //   services.AddModuleAssembly(typeof(BookingModule).Assembly);
        return services;
    }
}

public static class BookingModuleRegistration
{
    public static IServiceCollection AddBookingModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new BookingModule().AddModule(services, configuration);
}
