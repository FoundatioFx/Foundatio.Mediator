using Foundatio.Mediator;
using Foundatio.Mediator.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConsoleSample;

public static class ServiceConfiguration
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services)
    {
        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        // Add Foundatio Mediator
        services.AddMediator();

        // Add queue support (discovers [Queue]-decorated handlers and starts background workers)
        services.AddMediatorQueues();

        return services;
    }
}
