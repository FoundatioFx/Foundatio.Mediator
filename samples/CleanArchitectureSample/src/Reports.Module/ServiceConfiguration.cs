using Microsoft.Extensions.DependencyInjection;
using Reports.Module.Handlers;

namespace Reports.Module;

public static class ServiceConfiguration
{
    public static IServiceCollection AddReportsModule(this IServiceCollection services)
    {
        // Register handler with scoped lifetime
        services.AddScoped<ReportHandler>();

        return services;
    }
}
