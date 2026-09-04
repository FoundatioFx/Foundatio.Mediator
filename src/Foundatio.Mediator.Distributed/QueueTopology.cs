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
    /// <summary>
    /// The fully prefixed queue name.
    /// </summary>
    public required string QueueName { get; init; }

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
    public IReadOnlyList<HandlerRegistration> HandlersFor(Type messageType)
    {
        var result = new List<HandlerRegistration>(Handlers.Count);
        foreach (var handler in Handlers)
        {
            if (handler.MessageType is { } declared && declared.IsAssignableFrom(messageType))
                result.Add(handler);
        }

        return result;
    }

    /// <summary>
    /// The one handler whose middleware enqueues a message of <paramref name="messageType"/> when several
    /// handlers share this queue; the others return without sending so the queue receives one message.
    /// </summary>
    public string? DesignatedEnqueuerFor(Type messageType)
    {
        string? designated = null;
        foreach (var handler in HandlersFor(messageType))
        {
            if (designated is null || string.CompareOrdinal(handler.DescriptorId, designated) < 0)
                designated = handler.DescriptorId;
        }

        return designated;
    }
}
