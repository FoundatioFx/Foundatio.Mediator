namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Transport-agnostic contract for sending and receiving queue messages.
/// Implementations map to specific transports (in-memory, SQS, RabbitMQ, etc.).
/// </summary>
public interface IQueueClient : IAsyncDisposable
{
    /// <summary>
    /// Sends one or more messages to the specified queue.
    /// Implementations may use transport-native batch APIs for better throughput.
    /// </summary>
    Task SendAsync(string queueName, IReadOnlyList<QueueEntry> entries, CancellationToken cancellationToken = default);

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
    /// <remarks>
    /// Implementations should follow this convention:
    /// <list type="number">
    /// <item>Send a new message to <c>{queueName}-dead-letter</c> with the original body and headers.</item>
    /// <item>Add the metadata headers <see cref="MessageHeaders.DeadLetterReason"/>,
    ///   <see cref="MessageHeaders.DeadLetteredAt"/>, <see cref="MessageHeaders.OriginalQueueName"/>,
    ///   and <see cref="MessageHeaders.DeadLetterDequeueCount"/>.</item>
    /// <item>Complete (delete) the original message from the source queue.</item>
    /// </list>
    /// Transports that manage dead-letter queues natively (e.g., Azure Service Bus) may use
    /// native dead-letter operations instead, but must still preserve the metadata headers.
    /// </remarks>
    /// <param name="message">The message to dead-letter.</param>
    /// <param name="reason">A human-readable reason for dead-lettering.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the specified queues exist, creating them if necessary.
    /// Implementations may batch the operations for efficiency.
    /// </summary>
    Task EnsureQueuesAsync(IReadOnlyList<QueueDefinition> queues, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Gets transport-level statistics for the specified queues.
    /// Not all transports support all metrics; unsupported values will be zero.
    /// </summary>
    Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(IReadOnlyList<string> queueNames, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<QueueStats>>(queueNames.Select(n => new QueueStats { QueueName = n }).ToList());

    /// <inheritdoc />
    ValueTask IAsyncDisposable.DisposeAsync() => default;
}
