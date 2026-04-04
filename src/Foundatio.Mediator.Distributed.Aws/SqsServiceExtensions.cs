using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>
/// Extension methods for registering the SQS transport for Foundatio.Mediator.Distributed.
/// </summary>
public static class SqsServiceExtensions
{
    /// <summary>
    /// Registers <see cref="SqsQueueClient"/> as the <see cref="IQueueClient"/> implementation.
    /// Must be called <b>before</b> <c>AddMediatorDistributed()</c> so that the SQS client
    /// is already registered when the middleware and workers are wired up.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration for <see cref="SqsQueueClientOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// // LocalStack / dev
    /// services.AddSingleton&lt;IAmazonSQS&gt;(new AmazonSQSClient(
    ///     new AmazonSQSConfig { ServiceURL = "http://localhost:4566" }));
    /// services.AddMediatorSqs();
    /// services.AddMediatorDistributed();
    ///
    /// // Production (uses default credential chain)
    /// services.AddAWSService&lt;IAmazonSQS&gt;();
    /// services.AddMediatorSqs(opts => opts.AutoCreateQueues = false);
    /// services.AddMediatorDistributed();
    /// </code>
    /// </example>
    public static IServiceCollection AddMediatorSqs(
        this IServiceCollection services,
        Action<SqsQueueClientOptions>? configure = null)
    {
        var options = new SqsQueueClientOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IQueueClient, SqsQueueClient>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="SnsSqsPubSubClient"/> as the <see cref="IPubSubClient"/> implementation.
    /// Must be called <b>before</b> <c>AddMediatorDistributedNotifications()</c> so that the
    /// bus is already registered. Requires <see cref="IAmazonSimpleNotificationService"/> and
    /// <see cref="IAmazonSQS"/> in DI.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration for <see cref="SnsSqsPubSubClientOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddSingleton&lt;IAmazonSQS&gt;(new AmazonSQSClient(...));
    /// services.AddSingleton&lt;IAmazonSimpleNotificationService&gt;(new AmazonSimpleNotificationServiceClient(...));
    /// services.AddMediatorSnsSqsPubSub();
    /// services.AddMediatorDistributedNotifications();
    /// </code>
    /// </example>
    public static IServiceCollection AddMediatorSnsSqsPubSub(
        this IServiceCollection services,
        Action<SnsSqsPubSubClientOptions>? configure = null)
    {
        var options = new SnsSqsPubSubClientOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IPubSubClient>(sp => new SnsSqsPubSubClient(
            sp.GetRequiredService<IAmazonSimpleNotificationService>(),
            sp.GetRequiredService<IAmazonSQS>(),
            options,
            sp.GetRequiredService<DistributedNotificationOptions>(),
            sp.GetRequiredService<ILogger<SnsSqsPubSubClient>>()));

        return services;
    }
}
