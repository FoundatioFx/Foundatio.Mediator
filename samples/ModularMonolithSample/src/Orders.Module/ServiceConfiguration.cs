using Microsoft.Extensions.DependencyInjection;
using Orders.Module.Handlers;

namespace Orders.Module;

public static class ServiceConfiguration
{
    public static IServiceCollection AddOrdersModule(this IServiceCollection services)
    {
        services.AddSingleton<OrderHandler>();

        return services;
    }
}
