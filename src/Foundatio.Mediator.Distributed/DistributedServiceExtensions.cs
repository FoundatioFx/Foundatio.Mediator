using Foundatio;
using Foundatio.Serializer;
using Foundatio.Messaging;
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
    /// the infrastructure initializer. Configure the native Foundatio message bus before starting
    /// the host, for example with <c>AddFoundatio().Messaging.UseInMemory()</c>.
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

        var defaults = builder.GetDistributedOptions();
        var options = new DistributedQueueOptions { ResourcePrefix = defaults.ResourcePrefix };
        configure?.Invoke(options);
        if (options.ReceiveBatchDelay < TimeSpan.Zero || options.ReceiveBatchDelay > TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(options.ReceiveBatchDelay), "Receive batch delay must be between zero and one second.");
        services.AddSingleton(options);

        var topology = new QueueTopology();
        services.AddSingleton(topology);

        var queueHandlers = registry.GetHandlersWithAttribute<QueueAttribute>();
        if (queueHandlers.Count == 0)
        {
            options.Workers.Validate([]);
            return builder;
        }

        services.TryAddSingleton<QueueMiddleware>();
        services.TryAddSingleton<QueueLockMiddleware>();

        var workerRegistry = new QueueWorkerRegistry();
        services.AddSingleton<IQueueWorkerRegistry>(workerRegistry);
        var infraOptions = GetOrAddInfrastructureOptions(services);

        var queues = new Dictionary<string, List<(HandlerRegistration Handler, QueueAttribute Settings)>>(StringComparer.OrdinalIgnoreCase);
        var queueOrder = new List<string>();

        foreach (var handler in queueHandlers)
        {
            ValidateReturnType(handler);
            var messageType = handler.MessageType;
            if (messageType is null)
                continue;

            var settings = handler.GetPreferredAttribute<QueueAttribute>()?.Attribute as QueueAttribute ?? new QueueAttribute();
            var logicalName = !string.IsNullOrWhiteSpace(settings.QueueName) ? settings.QueueName! : GetDefaultQueueName(handler, messageType);
            if (options.QueueOverrides.TryGetValue(logicalName, out var configureQueue))
                configureQueue(settings);
            if (!string.IsNullOrWhiteSpace(settings.QueueName) && settings.QueueName != logicalName)
                throw new InvalidOperationException($"Queue override '{logicalName}' cannot change QueueName. Configure subscription identity on the handler.");
            var queueName = options.ApplyPrefix(logicalName);

            RegisterMessageType(services, messageType);

            if (!queues.TryGetValue(queueName, out var list))
            {
                list = [];
                queues[queueName] = list;
                queueOrder.Add(queueName);
            }

            if (list.Count > 0 && (string.IsNullOrWhiteSpace(settings.QueueName) || list.Any(member => string.IsNullOrWhiteSpace(member.Settings.QueueName))))
                throw new InvalidOperationException($"Independent subscriptions collide at '{queueName}'. Set distinct QueueName values, or explicitly name the same queue on every handler to share execution and retries.");
            list.Add((handler, settings));
        }

        options.Workers.Validate(queues.SelectMany(queue => new[] { options.RemovePrefix(queue.Key), queue.Value[0].Settings.Group }).OfType<string>());
        var unknownOverrides = options.QueueOverrides.Keys.Except(queues.Keys.Select(options.RemovePrefix), StringComparer.OrdinalIgnoreCase).ToArray();
        if (unknownOverrides.Length > 0)
            throw new InvalidOperationException($"Unknown queue overrides: {string.Join(", ", unknownOverrides)}. Available subscriptions: {string.Join(", ", queues.Keys.Select(options.RemovePrefix))}.");

        foreach (var queueName in queueOrder)
        {
            var members = queues[queueName];
            var settings = members[0].Settings;
            ValidateQueueSettings(queueName, members);
            var displayNames = members.Select(m => m.Settings.DisplayName?.Trim())
                .Where(name => !string.IsNullOrEmpty(name)).Distinct(StringComparer.Ordinal).ToArray();
            if (displayNames.Length > 1)
                throw new InvalidOperationException($"Queue '{queueName}' has conflicting DisplayName values. Set the same label on its handlers or use QueueOverrides to configure it once.");
            var displayName = displayNames.FirstOrDefault();

            var handlers = HandlerRegistry.OrderRegistrations(members.Select(m => m.Handler));
            var messageType = members[0].Handler.MessageType!;

            var retrySchedule = !string.IsNullOrWhiteSpace(settings.RetryDelays) ? QueueRetryDelay.ParseSchedule(settings.RetryDelays!) : null;
            var retryPolicy = retrySchedule is not null ? QueueRetryPolicy.Schedule : settings.RetryPolicy;

            var registration = new QueueRegistration
            {
                QueueName = queueName,
                DisplayName = displayName,
                Settings = settings,
                MessageType = messageType,
                Handlers = handlers
            };
            topology.Add(registration);
            registry.SetPublishGroup($"queue:{queueName}", handlers);

            var visibilityTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

            // Queues must exist for enqueue-only nodes too; dead-letter queues are provisioned alongside.
            infraOptions.QueueNames.Add(DestinationAddress.ForQueue(queueName));


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
                DisplayName = displayName,
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

            if (!options.Workers.Includes(options.RemovePrefix(queueName), settings.Group))
                continue;

            registration.WorkerRunsHere = true;
            workerInfo.Stats.SetWorkerRegistered(true);

            services.AddSingleton<IHostedService>(sp => new QueueWorker(
                sp.GetRequiredService<IMessageBus>(),
                sp.GetRequiredService<IMessageTypeRegistry>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                workerOptions,
                sp.GetService<DistributedQueueOptions>(),
                sp.GetRequiredService<ILogger<QueueWorker>>(),
                workerInfo,
                sp.GetService<IMessageExecutionStore>(),
                sp.GetService<DistributedInfrastructureReady>(),
                sp.GetService<TimeProvider>(),
                sp.GetService<ISerializer>()));
        }



        services.AddSingleton<IHostedService, DistributedConfigurationValidator>();
        services.AddSingleton<IHostedService>(sp => new QueueDepthMetricsService(
            sp.GetRequiredService<IMessageTransport>(),
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
    public static IMediatorBuilder AddQueueHeaderProvider<TProvider>(this IMediatorBuilder builder, ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TProvider : class, IQueueHeaderProvider
    {
        builder.Services.Add(new ServiceDescriptor(typeof(IQueueHeaderProvider), typeof(TProvider), lifetime));
        return builder;
    }

    private static void ValidateReturnType(HandlerRegistration handler)
    {
        var type = handler.HandlerMethod?.ReturnType
            ?? throw new InvalidOperationException($"Cannot resolve the return type for queue handler '{handler.DescriptorId}'. Rebuild with source-handler metadata enabled.");
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
            type = type.GetGenericArguments()[0];
        if (type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask))
            return;
        if (type.IsGenericType && type.FullName!.StartsWith("System.ValueTuple`", StringComparison.Ordinal))
            type = type.GetGenericArguments()[0];
        if (type == typeof(Result) || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>))
            return;
        throw new InvalidOperationException($"Queue handler '{handler.SourceHandlerName}.{handler.MethodName}' returns '{handler.HandlerMethod!.ReturnType}'. Queue handlers must return void, Task, ValueTask, Result, Result<T>, or a cascading tuple with Result/Result<T> first (optionally awaited). Enqueueing returns acceptance; the worker produces the eventual result.");
    }

    private static string GetDefaultQueueName(HandlerRegistration handler, Type messageType)
    {
        var name = handler.SourceHandlerName ?? handler.SourceHandlerType?.Name
            ?? throw new InvalidOperationException($"Queue handler '{handler.DescriptorId}' needs source-handler metadata or an explicit QueueName.");
        foreach (var suffix in new[] { "Handler", "Consumer" })
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
            {
                name = name[..^suffix.Length];
                break;
            }
        }
        return name == messageType.Name ? name : $"{name}-{messageType.Name}";
    }

    private static void ValidateQueueSettings(string queueName, List<(HandlerRegistration Handler, QueueAttribute Settings)> members)
    {
        var first = members[0].Settings;

        if (first.Concurrency < 1 || first.PrefetchCount < 0 || first.RetryDelaySeconds < 0)
            throw new InvalidOperationException($"Queue '{queueName}': Concurrency must be positive; PrefetchCount and RetryDelaySeconds cannot be negative.");
        if (!Enum.IsDefined(first.RetryPolicy))
            throw new InvalidOperationException($"Queue '{queueName}': invalid RetryPolicy.");
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
    /// are published to the <see cref="IMessageBus"/> and re-published locally on every other node.
    /// Configure the native Foundatio message bus before starting the host.
    /// </summary>
    public static IMediatorBuilder AddDistributedNotifications(
        this IMediatorBuilder builder,
        Action<DistributedNotificationOptions>? configure = null)
    {
        var services = builder.Services;

        if (services.Any(sd => sd.ServiceType == typeof(DistributedNotificationOptions)))
            return builder;

        var defaults = builder.GetDistributedOptions();
        var options = new DistributedNotificationOptions { ResourcePrefix = defaults.ResourcePrefix };
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxConcurrentPublishes, 1);

        services.AddSingleton(options);

        var infraOptions = GetOrAddInfrastructureOptions(services);
        infraOptions.TopicNames.Add(DestinationAddress.ForTopic(options.EffectiveTopic));

        var distributedTypes = new HashSet<Type>();
        var registry = services.GetHandlerRegistry();
        if (registry is not null)
        {
            foreach (var reg in registry.Registrations)
            {
                if (reg.MessageType is not null && options.ShouldDistribute(reg.MessageType))
                {
                    RegisterMessageType(services, reg.MessageType);
                    distributedTypes.Add(reg.MessageType);
                }
            }
        }

        foreach (var type in options.IncludedTypes)
        {
            if (options.ShouldDistribute(type))
            {
                RegisterMessageType(services, type);
                distributedTypes.Add(type);
            }
        }

        options.ResolvedTypes = distributedTypes.OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();

        services.AddSingleton<IHostedService>(sp => new DistributedNotificationWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<DistributedNotificationOptions>(),
            sp.GetRequiredService<ILogger<DistributedNotificationWorker>>(),
            sp.GetRequiredService<IMessageTypeRegistry>(),
            sp.GetService<DistributedInfrastructureReady>(),
            sp.GetService<TimeProvider>(),
            sp.GetService<ISerializer>()));

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
            sp.GetRequiredService<IMessageTransport>(),
            sp.GetService<MessagingTopologyOptions>(),
            sp.GetRequiredService<DistributedInfrastructureOptions>(),
            sp.GetRequiredService<DistributedInfrastructureReady>(),
            sp.GetRequiredService<ILogger<DistributedInfrastructureInitializer>>()));

        return infraOptions;
    }

    private static void RegisterMessageType(IServiceCollection services, Type type)
    {
        if (type.IsInterface || type.IsAbstract) return;
        if (!services.Any(descriptor => descriptor.ImplementationInstance is MessageTypeRegistration registration && registration.MessageType == type))
            services.AddSingleton(new MessageTypeRegistration(type.FullName ?? type.Name, type));
    }

    private sealed class DistributedQueuesMarker;
}
