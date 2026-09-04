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
    /// When <see cref="AwsTransportOptions.ServiceUrl"/> is set, the SQS and SNS SDK
    /// clients are automatically registered; otherwise you must register
    /// <c>IAmazonSQS</c> and <c>IAmazonSimpleNotificationService</c> before calling this.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <param name="configure">Optional configuration for <see cref="AwsTransportOptions"/>.</param>
    /// <returns>The mediator builder for chaining.</returns>
    /// <example>
    /// <code>
    /// // LocalStack / dev — SDK clients auto-registered
    /// services.AddMediator()
    ///     .AddDistributedQueues()
    ///     .AddDistributedNotifications()
    ///     .UseAws(aws => aws.ServiceUrl = "http://localhost:4566");
    ///
    /// // Production — SDK clients pre-registered via AddAWSService
    /// services.AddAWSService&lt;IAmazonSQS&gt;();
    /// services.AddAWSService&lt;IAmazonSimpleNotificationService&gt;();
    /// services.AddMediator()
    ///     .AddDistributedQueues()
    ///     .AddDistributedNotifications()
    ///     .UseAws(aws => aws.Queues.AutoCreateQueues = false);
    /// </code>
    /// </example>
    public static IMediatorBuilder UseAws(
        this IMediatorBuilder builder,
        Action<AwsTransportOptions>? configure = null)
    {
        var options = new AwsTransportOptions();
        configure?.Invoke(options);

        if (!string.IsNullOrEmpty(options.ServiceUrl))
            RegisterSdkClients(builder.Services, options);

        builder.UseAwsQueues(opts =>
        {
            opts.AutoCreateQueues = options.Queues.AutoCreateQueues;
            opts.WaitTimeSeconds = options.Queues.WaitTimeSeconds;
        });

        builder.UseAwsNotifications(opts =>
        {
            opts.TopicName = options.Notifications.TopicName;
            opts.TopicArn = options.Notifications.TopicArn;
            opts.AutoCreate = options.Notifications.AutoCreate;
            opts.QueuePrefix = options.Notifications.QueuePrefix;
            opts.WaitTimeSeconds = options.Notifications.WaitTimeSeconds;
            opts.CleanupOnDispose = options.Notifications.CleanupOnDispose;
        });

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
    ///     .UseAwsQueues(opts => opts.AutoCreateQueues = false);
    /// </code>
    /// </example>
    public static IMediatorBuilder UseAwsQueues(
        this IMediatorBuilder builder,
        Action<SqsQueueClientOptions>? configure = null)
    {
        var services = builder.Services;
        var options = new SqsQueueClientOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IQueueClient, SqsQueueClient>();

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
        var services = builder.Services;
        var options = new SqsPubSubClientOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IPubSubClient>(sp => new SqsPubSubClient(
            sp.GetRequiredService<IAmazonSimpleNotificationService>(),
            sp.GetRequiredService<IAmazonSQS>(),
            options,
            sp.GetRequiredService<DistributedNotificationOptions>(),
            sp.GetRequiredService<ILogger<SqsPubSubClient>>()));

        return builder;
    }

    private static void RegisterSdkClients(IServiceCollection services, AwsTransportOptions options)
    {
        var credentials = options.Credentials
            ?? new BasicAWSCredentials("test", "test");

        if (!services.Any(sd => sd.ServiceType == typeof(IAmazonSQS)))
        {
            var sqsConfig = new AmazonSQSConfig
            {
                ServiceURL = options.ServiceUrl,
                AuthenticationRegion = options.Region
            };
            services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(credentials, sqsConfig));
        }

        if (!services.Any(sd => sd.ServiceType == typeof(IAmazonSimpleNotificationService)))
        {
            var snsConfig = new AmazonSimpleNotificationServiceConfig
            {
                ServiceURL = options.ServiceUrl,
                AuthenticationRegion = options.Region
            };
            services.AddSingleton<IAmazonSimpleNotificationService>(
                _ => new AmazonSimpleNotificationServiceClient(credentials, snsConfig));
        }
    }
}
