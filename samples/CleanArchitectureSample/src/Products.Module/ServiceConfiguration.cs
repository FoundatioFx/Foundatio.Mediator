using Microsoft.Extensions.DependencyInjection;
using Products.Module.Data;
using Products.Module.Handlers;

namespace Products.Module;

public static class ServiceConfiguration
{
    public static IServiceCollection AddProductsModule(this IServiceCollection services)
    {
        // Register the repository - singleton for in-memory demo
        // In production, you'd use Scoped with a real database
        services.AddSingleton<IProductRepository, InMemoryProductRepository>();

        // Register handler with scoped lifetime so it gets the repository injected
        services.AddScoped<ProductHandler>();

        return services;
    }
}
