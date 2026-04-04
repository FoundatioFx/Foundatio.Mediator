using Microsoft.Extensions.DependencyInjection;
using Orders.Module.Data;
using Orders.Module.Handlers;
using StackExchange.Redis;

namespace Orders.Module;

public static class ServiceConfiguration
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services)
    {
        // Use Redis-backed repository when IConnectionMultiplexer is registered,
        // otherwise fall back to in-memory for standalone (non-Aspire) runs
        services.AddSingleton<IOrderRepository>(sp =>
        {
            var redis = sp.GetService<IConnectionMultiplexer>();
            return redis is not null
                ? new RedisOrderRepository(redis)
                : new InMemoryOrderRepository();
        });

        // Register handler with scoped lifetime so it gets the repository injected
        services.AddScoped<OrderHandler>();

        return services;
    }
}
