namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Transport-agnostic contract for sending and receiving queue messages.
/// Implementations map to specific transports (in-memory, SQS, RabbitMQ, etc.).
/// </summary>
public interface IQueueClient : IAsyncDisposable
{
    /// <summary>
    /// Sends a single message to the specified queue.
    /// </summary>
    Task SendAsync(string queueName, QueueEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a batch of messages to the specified queue.
    /// </summary>
    Task SendBatchAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives up to <paramref name="maxCount"/> messages from the specified queue.
    /// Returns an empty list when no messages are available after a transport-specific wait
    /// (e.g., SQS long-poll, RabbitMQ prefetch, in-memory channel wait).
    /// </summary>
    Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string queueName, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a message as successfully processed and removes it from the queue.
    /// </summary>
    Task CompleteAsync(QueueMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a message to the queue for reprocessing.
    /// When <paramref name="delay"/> is zero (default) the message becomes visible immediately;
    /// otherwise it remains invisible until the delay elapses. Used for retry backoff strategies.
    /// </summary>
    Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extends the visibility timeout of a message so it remains invisible to other consumers
    /// for an additional <paramref name="extension"/> duration. Used by long-running handlers
    /// to prevent the message from being redelivered while still processing.
    /// </summary>
    Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a message to the dead-letter queue for the specified queue.
    /// The message body and headers are preserved, with additional dead-letter metadata added.
    /// </summary>
    /// <param name="message">The message to dead-letter.</param>
    /// <param name="reason">A human-readable reason for dead-lettering.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the specified queues exist, creating them if necessary.
    /// Implementations may batch the operations for efficiency.
    /// </summary>
    Task EnsureQueuesAsync(IReadOnlyList<string> queueNames, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Gets transport-level statistics for the specified queue.
    /// Not all transports support all metrics; unsupported values will be zero.
    /// </summary>
    Task<QueueStats> GetQueueStatsAsync(string queueName, CancellationToken cancellationToken = default)
        => Task.FromResult(new QueueStats { QueueName = queueName });

    /// <inheritdoc />
    ValueTask IAsyncDisposable.DisposeAsync() => default;
}
