using Common.Module.Services;
using Foundatio.Resilience;
using Microsoft.Extensions.DependencyInjection;

namespace Common.Module;

public static class ServiceConfiguration
{
    public static IServiceCollection AddCommonModule(this IServiceCollection services)
    {
        // Register .NET MemoryCache for the caching middleware
        services.AddMemoryCache();

        // Register retry policies used by [Retry(PolicyName = "...")] middleware
        services.AddSingleton<IResiliencePolicyProvider>(
            new ResiliencePolicyProvider()
                .WithPolicy("aggressive", p => p
                    .WithMaxAttempts(10)
                    .WithExponentialDelay(TimeSpan.FromMilliseconds(50))
                    .WithJitter()));

        // Register shared services that can be used by event handlers in other assemblies
        services.AddSingleton<IAuditService, InMemoryAuditService>();
        services.AddSingleton<INotificationService, InMemoryNotificationService>();

        // Demo user store for auth handlers
        services.AddSingleton<IDemoUserService, DemoUserService>();

        return services;
    }
}
