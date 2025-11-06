using Microsoft.Extensions.DependencyInjection;
using Products.Module.Handlers;

namespace Products.Module;

public static class ServiceConfiguration
{
    public static IServiceCollection AddProductsModule(this IServiceCollection services)
    {
        services.AddSingleton<ProductHandler>();

        return services;
    }
}
