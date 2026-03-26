using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SlimMessageBus.Host;
using SlimMessageBus.Host.Memory;

namespace Foundatio.Mediator.Queues;

/// <summary>
/// Extension methods for registering SlimMessageBus-backed queue support for Foundatio.Mediator.
/// </summary>
public static class QueueServiceExtensions
{
    private static readonly MethodInfo s_configureMethod = typeof(QueueServiceExtensions)
        .GetMethod(nameof(ConfigureQueueForType), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Adds queue processing support to Foundatio.Mediator using SlimMessageBus.
    /// Handlers decorated with <see cref="QueueAttribute"/> will have their messages
    /// enqueued via the message bus for asynchronous processing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureBus">
    /// Optional configuration callback for the <see cref="MessageBusBuilder"/>.
    /// Use this to set a transport provider (e.g. Kafka, RabbitMQ, Azure Service Bus).
    /// If not provided, defaults to the in-memory transport.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddMediator();
    /// services.AddMediatorQueues();
    ///
    /// // Or with a real transport:
    /// services.AddMediatorQueues(mbb => mbb.WithProviderServiceBus(cfg => { ... }));
    /// </code>
    /// </example>
    public static IServiceCollection AddMediatorQueues(
        this IServiceCollection services,
        Action<MessageBusBuilder>? configureBus = null)
    {
        // Prevent double registration
        if (services.Any(sd => sd.ServiceType == typeof(QueueMiddleware)))
            return services;

        var registry = services.GetHandlerRegistry()
            ?? throw new InvalidOperationException(
                "AddMediatorQueues requires AddMediator to be called first.");

        var queueHandlers = registry.GetHandlersWithAttribute<QueueAttribute>();
        if (queueHandlers.Count == 0)
            return services;

        // Register the middleware and generic consumer
        services.AddTransient<QueueMiddleware>();
        services.AddTransient(typeof(MediatorConsumer<>));

        services.AddSlimMessageBus(mbb =>
        {
            // Register produce/consume for each [Queue]-decorated handler
            foreach (var handler in queueHandlers)
            {
                var messageType = handler.MessageType;
                if (messageType == null)
                    continue;

                var queueAttr = handler.GetPreferredAttribute<QueueAttribute>()?.Attribute as QueueAttribute;
                var queueName = !string.IsNullOrWhiteSpace(queueAttr?.QueueName)
                    ? queueAttr!.QueueName!
                    : messageType.Name;
                var concurrency = queueAttr?.Concurrency ?? 1;

                s_configureMethod.MakeGenericMethod(messageType)
                    .Invoke(null, [mbb, queueName, concurrency]);
            }

            // Let the caller configure transport, serializer, etc.
            if (configureBus != null)
                configureBus(mbb);
            else
                mbb.WithProviderMemory(); // Default to in-memory
        });

        return services;
    }

    /// <summary>
    /// Strongly-typed helper invoked via reflection to register produce/consume
    /// for a specific message type with the <see cref="MessageBusBuilder"/>.
    /// </summary>
    private static void ConfigureQueueForType<T>(
        MessageBusBuilder mbb, string queueName, int concurrency) where T : class
    {
        mbb.Produce<T>(x => x.DefaultTopic(queueName));
        mbb.Consume<T>(x =>
        {
            x.Topic(queueName);
            x.WithConsumer<MediatorConsumer<T>>();
            x.Instances(concurrency);
        });
    }
}
