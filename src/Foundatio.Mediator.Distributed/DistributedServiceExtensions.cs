using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Extension methods for registering distributed queue support for Foundatio.Mediator.
/// </summary>
public static class DistributedServiceExtensions
{
    /// <summary>
    /// Adds distributed queue processing support to Foundatio.Mediator.
    /// Handlers decorated with <see cref="QueueAttribute"/> will have their messages
    /// serialized and sent to a queue for asynchronous processing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback for <see cref="DistributedQueueOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddMediator();
    /// services.AddMediatorDistributed();
    ///
    /// // Or with a custom transport:
    /// services.AddSingleton&lt;IQueueClient, SqsQueueClient&gt;();
    /// services.AddMediatorDistributed(opts => opts.Group = "order-processing");
    /// </code>
    /// </example>
    public static IServiceCollection AddMediatorDistributed(
        this IServiceCollection services,
        Action<DistributedQueueOptions>? configure = null)
    {
        // Prevent double registration
        if (services.Any(sd => sd.ServiceType == typeof(QueueMiddleware)))
            return services;

        var registry = services.GetHandlerRegistry()
            ?? throw new InvalidOperationException(
                "AddMediatorDistributed requires AddMediator to be called first.");

        var options = new DistributedQueueOptions();
        configure?.Invoke(options);

        // Register options as singleton for QueueMiddleware and QueueWorker to consume
        services.AddSingleton(options);

        var queueHandlers = registry.GetHandlersWithAttribute<QueueAttribute>();
        if (queueHandlers.Count == 0)
            return services;

        // Register IQueueClient if not already registered (default: in-memory)
        if (!services.Any(sd => sd.ServiceType == typeof(IQueueClient)))
            services.AddSingleton<IQueueClient, InMemoryQueueClient>();

        // Register the middleware
        services.AddTransient<QueueMiddleware>();

        // Register the worker registry and type resolver
        var workerRegistry = new QueueWorkerRegistry();
        services.AddSingleton<IQueueWorkerRegistry>(workerRegistry);
        var typeResolver = GetOrAddTypeResolver(services);

        // Track whether any handler uses progress tracking
        bool anyTrackProgress = false;

        // Collect queue names for startup initialization
        var infraOptions = GetOrAddInfrastructureOptions(services);

        // Register a QueueWorker for each [Queue]-decorated handler
        foreach (var handler in queueHandlers)
        {
            var messageType = handler.MessageType;
            if (messageType is null)
                continue;

            var queueAttr = handler.GetPreferredAttribute<QueueAttribute>()?.Attribute as QueueAttribute;
            var queueName = !string.IsNullOrWhiteSpace(queueAttr?.QueueName)
                ? queueAttr!.QueueName!
                : messageType.Name;

            // Register this message type in the type resolver for safe deserialization
            typeResolver.Register(messageType);

            // Apply group filtering
            var group = queueAttr?.Group;
            if (options.Group is not null && !string.Equals(options.Group, group, StringComparison.OrdinalIgnoreCase))
                continue;

            infraOptions.QueueNames.Add(queueName);
            infraOptions.QueueNames.Add($"{queueName}-dead-letter");

            var visibilityTimeout = TimeSpan.FromMinutes(5);
            if (!string.IsNullOrWhiteSpace(queueAttr?.Timeout) && TimeSpan.TryParse(queueAttr!.Timeout, out var parsed))
                visibilityTimeout = parsed;

            var retryDelay = TimeSpan.FromSeconds(5);
            if (!string.IsNullOrWhiteSpace(queueAttr?.RetryDelay) && TimeSpan.TryParse(queueAttr!.RetryDelay, out var parsedDelay))
                retryDelay = parsedDelay;

            var trackProgress = queueAttr?.TrackProgress ?? false;
            if (trackProgress)
                anyTrackProgress = true;

            var workerOptions = new QueueWorkerOptions
            {
                QueueName = queueName,
                MessageType = messageType,
                Registration = handler,
                Concurrency = queueAttr?.Concurrency ?? 1,
                PrefetchCount = queueAttr?.PrefetchCount ?? 1,
                VisibilityTimeout = visibilityTimeout,
                MaxRetries = queueAttr?.MaxRetries ?? 2,
                RetryPolicy = queueAttr?.RetryPolicy ?? QueueRetryPolicy.Exponential,
                RetryDelay = retryDelay,
                Group = group,
                AutoComplete = queueAttr?.AutoComplete ?? true,
                TrackProgress = trackProgress
            };

            // Create and register worker info for the dashboard
            var workerInfo = new QueueWorkerInfo
            {
                QueueName = queueName,
                MessageTypeName = messageType.FullName ?? messageType.Name,
                Concurrency = workerOptions.Concurrency,
                PrefetchCount = workerOptions.PrefetchCount,
                MaxRetries = workerOptions.MaxRetries,
                VisibilityTimeout = workerOptions.VisibilityTimeout,
                Group = workerOptions.Group,
                RetryPolicy = workerOptions.RetryPolicy,
                TrackProgress = workerOptions.TrackProgress
            };
            workerRegistry.Register(workerInfo);

            // Register as a hosted service using a factory so each worker gets its own options
            services.AddSingleton<IHostedService>(sp => new QueueWorker(
                sp.GetRequiredService<IQueueClient>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                workerOptions,
                sp.GetService<DistributedQueueOptions>(),
                sp.GetRequiredService<ILogger<QueueWorker>>(),
                workerInfo,
                sp.GetService<IQueueJobStateStore>(),
                sp.GetService<TimeProvider>()));
        }

        // Register default in-memory state store if any handler uses progress tracking and no store is registered
        if (anyTrackProgress && !services.Any(sd => sd.ServiceType == typeof(IQueueJobStateStore)))
            services.AddSingleton<IQueueJobStateStore, InMemoryQueueJobStateStore>();

        return services;
    }

    /// <summary>
    /// Adds distributed notification fan-out support to Foundatio.Mediator.
    /// Notifications implementing <see cref="IDistributedNotification"/> will be
    /// automatically published to a pub/sub client and re-published on all other nodes
    /// in the cluster.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback for <see cref="DistributedNotificationOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddMediator();
    /// services.AddMediatorDistributedNotifications();
    ///
    /// // Or with a custom transport:
    /// services.AddSingleton&lt;IPubSubClient, SnsSqsPubSubClient&gt;();
    /// services.AddMediatorDistributedNotifications(opts =>
    /// {
    ///     opts.Topic = "my-app-notifications";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddMediatorDistributedNotifications(
        this IServiceCollection services,
        Action<DistributedNotificationOptions>? configure = null)
    {
        // Prevent double registration
        if (services.Any(sd => sd.ServiceType == typeof(DistributedNotificationOptions)))
            return services;

        var options = new DistributedNotificationOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        // Register IPubSubClient if not already registered (default: in-memory)
        if (!services.Any(sd => sd.ServiceType == typeof(IPubSubClient)))
            services.AddSingleton<IPubSubClient, InMemoryPubSubClient>();

        // Collect topic name for startup initialization
        var infraOptions = GetOrAddInfrastructureOptions(services);
        infraOptions.TopicNames.Add(options.Topic);

        // Register the background worker
        services.AddSingleton<IHostedService>(sp => new DistributedNotificationWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IPubSubClient>(),
            sp.GetRequiredService<DistributedNotificationOptions>(),
            sp.GetRequiredService<ILogger<DistributedNotificationWorker>>(),
            sp.GetService<MessageTypeResolver>(),
            sp.GetService<TimeProvider>()));

        // Register known notification types in the type resolver
        var registry = services.GetHandlerRegistry();
        if (registry is not null)
        {
            var typeResolver = GetOrAddTypeResolver(services);
            foreach (var reg in registry.Registrations)
            {
                if (reg.MessageType is not null && typeof(IDistributedNotification).IsAssignableFrom(reg.MessageType))
                    typeResolver.Register(reg.MessageType);
            }
        }

        return services;
    }

    private static DistributedInfrastructureOptions GetOrAddInfrastructureOptions(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(DistributedInfrastructureOptions));
        if (descriptor?.ImplementationInstance is DistributedInfrastructureOptions existing)
            return existing;

        var infraOptions = new DistributedInfrastructureOptions();
        services.AddSingleton(infraOptions);

        // Register the initializer — runs before workers because it's registered first
        services.AddSingleton<IHostedService>(sp => new DistributedInfrastructureInitializer(
            sp.GetService<IQueueClient>(),
            sp.GetService<IPubSubClient>(),
            sp.GetRequiredService<DistributedInfrastructureOptions>(),
            sp.GetRequiredService<ILogger<DistributedInfrastructureInitializer>>()));

        return infraOptions;
    }

    private static MessageTypeResolver GetOrAddTypeResolver(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(MessageTypeResolver));
        if (descriptor?.ImplementationInstance is MessageTypeResolver existing)
            return existing;

        var resolver = new MessageTypeResolver();
        services.AddSingleton(resolver);
        return resolver;
    }
}
