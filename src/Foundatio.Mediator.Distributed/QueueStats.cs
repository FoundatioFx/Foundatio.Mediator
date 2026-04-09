namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Transport-level statistics for a single queue.
/// </summary>
public sealed class QueueStats
{
    /// <summary>
    /// An empty stats instance with all counts at zero.
    /// </summary>
    public static QueueStats Empty { get; } = new() { QueueName = string.Empty };

    /// <summary>
    /// The name of the queue.
    /// </summary>
    public required string QueueName { get; init; }

    /// <summary>
    /// Approximate number of messages available for retrieval.
    /// </summary>
    public long ActiveCount { get; init; }

    /// <summary>
    /// Approximate number of messages in the dead-letter queue.
    /// </summary>
    public long DeadLetterCount { get; init; }

    /// <summary>
    /// Approximate number of messages currently being processed (in-flight).
    /// Not all transports support this metric.
    /// </summary>
    public long InFlightCount { get; init; }
}
