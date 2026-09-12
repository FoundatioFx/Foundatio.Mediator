using System.Collections.Concurrent;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// The set of queues discovered from <see cref="QueueAttribute"/> handlers at registration time,
/// shared by <see cref="QueueMiddleware"/> and the workers.
/// </summary>
public sealed class QueueTopology
{
    private readonly Dictionary<string, QueueRegistration> _byQueueName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, QueueRegistration> _byDescriptorId = new(StringComparer.Ordinal);
    private readonly List<QueueRegistration> _queues = [];
    private readonly ConcurrentDictionary<Type, HandlerRegistration> _enqueueHandlers = new();

    internal HandlerRegistration GetEnqueueHandler(Type messageType, HandlerRegistry registry)
        => _enqueueHandlers.GetOrAdd(messageType, static (type, state) =>
        {
            var handlers = state.Registry.GetRegistrationsForMessageType(type);
            if (handlers.Count != 1)
                throw new InvalidOperationException($"EnqueueAsync requires exactly one handler for '{type.Name}'; found {handlers.Count}. Use PublishAsync for multiple subscriptions.");
            var handler = handlers[0];
            if (state.Topology.GetByDescriptorId(handler.DescriptorId) is null)
                throw new InvalidOperationException($"Handler '{handler.SourceHandlerName}' is not a queue handler.");
            return handler;
        }, (Topology: this, Registry: registry));

    /// <summary>
    /// Every queue, in registration order.
    /// </summary>
    public IReadOnlyList<QueueRegistration> Queues => _queues;

    /// <summary>
    /// Finds a queue by its fully prefixed name.
    /// </summary>
    public QueueRegistration? GetByQueueName(string queueName)
        => _byQueueName.GetValueOrDefault(queueName);

    /// <summary>
    /// Finds the queue a handler registration belongs to.
    /// </summary>
    public QueueRegistration? GetByDescriptorId(string descriptorId)
        => _byDescriptorId.GetValueOrDefault(descriptorId);

    internal void Add(QueueRegistration registration)
    {
        _queues.Add(registration);
        _byQueueName[registration.QueueName] = registration;
        foreach (var handler in registration.Handlers)
            _byDescriptorId[handler.DescriptorId] = registration;
    }
}

/// <summary>
/// A queue and the handlers bound to it.
/// </summary>
public sealed class QueueRegistration
{
    private readonly ConcurrentDictionary<Type, IReadOnlyList<HandlerRegistration>> _matchingHandlers = new();
    internal HandlerRegistry? Registry { get; init; }

    /// <summary>
    /// The fully prefixed queue name.
    /// </summary>
    public required string QueueName { get; init; }

    /// <summary>The configured display label, or <c>null</c> to display <see cref="QueueName"/>.</summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// The effective settings shared by every handler on the queue.
    /// </summary>
    public required QueueAttribute Settings { get; init; }

    /// <summary>
    /// The declared message type of the handlers, used as the deserialization fallback.
    /// </summary>
    public required Type MessageType { get; init; }

    /// <summary>
    /// Handler registrations bound to this queue.
    /// </summary>
    public required IReadOnlyList<HandlerRegistration> Handlers { get; init; }

    /// <summary>
    /// Whether a worker for this queue runs in the current process.
    /// </summary>
    public bool WorkerRunsHere { get; internal set; }

    /// <summary>
    /// Handlers that accept <paramref name="messageType"/>, including handlers declared on an interface
    /// or base type of it.
    /// </summary>
    public IReadOnlyList<HandlerRegistration> HandlersFor(Type messageType) => _matchingHandlers.GetOrAdd(messageType, static (type, self) =>
    {
        var matching = self.Handlers.Where(handler => handler.MessageType?.IsAssignableFrom(type) == true).ToArray();
        if (self.Registry is null || matching.Length < 2)
            return matching;

        // Reuse Mediator's existing publication order without changing its registry.
        var byDelegate = matching.ToDictionary(handler => handler.PublishAsync);
        return self.Registry.GetPublishHandlersForType(type)
            .Where(byDelegate.ContainsKey).Select(publish => byDelegate[publish]).ToArray();
    }, this);
}
