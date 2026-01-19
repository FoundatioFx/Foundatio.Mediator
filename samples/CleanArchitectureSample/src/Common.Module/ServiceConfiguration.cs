using Common.Module.Middleware;
using Common.Module.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Module;

public static class ServiceConfiguration
{
    public static IServiceCollection AddCommonModule(this IServiceCollection services)
    {
        // Register middleware (used by the mediator pipeline)
        services.AddSingleton<ObservabilityMiddleware>();

        // Register shared services that can be used by event handlers in other assemblies
        services.AddSingleton<IAuditService, InMemoryAuditService>();
        services.AddSingleton<INotificationService, InMemoryNotificationService>();

        return services;
    }
}
