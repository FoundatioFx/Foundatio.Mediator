using Microsoft.Extensions.DependencyInjection;
using Orders.Module.Data;
using Orders.Module.Handlers;

namespace Orders.Module;

public static class ServiceConfiguration
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services)
    {
        // Register the repository - singleton for in-memory demo
        // In production, you'd use Scoped with a real database
        services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();

        // Register handler with scoped lifetime so it gets the repository injected
        services.AddScoped<OrderHandler>();

        return services;
    }
}
