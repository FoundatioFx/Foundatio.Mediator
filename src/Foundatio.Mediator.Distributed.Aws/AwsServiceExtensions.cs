using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>
/// Extension methods for configuring AWS SQS/SNS transports on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class AwsBuilderExtensions
{
    /// <summary>
    /// Configures both SQS queues and SNS/SQS pub/sub as the distributed transports.
    /// When <see cref="AwsTransportOptions.ServiceUrl"/> is set, the SQS and SNS SDK clients target that
    /// endpoint; otherwise they come from the SDK's default credential and region chain. Either way an
    /// <c>IAmazonSQS</c> or <c>IAmazonSimpleNotificationService</c> already registered in DI wins.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <param name="configure">Optional configuration for <see cref="AwsTransportOptions"/>.</param>
    /// <returns>The mediator builder for chaining.</returns>
    /// <example>
    /// <code>
    /// // LocalStack / dev — SDK clients target the emulator with test credentials
    /// services.AddMediator()
    ///     .AddDistributedQueues()
    ///     .AddDistributedNotifications()
    ///     .UseAws(aws => aws.ServiceUrl = "http://localhost:4566");
    ///
    /// // Production — default credential chain, queues provisioned elsewhere
    /// services.AddMediator()
    ///     .AddDistributedQueues()
    ///     .AddDistributedNotifications()
    ///     .UseAws(aws => aws.Queues.Provisioning = SqsProvisioningMode.Validate);
    /// </code>
    /// </example>
    public static IMediatorBuilder UseAws(
        this IMediatorBuilder builder,
        Action<AwsTransportOptions>? configure = null)
    {
        var options = new AwsTransportOptions();
        configure?.Invoke(options);

        RegisterSdkClients(builder.Services, options);
        RegisterQueues(builder.Services, options.Queues);
        RegisterNotifications(builder.Services, options.Notifications);

        return builder;
    }

    /// <summary>
    /// Registers <see cref="SqsQueueClient"/> as the <see cref="IQueueClient"/> implementation.
    /// Requires <c>IAmazonSQS</c> to be registered in DI.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <param name="configure">Optional configuration for <see cref="SqsQueueClientOptions"/>.</param>
    /// <returns>The mediator builder for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddAWSService&lt;IAmazonSQS&gt;();
    /// services.AddMediator()
    ///     .AddDistributedQueues()
    ///     .UseAwsQueues(opts => opts.Provisioning = SqsProvisioningMode.Validate);
    /// </code>
    /// </example>
    public static IMediatorBuilder UseAwsQueues(
        this IMediatorBuilder builder,
        Action<SqsQueueClientOptions>? configure = null)
    {
        var options = new SqsQueueClientOptions();
        configure?.Invoke(options);

        RegisterQueues(builder.Services, options);
        return builder;
    }

    /// <summary>
    /// Registers <see cref="SqsPubSubClient"/> as the <see cref="IPubSubClient"/> implementation.
    /// Requires <c>IAmazonSimpleNotificationService</c> and <c>IAmazonSQS</c> to be registered in DI.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <param name="configure">Optional configuration for <see cref="SqsPubSubClientOptions"/>.</param>
    /// <returns>The mediator builder for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddAWSService&lt;IAmazonSQS&gt;();
    /// services.AddAWSService&lt;IAmazonSimpleNotificationService&gt;();
    /// services.AddMediator()
    ///     .AddDistributedNotifications()
    ///     .UseAwsNotifications();
    /// </code>
    /// </example>
    public static IMediatorBuilder UseAwsNotifications(
        this IMediatorBuilder builder,
        Action<SqsPubSubClientOptions>? configure = null)
    {
        var options = new SqsPubSubClientOptions();
        configure?.Invoke(options);

        RegisterNotifications(builder.Services, options);
        return builder;
    }

    private static void RegisterQueues(IServiceCollection services, SqsQueueClientOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<IQueueClient>(sp => new SqsQueueClient(
            sp.GetRequiredService<IAmazonSQS>(),
            options,
            sp.GetService<TimeProvider>(),
            sp.GetRequiredService<ILogger<SqsQueueClient>>()));
    }

    private static void RegisterNotifications(IServiceCollection services, SqsPubSubClientOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<IPubSubClient>(sp => new SqsPubSubClient(
            sp.GetRequiredService<IAmazonSimpleNotificationService>(),
            sp.GetRequiredService<IAmazonSQS>(),
            options,
            sp.GetRequiredService<DistributedNotificationOptions>(),
            sp.GetRequiredService<ILogger<SqsPubSubClient>>(),
            sp.GetService<TimeProvider>()));
    }

    private static void RegisterSdkClients(IServiceCollection services, AwsTransportOptions options)
    {
        bool hasSqs = services.Any(sd => sd.ServiceType == typeof(IAmazonSQS));
        bool hasSns = services.Any(sd => sd.ServiceType == typeof(IAmazonSimpleNotificationService));
        if (hasSqs && hasSns)
            return;

        if (string.IsNullOrEmpty(options.ServiceUrl))
        {
            // Default SDK chain: environment, profile, instance/task role, and the configured region.
            if (!hasSqs)
                services.AddSingleton<IAmazonSQS>(_ => options.Credentials is null ? new AmazonSQSClient() : new AmazonSQSClient(options.Credentials));
            if (!hasSns)
                services.AddSingleton<IAmazonSimpleNotificationService>(_ => options.Credentials is null
                    ? new AmazonSimpleNotificationServiceClient()
                    : new AmazonSimpleNotificationServiceClient(options.Credentials));
            return;
        }

        var credentials = options.Credentials ?? new BasicAWSCredentials("test", "test");

        if (!hasSqs)
        {
            services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(credentials, new AmazonSQSConfig
            {
                ServiceURL = options.ServiceUrl,
                AuthenticationRegion = options.Region
            }));
        }

        if (!hasSns)
        {
            services.AddSingleton<IAmazonSimpleNotificationService>(_ => new AmazonSimpleNotificationServiceClient(credentials, new AmazonSimpleNotificationServiceConfig
            {
                ServiceURL = options.ServiceUrl,
                AuthenticationRegion = options.Region
            }));
        }
    }
}
