using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConsoleSample;

public static class ServiceConfiguration
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services)
    {
        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        // Add Foundatio Mediator
        services.AddMediator();

        // Register handlers explicitly for now
        services.AddTransient<ConsoleSample.Handlers.OrderHandler>();

        return services;
    }
}
