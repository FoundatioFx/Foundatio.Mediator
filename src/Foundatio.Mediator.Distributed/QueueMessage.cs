namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Represents a message that has been dequeued from a queue.
/// Contains the message body, headers, and metadata for tracking and lifecycle management.
/// </summary>
public sealed class QueueMessage
{
    /// <summary>
    /// Unique identifier for this message instance, assigned by the transport.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The serialized message body.
    /// </summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>
    /// Message headers / metadata. Well-known keys are defined in <see cref="MessageHeaders"/>.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>
    /// The name of the queue this message was received from.
    /// </summary>
    public required string QueueName { get; init; }

    /// <summary>
    /// The number of times this message has been dequeued (including the current attempt).
    /// Useful for detecting poison messages or implementing backoff strategies.
    /// </summary>
    public int DequeueCount { get; init; }

    /// <summary>
    /// When the message was originally enqueued.
    /// </summary>
    public DateTimeOffset EnqueuedAt { get; init; }

    /// <summary>
    /// When the message was dequeued for the current processing attempt.
    /// </summary>
    public DateTimeOffset DequeuedAt { get; init; }

    /// <summary>
    /// Transport-specific native message handle (e.g., SQS Message object).
    /// Used internally by <see cref="IQueueClient"/> implementations for lifecycle operations.
    /// </summary>
    public object? NativeMessage { get; init; }
}
