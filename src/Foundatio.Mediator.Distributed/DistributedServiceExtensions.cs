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
    /// <param name="builder">The mediator builder.</param>
    /// <param name="configure">Optional configuration callback for <see cref="DistributedQueueOptions"/>.</param>
    /// <returns>The mediator builder for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddMediator()
    ///     .AddDistributedQueues();
    ///
    /// // Or with options:
    /// services.AddMediator()
    ///     .AddDistributedQueues(opts => opts.Group = "order-processing");
    /// </code>
    /// </example>
    public static IMediatorBuilder AddDistributedQueues(
        this IMediatorBuilder builder,
        Action<DistributedQueueOptions>? configure = null)
    {
        var services = builder.Services;

        // Prevent double registration
        if (services.Any(sd => sd.ServiceType == typeof(QueueMiddleware)))
            return builder;

        var registry = services.GetHandlerRegistry()
            ?? throw new InvalidOperationException(
                "AddDistributedQueues requires AddMediator to be called first.");

        var options = new DistributedQueueOptions();
        configure?.Invoke(options);

        // Register options as singleton for QueueMiddleware and QueueWorker to consume
        services.AddSingleton(options);

        var queueHandlers = registry.GetHandlersWithAttribute<QueueAttribute>();
        if (queueHandlers.Count == 0)
            return builder;

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

        // Track which queue names already have a worker registered to avoid duplicates.
        // Multiple handlers for the same message type share a single queue and worker.
        var registeredQueues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            // Apply resource prefix for app-level scoping (e.g., "myapp-CreateOrder")
            queueName = options.ApplyPrefix(queueName);

            // Register this message type in the type resolver for safe deserialization
            typeResolver.Register(messageType);

            // Skip if already processed this queue name.
            // Multiple handlers for the same message type (e.g., AuditEventHandler and
            // NotificationEventHandler both handling OrderCreated) share one queue worker.
            if (!registeredQueues.Add(queueName))
                continue;

            var group = queueAttr?.Group;

            // Always register infrastructure (queues must exist for enqueuing from API-only nodes).
            // Dead-letter queues are created lazily on first dead-letter to reduce startup latency.
            infraOptions.QueueNames.Add(queueName);

            var visibilityTimeout = TimeSpan.FromSeconds(queueAttr?.TimeoutSeconds ?? 30);
            var retryDelay = TimeSpan.FromSeconds(queueAttr?.RetryDelaySeconds ?? 5);

            var trackProgress = queueAttr?.TrackProgress ?? false;
            if (trackProgress)
                anyTrackProgress = true;

            var concurrency = queueAttr?.Concurrency ?? 1;
            var prefetchCount = queueAttr?.PrefetchCount ?? 0;
            // Auto-scale prefetch to match concurrency when not explicitly set.
            // This ensures each ReceiveAsync call can fill the consumer pipeline in a
            // single round-trip, which is critical for fair distribution across nodes.
            if (prefetchCount <= 0)
                prefetchCount = concurrency;

            var workerOptions = new QueueWorkerOptions
            {
                QueueName = queueName,
                MessageType = messageType,
                Registration = handler,
                Concurrency = concurrency,
                PrefetchCount = prefetchCount,
                VisibilityTimeout = visibilityTimeout,
                MaxAttempts = queueAttr?.MaxAttempts ?? 3,
                RetryPolicy = queueAttr?.RetryPolicy ?? QueueRetryPolicy.Exponential,
                RetryDelay = retryDelay,
                Group = group,
                AutoComplete = queueAttr?.AutoComplete ?? true,
                AutoRenewTimeout = queueAttr?.AutoRenewTimeout ?? true,
                TrackProgress = trackProgress
            };

            // Always register worker info for dashboard visibility across all nodes
            var workerInfo = new QueueWorkerInfo
            {
                QueueName = queueName,
                MessageTypeName = messageType.FullName ?? messageType.Name,
                Concurrency = workerOptions.Concurrency,
                PrefetchCount = workerOptions.PrefetchCount,
                MaxAttempts = workerOptions.MaxAttempts,
                VisibilityTimeout = workerOptions.VisibilityTimeout,
                Group = workerOptions.Group,
                RetryPolicy = workerOptions.RetryPolicy,
                TrackProgress = workerOptions.TrackProgress,
                Description = queueAttr?.Description
            };
            workerRegistry.Register(workerInfo);

            // Determine whether this worker should start in this process.
            // Workers are skipped when:
            // - WorkersEnabled is false (API-only nodes that only enqueue)
            // - Group filter is set and doesn't match the handler's group
            // - Queues filter is set and neither the queue name nor group name matches
            if (!options.WorkersEnabled)
                continue;

            if (options.Group is not null && !string.Equals(options.Group, group, StringComparison.OrdinalIgnoreCase))
                continue;

            if (options.Queues is { Count: > 0 } queues
                && !queues.Contains(queueName, StringComparer.OrdinalIgnoreCase)
                && (group is null || !queues.Contains(group, StringComparer.OrdinalIgnoreCase)))
                continue;

            // Mark that a worker is actually running on this node
            workerInfo.WorkerRegistered = true;

            // Register as a hosted service using a factory so each worker gets its own options
            services.AddSingleton<IHostedService>(sp => new QueueWorker(
                sp.GetRequiredService<IQueueClient>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                workerOptions,
                sp.GetService<DistributedQueueOptions>(),
                sp.GetRequiredService<ILogger<QueueWorker>>(),
                workerInfo,
                sp.GetService<IQueueJobStateStore>(),
                sp.GetService<DistributedInfrastructureReady>(),
                sp.GetService<TimeProvider>()));
        }

        // Register default in-memory state store if any handler uses progress tracking and no store is registered
        if (anyTrackProgress && !services.Any(sd => sd.ServiceType == typeof(IQueueJobStateStore)))
            services.AddSingleton<IQueueJobStateStore, InMemoryQueueJobStateStore>();

        return builder;
    }

    /// <summary>
    /// Adds distributed notification fan-out support to Foundatio.Mediator.
    /// Notifications are distributed when they implement <see cref="IDistributedNotification"/>,
    /// are decorated with <see cref="DistributedNotificationAttribute"/>, are explicitly included
    /// via <see cref="DistributedNotificationOptions.Include{T}"/>, match
    /// <see cref="DistributedNotificationOptions.MessageFilter"/>, or when
    /// <see cref="DistributedNotificationOptions.IncludeAllNotifications"/> is enabled.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <param name="configure">Optional configuration callback for <see cref="DistributedNotificationOptions"/>.</param>
    /// <returns>The mediator builder for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddMediator()
    ///     .AddDistributedNotifications();
    ///
    /// // Or with options:
    /// services.AddMediator()
    ///     .AddDistributedNotifications(opts =>
    ///     {
    ///         opts.Topic = "my-app-notifications";
    ///         opts.Include&lt;OrderCreated&gt;();
    ///     });
    /// </code>
    /// </example>
    public static IMediatorBuilder AddDistributedNotifications(
        this IMediatorBuilder builder,
        Action<DistributedNotificationOptions>? configure = null)
    {
        var services = builder.Services;

        // Prevent double registration
        if (services.Any(sd => sd.ServiceType == typeof(DistributedNotificationOptions)))
            return builder;

        var options = new DistributedNotificationOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        // Register IPubSubClient if not already registered (default: in-memory)
        if (!services.Any(sd => sd.ServiceType == typeof(IPubSubClient)))
            services.AddSingleton<IPubSubClient, InMemoryPubSubClient>();

        // Collect topic name for startup initialization
        var infraOptions = GetOrAddInfrastructureOptions(services);
        infraOptions.TopicNames.Add(options.EffectiveTopic);

        // Register the background worker
        // Build the resolved set of distributed types for the worker to filter on.
        var distributedTypes = new HashSet<Type>();

        // Register known notification types in the type resolver
        var registry = services.GetHandlerRegistry();
        if (registry is not null)
        {
            var typeResolver = GetOrAddTypeResolver(services);
            foreach (var reg in registry.Registrations)
            {
                if (reg.MessageType is not null && options.ShouldDistribute(reg.MessageType))
                {
                    typeResolver.Register(reg.MessageType);
                    distributedTypes.Add(reg.MessageType);
                }
            }
        }

        // Also register explicitly included types that may not have handlers in the registry
        // (e.g., types only consumed on other nodes)
        if (distributedTypes.Count == 0 && options.IncludedTypes.Count == 0
            && !options.IncludeAllNotifications && options.MessageFilter is null)
        {
            // No distributed types discovered and no dynamic filters — skip the worker entirely
            return builder;
        }

        foreach (var type in options.IncludedTypes)
        {
            var typeResolver2 = GetOrAddTypeResolver(services);
            typeResolver2.Register(type);
            distributedTypes.Add(type);
        }

        services.AddSingleton<IHostedService>(sp => new DistributedNotificationWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IPubSubClient>(),
            sp.GetRequiredService<DistributedNotificationOptions>(),
            sp.GetRequiredService<ILogger<DistributedNotificationWorker>>(),
            sp.GetService<MessageTypeResolver>(),
            sp.GetService<DistributedInfrastructureReady>(),
            sp.GetService<TimeProvider>()));

        return builder;
    }

    private static DistributedInfrastructureOptions GetOrAddInfrastructureOptions(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(DistributedInfrastructureOptions));
        if (descriptor?.ImplementationInstance is DistributedInfrastructureOptions existing)
            return existing;

        var infraOptions = new DistributedInfrastructureOptions();
        services.AddSingleton(infraOptions);

        var ready = new DistributedInfrastructureReady();
        services.AddSingleton(ready);

        // Register the initializer — starts infrastructure creation in the background
        services.AddSingleton<IHostedService>(sp => new DistributedInfrastructureInitializer(
            sp.GetService<IQueueClient>(),
            sp.GetService<IPubSubClient>(),
            sp.GetRequiredService<DistributedInfrastructureOptions>(),
            sp.GetRequiredService<DistributedInfrastructureReady>(),
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
