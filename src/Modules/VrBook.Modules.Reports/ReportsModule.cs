using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VrBook.Application.Common;

namespace VrBook.Modules.Reports;

public sealed class ReportsModule : IModuleRegistration
{
    public string Name => "reports";

    public IServiceCollection AddModule(IServiceCollection services, IConfiguration configuration)
    {
        // No DbContext owned by Reports - handlers inject BookingDbContext +
        // SyncDbContext + IPropertyOwnerLookup directly. See SLICE7_PLAN §2.1.
        services.AddModuleAssembly(typeof(ReportsModule).Assembly);
        return services;
    }
}

public static class ReportsModuleRegistration
{
    public static IServiceCollection AddReportsModule(
        this IServiceCollection services, IConfiguration configuration) =>
        new ReportsModule().AddModule(services, configuration);
}
