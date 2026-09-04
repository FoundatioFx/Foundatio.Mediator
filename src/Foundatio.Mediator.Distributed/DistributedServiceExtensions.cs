using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Registration for distributed queues and notifications.
/// </summary>
public static class DistributedServiceExtensions
{
    /// <summary>
    /// Registers queue routing for every <see cref="QueueAttribute"/> handler: the middleware that
    /// enqueues, one worker per queue (subject to <see cref="DistributedQueueOptions"/> filters), and
    /// the infrastructure initializer. Register a transport before calling this; otherwise the
    /// in-memory queue client is used.
    /// </summary>
    public static IMediatorBuilder AddDistributedQueues(
        this IMediatorBuilder builder,
        Action<DistributedQueueOptions>? configure = null)
    {
        var services = builder.Services;

        if (services.Any(sd => sd.ServiceType == typeof(DistributedQueuesMarker)))
            return builder;
        services.AddSingleton<DistributedQueuesMarker>();

        var registry = services.GetHandlerRegistry()
            ?? throw new InvalidOperationException("AddDistributedQueues requires AddMediator to be called first.");

        var options = new DistributedQueueOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        var topology = new QueueTopology();
        services.AddSingleton(topology);

        var queueHandlers = registry.GetHandlersWithAttribute<QueueAttribute>();
        if (queueHandlers.Count == 0)
            return builder;

        var clientDescriptor = services.LastOrDefault(sd => sd.ServiceType == typeof(IQueueClient));
        bool usingInMemoryClient = clientDescriptor is null
            || clientDescriptor.ImplementationInstance is InMemoryQueueClient
            || clientDescriptor.ImplementationType == typeof(InMemoryQueueClient);
        if (clientDescriptor is null)
            services.AddSingleton<IQueueClient, InMemoryQueueClient>();

        // An explicitly registered in-memory client is a deliberate choice; only the silent default is a trap.
        if (clientDescriptor is null && !options.Workers.IsAll && !options.AllowInMemoryWithoutWorkers)
        {
            throw new InvalidOperationException(
                "Workers are disabled or filtered in this process but no IQueueClient transport is registered, so enqueued messages " +
                "would go to an in-memory queue nothing consumes. Register a transport (for example UseAws()) before AddDistributedQueues(), " +
                "or set DistributedQueueOptions.AllowInMemoryWithoutWorkers for tests.");
        }

        services.TryAddSingleton<QueueMiddleware>();
        services.TryAddSingleton<QueueLockMiddleware>();

        // A process-local lock is only safe when the queue is process-local too; with a real transport the
        // middleware fails the first locked message with a clear error until a provider is registered.
        if (usingInMemoryClient && !services.Any(sd => sd.ServiceType == typeof(IQueueLockProvider)))
            services.AddSingleton<IQueueLockProvider, InMemoryQueueLockProvider>();

        var workerRegistry = new QueueWorkerRegistry();
        services.AddSingleton<IQueueWorkerRegistry>(workerRegistry);
        var typeResolver = GetOrAddTypeResolver(services);
        var infraOptions = GetOrAddInfrastructureOptions(services);

        var queues = new Dictionary<string, List<(HandlerRegistration Handler, QueueAttribute Settings)>>(StringComparer.OrdinalIgnoreCase);
        var queueOrder = new List<string>();

        foreach (var handler in queueHandlers)
        {
            var messageType = handler.MessageType;
            if (messageType is null)
                continue;

            var settings = handler.GetPreferredAttribute<QueueAttribute>()?.Attribute as QueueAttribute ?? new QueueAttribute();
            var queueName = options.ApplyPrefix(!string.IsNullOrWhiteSpace(settings.QueueName) ? settings.QueueName! : messageType.Name);

            typeResolver.Register(messageType);

            if (!queues.TryGetValue(queueName, out var list))
            {
                list = [];
                queues[queueName] = list;
                queueOrder.Add(queueName);
            }

            list.Add((handler, settings));
        }

        bool anyTrackProgress = false;

        foreach (var queueName in queueOrder)
        {
            var members = queues[queueName];
            var settings = members[0].Settings;
            ValidateQueueSettings(queueName, members);

            var handlers = members.Select(m => m.Handler).ToList();
            var messageType = members[0].Handler.MessageType!;

            var retrySchedule = !string.IsNullOrWhiteSpace(settings.RetryDelays) ? QueueRetryDelay.ParseSchedule(settings.RetryDelays!) : null;
            var retryPolicy = retrySchedule is not null ? QueueRetryPolicy.Schedule : settings.RetryPolicy;

            var registration = new QueueRegistration
            {
                QueueName = queueName,
                Settings = settings,
                MessageType = messageType,
                Handlers = handlers
            };
            topology.Add(registration);

            var visibilityTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

            // Queues must exist for enqueue-only nodes too; dead-letter queues are provisioned alongside.
            infraOptions.QueueNames.Add(new QueueDefinition
            {
                Name = queueName,
                VisibilityTimeout = visibilityTimeout,
                MaxAttempts = settings.MaxAttempts
            });

            if (settings.TrackProgress)
                anyTrackProgress = true;

            var concurrency = Math.Max(1, settings.Concurrency);
            var prefetchCount = settings.PrefetchCount > 0 ? settings.PrefetchCount : concurrency;

            var workerOptions = new QueueWorkerOptions
            {
                QueueName = queueName,
                MessageType = messageType,
                Registrations = handlers,
                Concurrency = concurrency,
                PrefetchCount = prefetchCount,
                VisibilityTimeout = visibilityTimeout,
                MaxAttempts = settings.MaxAttempts,
                RetryPolicy = retryPolicy,
                RetryDelay = TimeSpan.FromSeconds(settings.RetryDelaySeconds),
                RetrySchedule = retrySchedule,
                Group = settings.Group,
                AutoComplete = settings.AutoComplete,
                AutoRenewTimeout = settings.AutoRenewTimeout,
                TrackProgress = settings.TrackProgress
            };

            // Worker info is registered on every node so dashboards can list queues from an API-only node.
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
                Description = settings.Description
            };
            workerRegistry.Register(workerInfo);

            if (!options.Workers.Includes(queueName, settings.Group))
                continue;

            registration.WorkerRunsHere = true;
            workerInfo.Stats.SetWorkerRegistered(true);

            services.AddSingleton<IHostedService>(sp => new QueueWorker(
                sp.GetRequiredService<IQueueClient>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                workerOptions,
                sp.GetService<DistributedQueueOptions>(),
                sp.GetRequiredService<ILogger<QueueWorker>>(),
                workerInfo,
                sp.GetService<IQueueJobStateStore>(),
                sp.GetService<DistributedInfrastructureReady>(),
                sp.GetService<TimeProvider>(),
                sp.GetService<MessageTypeResolver>(),
                sp.GetServices<IQueueHeaderProvider>()));
        }

        if (anyTrackProgress && !services.Any(sd => sd.ServiceType == typeof(IQueueJobStateStore)))
            services.AddSingleton<IQueueJobStateStore, InMemoryQueueJobStateStore>();

        services.AddSingleton<IHostedService>(sp => new QueueDepthMetricsService(
            sp.GetRequiredService<IQueueClient>(),
            sp.GetRequiredService<QueueTopology>(),
            sp.GetRequiredService<DistributedQueueOptions>(),
            sp.GetRequiredService<ILogger<QueueDepthMetricsService>>(),
            sp.GetService<DistributedInfrastructureReady>(),
            sp.GetService<TimeProvider>()));

        return builder;
    }

    /// <summary>
    /// Registers a header provider that enriches queued messages on enqueue and restores context on the worker.
    /// </summary>
    public static IMediatorBuilder AddQueueHeaderProvider<TProvider>(this IMediatorBuilder builder)
        where TProvider : class, IQueueHeaderProvider
    {
        builder.Services.AddSingleton<IQueueHeaderProvider, TProvider>();
        return builder;
    }

    private static void ValidateQueueSettings(string queueName, List<(HandlerRegistration Handler, QueueAttribute Settings)> members)
    {
        var first = members[0].Settings;

        if (first.MaxAttempts == 0)
            throw new InvalidOperationException($"Queue '{queueName}': MaxAttempts must be at least 1 (or negative for unlimited).");

        if (first.TimeoutSeconds < 1 || first.TimeoutSeconds > 12 * 60 * 60)
            throw new InvalidOperationException($"Queue '{queueName}': TimeoutSeconds must be between 1 and 43200 (12 hours).");

        for (int i = 1; i < members.Count; i++)
        {
            var other = members[i].Settings;
            var differences = new List<string>();

            if (first.MaxAttempts != other.MaxAttempts) differences.Add(nameof(QueueAttribute.MaxAttempts));
            if (first.TimeoutSeconds != other.TimeoutSeconds) differences.Add(nameof(QueueAttribute.TimeoutSeconds));
            if (first.Concurrency != other.Concurrency) differences.Add(nameof(QueueAttribute.Concurrency));
            if (first.PrefetchCount != other.PrefetchCount) differences.Add(nameof(QueueAttribute.PrefetchCount));
            if (!string.Equals(first.Group, other.Group, StringComparison.OrdinalIgnoreCase)) differences.Add(nameof(QueueAttribute.Group));
            if (first.AutoComplete != other.AutoComplete) differences.Add(nameof(QueueAttribute.AutoComplete));
            if (first.AutoRenewTimeout != other.AutoRenewTimeout) differences.Add(nameof(QueueAttribute.AutoRenewTimeout));
            if (first.RetryPolicy != other.RetryPolicy) differences.Add(nameof(QueueAttribute.RetryPolicy));
            if (first.RetryDelaySeconds != other.RetryDelaySeconds) differences.Add(nameof(QueueAttribute.RetryDelaySeconds));
            if (!string.Equals(first.RetryDelays, other.RetryDelays, StringComparison.Ordinal)) differences.Add(nameof(QueueAttribute.RetryDelays));
            if (first.TrackProgress != other.TrackProgress) differences.Add(nameof(QueueAttribute.TrackProgress));

            if (differences.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Queue '{queueName}' is shared by handlers '{members[0].Handler.SourceHandlerName ?? members[0].Handler.DescriptorId}' and " +
                    $"'{members[i].Handler.SourceHandlerName ?? members[i].Handler.DescriptorId}' with different [Queue] settings ({string.Join(", ", differences)}). " +
                    "Handlers on the same queue must declare identical settings.");
            }
        }
    }

    /// <summary>
    /// Bridges notifications across processes. Types selected by <see cref="DistributedNotificationOptions"/>
    /// are published to the <see cref="IPubSubClient"/> and re-published locally on every other node.
    /// Register a transport before calling this; otherwise the in-memory pub/sub client is used.
    /// </summary>
    public static IMediatorBuilder AddDistributedNotifications(
        this IMediatorBuilder builder,
        Action<DistributedNotificationOptions>? configure = null)
    {
        var services = builder.Services;

        if (services.Any(sd => sd.ServiceType == typeof(DistributedNotificationOptions)))
            return builder;

        var options = new DistributedNotificationOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        if (!services.Any(sd => sd.ServiceType == typeof(IPubSubClient)))
            services.AddSingleton<IPubSubClient, InMemoryPubSubClient>();

        var infraOptions = GetOrAddInfrastructureOptions(services);
        infraOptions.TopicNames.Add(new TopicDefinition { Name = options.EffectiveTopic });

        var distributedTypes = new HashSet<Type>();
        var registry = services.GetHandlerRegistry();
        var typeResolver = GetOrAddTypeResolver(services);
        if (registry is not null)
        {
            foreach (var reg in registry.Registrations)
            {
                if (reg.MessageType is not null && options.ShouldDistribute(reg.MessageType))
                {
                    typeResolver.Register(reg.MessageType);
                    distributedTypes.Add(reg.MessageType);
                }
            }
        }

        foreach (var type in options.IncludedTypes)
        {
            if (options.ShouldDistribute(type))
            {
                typeResolver.Register(type);
                distributedTypes.Add(type);
            }
        }

        if (distributedTypes.Count == 0 && !options.HasDynamicRules)
            return builder;

        options.ResolvedTypes = distributedTypes.OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();

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

    private sealed class DistributedQueuesMarker;
}
