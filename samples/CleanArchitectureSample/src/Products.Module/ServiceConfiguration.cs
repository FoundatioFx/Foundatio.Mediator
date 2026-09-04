using Microsoft.Extensions.DependencyInjection;
using Products.Module.Data;
using Products.Module.Handlers;
using StackExchange.Redis;

namespace Products.Module;

public static class ServiceConfiguration
{
    public static IServiceCollection AddProductsModule(this IServiceCollection services)
    {
        // Use Redis-backed repository when IConnectionMultiplexer is registered,
        // otherwise fall back to in-memory for standalone (non-Aspire) runs
        services.AddSingleton<IProductRepository>(sp =>
        {
            var redis = sp.GetService<IConnectionMultiplexer>();
            return redis is not null
                ? new RedisProductRepository(redis)
                : new InMemoryProductRepository();
        });

        // Register handler with scoped lifetime so it gets the repository injected
        services.AddScoped<ProductHandler>();

        return services;
    }
}
