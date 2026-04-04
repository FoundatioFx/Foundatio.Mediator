using Amazon.Runtime;
using Amazon.SQS;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Foundatio.Mediator.Distributed.Aws;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConsoleSample;

public static class ServiceConfiguration
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services, bool useSqs = false)
    {
        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        // Add Foundatio Mediator
        services.AddMediator();

        if (useSqs)
        {
            // Register the SQS client pointing at LocalStack with dummy credentials
            services.AddSingleton<IAmazonSQS>(new AmazonSQSClient(
                new BasicAWSCredentials("test", "test"),
                new AmazonSQSConfig
                {
                    ServiceURL = "http://localhost:4566",
                    AuthenticationRegion = "us-east-1"
                }));

            // Use SQS as the queue transport
            services.AddMediatorSqs();
        }

        // Add distributed queue support (discovers [Queue]-decorated handlers and starts background workers)
        // Falls back to in-memory if no IQueueClient was registered above
        services.AddMediatorDistributed();

        return services;
    }
}
