using Common.Module.Middleware;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Module;

public static class ServiceConfiguration
{
    public static IServiceCollection AddCommonModule(this IServiceCollection services)
    {
        services.AddSingleton<PerformanceMiddleware>();

        return services;
    }
}
