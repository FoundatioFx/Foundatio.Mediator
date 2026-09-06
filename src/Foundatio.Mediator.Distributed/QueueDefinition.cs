namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Describes a queue that the transport must provision or validate before workers start.
/// The settings mirror what the <see cref="QueueAttribute"/> on the handler declared so the
/// transport lock and the worker's renewal cadence always agree.
/// </summary>
public class QueueDefinition
{
    /// <summary>
    /// The fully prefixed queue name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// How long a received message stays invisible to other consumers before the transport
    /// redelivers it. Workers renew halfway through this interval while a handler is running.
    /// </summary>
    public TimeSpan VisibilityTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long an unconsumed message is retained. <c>null</c> keeps the transport default.
    /// </summary>
    public TimeSpan? MessageRetention { get; init; }

    /// <summary>
    /// The worker's maximum attempts. Transports that support native redrive policies should set
    /// their receive-count threshold above this so the library's own dead-lettering always runs first.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Whether a dead-letter queue named <see cref="DeadLetterQueueName"/> is expected to exist.
    /// </summary>
    public bool DeadLetterEnabled { get; init; } = true;

    /// <summary>
    /// The dead-letter queue name for this queue.
    /// </summary>
    public string DeadLetterQueueName => DeadLetterQueueNameFor(Name);

    /// <summary>
    /// Returns the conventional dead-letter queue name for a queue.
    /// </summary>
    public static string DeadLetterQueueNameFor(string queueName) => $"{queueName}-dead-letter";

    public override string ToString() => Name;
}
